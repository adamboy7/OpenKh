"""
MDLX entity model parser + T-pose matrix builder + mesh/bone-weight extractor.
Ported from:
  OpenKh.Kh2.Mdlx.Model.cs
  OpenKh.Engine.Parsers.MdlxParser.cs
  OpenKh.Engine.Parsers.Kkdf2MdlxParser.cs
"""
import struct
import math

from .vif_unpacker import VifUnpacker
from .vpu_packet import (
    read_vpu_packet,
    FUNC_STOCK,
    FUNC_DRAW_TRIANGLE,
    FUNC_DRAW_TRIANGLE_INV,
    FUNC_DRAW_DOUBLE_SIDED,
)

# SubModel types
TYPE_ENTITY = 3
TYPE_SHADOW = 4

RESERVED_AREA = 0x90  # bytes before first SubModel header in model data


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

class Bone:
    __slots__ = (
        "index", "parent", "unk08", "unk0c",
        "scale_x", "scale_y", "scale_z", "scale_w",
        "rot_x",   "rot_y",   "rot_z",   "rot_w",
        "trans_x", "trans_y", "trans_z", "trans_w",
    )

    def __init__(self, *args):
        for slot, val in zip(self.__slots__, args):
            setattr(self, slot, val)


class DmaVif:
    __slots__ = ("texture_index", "alaxi", "vif_packet", "base_address")

    def __init__(self, texture_index, alaxi, vif_packet, base_address):
        self.texture_index = texture_index
        self.alaxi = alaxi
        self.vif_packet = vif_packet
        self.base_address = base_address


class DmaChain:
    __slots__ = ("render_flags", "texture_index", "unk08", "dma_length", "dma_vifs")

    def __init__(self, render_flags, texture_index, unk08, dma_length, dma_vifs):
        self.render_flags = render_flags
        self.texture_index = texture_index
        self.unk08 = unk08
        self.dma_length = dma_length
        self.dma_vifs = dma_vifs

    @property
    def is_opaque(self):
        return (self.render_flags & 1) == 0


class SubModel:
    __slots__ = ("type", "unk04", "unk08", "bones", "unknown_data", "dma_chains")

    def __init__(self, type_, unk04, unk08, bones, unknown_data, dma_chains):
        self.type = type_
        self.unk04 = unk04
        self.unk08 = unk08
        self.bones = bones
        self.unknown_data = unknown_data
        self.dma_chains = dma_chains


class MeshPart:
    """One renderable group: positions, UVs, triangles, bone weights."""
    __slots__ = ("texture_index", "is_opaque", "positions", "uvs", "bone_weights", "faces")

    def __init__(self, texture_index, is_opaque):
        self.texture_index = texture_index
        self.is_opaque = is_opaque
        self.positions = []       # list of (x, y, z)
        self.uvs = []             # list of (u, v) — one per vertex
        self.bone_weights = []    # list of [(bone_idx, weight)] per vertex
        self.faces = []           # list of (i, j, k) vertex indices


# ---------------------------------------------------------------------------
# Binary reading helpers
# ---------------------------------------------------------------------------

def _ri32(data, offset):
    return struct.unpack_from("<i", data, offset)[0]

def _ri16(data, offset):
    return struct.unpack_from("<h", data, offset)[0]

def _rf(data, offset):
    return struct.unpack_from("<f", data, offset)[0]

def _ru32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]

def _ru16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


# ---------------------------------------------------------------------------
# Bone parsing
# ---------------------------------------------------------------------------

def _read_bone(data, offset):
    index  = _ri32(data, offset)
    parent = _ri32(data, offset + 4)
    unk08  = _ri32(data, offset + 8)
    unk0c  = _ri32(data, offset + 12)
    sx, sy, sz, sw = struct.unpack_from("<4f", data, offset + 16)
    rx, ry, rz, rw = struct.unpack_from("<4f", data, offset + 32)
    tx, ty, tz, tw = struct.unpack_from("<4f", data, offset + 48)
    return Bone(index, parent, unk08, unk0c,
                sx, sy, sz, sw,
                rx, ry, rz, rw,
                tx, ty, tz, tw)


# ---------------------------------------------------------------------------
# DMA chain parsing (ported from ReadDmaChain in Mdlx.Model.cs)
# ---------------------------------------------------------------------------

def _read_dma_chain(data, base, render_flags, texture_index, unk08, dma_length,
                    dma_offset, count1a_offset):
    """
    Parse one DMA chain from the SubModel data.
    `base` is the absolute byte offset of the SubModel start within `data`.
    All offsets in the chain headers are relative to `base`.
    """
    # --- Read alv1 list (bone index list with -1 separators) ---
    ca_pos = base + count1a_offset
    count1a = _ri32(data, ca_pos)
    alv1 = [_ri32(data, ca_pos + 4 + i * 4) for i in range(count1a)]

    # --- Reconstruct DMA offset list and alaxi groups ---
    dma_offsets = [dma_offset]
    alaxi_groups = []
    alaxref = []
    dma_base = dma_offset + 0x10  # start one DMA packet in

    for v in alv1:
        if v == -1:
            dma_offsets.append(dma_base + 0x10)
            dma_base += 0x20
            alaxi_groups.append(list(alaxref))
            alaxref = []
        else:
            dma_base += 0x10
            alaxref.append(v)
    alaxi_groups.append(list(alaxref))  # last group (no trailing -1)

    # --- Extract VIF packets ---
    dma_vifs = []
    for i, dma_off in enumerate(dma_offsets):
        alaxi = alaxi_groups[i] if i < len(alaxi_groups) else []

        # Read DMA packet chain: 16 bytes each, stop when qwc == 0
        chain = []
        pos = base + dma_off
        while True:
            qwc  = _ru16(data, pos)
            addr = _ru32(data, pos + 4)
            param = _ri32(data, pos + 12)
            chain.append((qwc, addr, param))
            pos += 16
            if qwc == 0:
                break

        first_qwc, first_addr, _ = chain[0]
        if first_qwc == 0:
            continue  # empty / close packet only

        # VIF1_Tops from second packet if present
        ba = chain[1][2] if len(chain) >= 2 and first_qwc > 0 else 0

        vif_start = base + (first_addr & 0x7FFFFFFF)
        vif_bytes = data[vif_start: vif_start + first_qwc * 16]

        dma_vifs.append(DmaVif(texture_index, alaxi, vif_bytes, ba))

    return DmaChain(render_flags, texture_index, unk08, dma_length, dma_vifs)


# ---------------------------------------------------------------------------
# SubModel parsing
# ---------------------------------------------------------------------------

def _read_submodel(data, base):
    """
    Parse one SubModel starting at `base`.
    Returns (next_offset, SubModel).
    """
    type_  = _ri32(data, base + 0)
    unk04  = _ri32(data, base + 4)
    unk08  = _ri32(data, base + 8)
    next_offset = _ri32(data, base + 12)
    bone_count  = _ri16(data, base + 16)
    bone_offset     = _ri32(data, base + 20)
    unk_data_offset = _ri32(data, base + 24)
    dma_chain_count = _ri32(data, base + 28)

    # --- DmaChainHeaders (0x20 bytes each, right after SubModelHeader) ---
    chain_headers = []
    for i in range(dma_chain_count):
        o = base + 0x20 + i * 0x20
        render_flags  = _ri32(data, o)
        tex_idx       = _ri32(data, o + 4)
        u08           = _ri32(data, o + 8)
        # o+12 unused
        dma_off       = _ri32(data, o + 16)
        c1a_off       = _ri32(data, o + 20)
        dma_len       = _ri32(data, o + 24)
        chain_headers.append((render_flags, tex_idx, u08, dma_len, dma_off, c1a_off))

    # --- Unknown data ---
    unk_data = b""
    if type_ == TYPE_ENTITY and unk_data_offset:
        unk_data = data[base + unk_data_offset: base + unk_data_offset + 0x120]

    # --- Bones ---
    bones = []
    if bone_offset and bone_count > 0:
        for i in range(bone_count):
            bones.append(_read_bone(data, base + bone_offset + i * 64))

    # --- DMA chains ---
    dma_chains = []
    for (rf, ti, u08, dl, dma_off, c1a_off) in chain_headers:
        dc = _read_dma_chain(data, base, rf, ti, u08, dl, dma_off, c1a_off)
        dma_chains.append(dc)

    return next_offset, SubModel(type_, unk04, unk08, bones, unk_data, dma_chains)


def parse_mdlx_entity(model_data: bytes) -> list:
    """
    Parse all SubModels from the raw model data bytes (BAR type-4 entry).
    Returns list of SubModel objects.
    """
    current_offset = 0
    next_offset = RESERVED_AREA
    submodels = []

    while next_offset != 0:
        current_offset += next_offset
        if current_offset >= len(model_data):
            break
        next_offset, sm = _read_submodel(model_data, current_offset)
        submodels.append(sm)

    return submodels


# ---------------------------------------------------------------------------
# T-pose matrix building (ported from MdlxParser.BuildTPoseMatrices)
# Uses plain Python math (no mathutils dependency) — returns list of 4x4 tuples
# ---------------------------------------------------------------------------

def _quat_identity():
    return (0.0, 0.0, 0.0, 1.0)  # (x, y, z, w)

def _quat_from_axis_angle(ax, ay, az, angle):
    s = math.sin(angle * 0.5)
    c = math.cos(angle * 0.5)
    return (ax * s, ay * s, az * s, c)

def _quat_mul(q1, q2):
    x1, y1, z1, w1 = q1
    x2, y2, z2, w2 = q2
    return (
        w1*x2 + x1*w2 + y1*z2 - z1*y2,
        w1*y2 - x1*z2 + y1*w2 + z1*x2,
        w1*z2 + x1*y2 - y1*x2 + z1*w2,
        w1*w2 - x1*x2 - y1*y2 - z1*z2,
    )

def _quat_rotate_vec3(q, v):
    """Rotate vector v by quaternion q."""
    qx, qy, qz, qw = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v)
    tx = 2.0 * (qy * vz - qz * vy)
    ty = 2.0 * (qz * vx - qx * vz)
    tz = 2.0 * (qx * vy - qy * vx)
    return (
        vx + qw * tx + qy * tz - qz * ty,
        vy + qw * ty + qz * tx - qx * tz,
        vz + qw * tz + qx * ty - qy * tx,
    )

def _quat_to_matrix(q):
    """Convert quaternion to 3x3 rotation matrix (row-major)."""
    x, y, z, w = q
    xx, yy, zz = x*x, y*y, z*z
    xy, xz, yz = x*y, x*z, y*z
    wx, wy, wz = w*x, w*y, w*z
    return (
        1-2*(yy+zz), 2*(xy-wz),  2*(xz+wy),
        2*(xy+wz),   1-2*(xx+zz),2*(yz-wx),
        2*(xz-wy),   2*(yz+wx),  1-2*(xx+yy),
    )

def build_tpose_matrices(bones: list) -> list:
    """
    Build a list of 4x4 world-space matrices for each bone in T-pose.
    Returns list of (m00,m01,m02,m03, m10,m11,m12,m13, m20,m21,m22,m23, 0,0,0,1) tuples.
    """
    n = len(bones)
    abs_rot   = [_quat_identity() for _ in range(n)]
    abs_trans = [(0.0, 0.0, 0.0)] * n

    for i, bone in enumerate(bones):
        parent = bone.parent
        if parent < 0:
            pr = _quat_identity()
            pt = (0.0, 0.0, 0.0)
        else:
            pr = abs_rot[parent]
            pt = abs_trans[parent]

        # Rotate local translation by parent absolute rotation
        lt = _quat_rotate_vec3(pr, (bone.trans_x, bone.trans_y, bone.trans_z))
        abs_trans[i] = (pt[0] + lt[0], pt[1] + lt[1], pt[2] + lt[2])

        # Build local rotation: ZYX order
        lr = _quat_identity()
        if bone.rot_z != 0.0:
            lr = _quat_mul(lr, _quat_from_axis_angle(0, 0, 1, bone.rot_z))
        if bone.rot_y != 0.0:
            lr = _quat_mul(lr, _quat_from_axis_angle(0, 1, 0, bone.rot_y))
        if bone.rot_x != 0.0:
            lr = _quat_mul(lr, _quat_from_axis_angle(1, 0, 0, bone.rot_x))
        abs_rot[i] = _quat_mul(pr, lr)

    # Build final 4x4 matrices (row-major, translation in last column)
    matrices = []
    for i in range(n):
        m = _quat_to_matrix(abs_rot[i])
        tx, ty, tz = abs_trans[i]
        # Row-major 4x4: rotation columns + translation
        matrices.append((
            m[0], m[1], m[2], tx,
            m[3], m[4], m[5], ty,
            m[6], m[7], m[8], tz,
            0.0,  0.0,  0.0,  1.0,
        ))
    return matrices


def _transform_point(mat4, vx, vy, vz):
    """Multiply mat4 (row-major 4x4 tuple) by point (vx, vy, vz)."""
    wx = mat4[0]*vx + mat4[1]*vy + mat4[2]*vz + mat4[3]
    wy = mat4[4]*vx + mat4[5]*vy + mat4[6]*vz + mat4[7]
    wz = mat4[8]*vx + mat4[9]*vy + mat4[10]*vz + mat4[11]
    return (wx, wy, wz)


# ---------------------------------------------------------------------------
# Vertex bone-weight extraction (ported from Kkdf2MdlxParser)
# ---------------------------------------------------------------------------

def _get_vertex_assignments(vpu, alaxi):
    """
    For each logical vertex, return a list of (global_bone_index, vertex_coord_index) pairs.
    Single-bone vertices: one entry per vertex using alaxi[i] as bone.
    Multi-bone vertices: from vpu.weighted_indices.
    """
    assignments = []
    j = 0
    for bone_slot, count in enumerate(vpu.vertex_range):
        global_bone = alaxi[bone_slot] if bone_slot < len(alaxi) else 0
        for _ in range(count):
            assignments.append([(global_bone, j)])
            j += 1

    # Override with multi-bone assignments if present
    if vpu.weighted_indices:
        # weighted_indices[a][v] = list of (a+1) vertex-coord indices
        # Rebuild assignments list in order
        multi = []
        for a, group in enumerate(vpu.weighted_indices):
            for desc in group:
                # desc = list of vertex indices; a+1 bones involved
                # Map each vertex index to its bone via existing single assignments
                pairs = []
                for vi in desc:
                    if vi < len(assignments) and assignments[vi]:
                        pairs.append((assignments[vi][0][0], vi))
                    else:
                        pairs.append((0, vi))
                multi.append(pairs)
        # Replace assignments with multi (they should be the same count)
        if len(multi) == len(assignments):
            assignments = multi

    return assignments


# ---------------------------------------------------------------------------
# Mesh processing (triangle strip → triangle list + world positions)
# ---------------------------------------------------------------------------

def process_vertices(submodel: SubModel, matrices: list) -> list:
    """
    Process all DMA chains in a SubModel into MeshPart objects.
    Each MeshPart has world-space vertex positions, UV coords, bone weights, and triangle faces.
    """
    all_parts = []

    for chain in submodel.dma_chains:
        part = MeshPart(chain.texture_index, chain.is_opaque)

        vertex_base = 0
        uv_base = 0
        ring = [None, None, None, None]
        ring_index = 0
        # Kkdf2 triangle order: offsets [1,3,2] from current ring head
        TORDER = [1, 3, 2]

        all_positions = []
        all_weights = []
        all_uvs_flat = []

        for dma_vif in chain.dma_vifs:
            if not dma_vif.vif_packet:
                continue

            unpacker = VifUnpacker(dma_vif.vif_packet)
            unpacker.run()
            vpu = read_vpu_packet(unpacker.memory)

            if not vpu.indices or not vpu.vertices:
                continue

            # Build vertex assignments (bone index, vertex coord index)
            assignments = _get_vertex_assignments(vpu, dma_vif.alaxi)

            # Compute world positions for each vertex coord
            verts_world = []
            weights_per_vert = []
            for va_list in assignments:
                if len(va_list) == 1:
                    bone_idx, vi = va_list[0]
                    vc = vpu.vertices[vi]
                    if bone_idx < len(matrices):
                        pos = _transform_point(matrices[bone_idx], vc.x, vc.y, vc.z)
                    else:
                        pos = (vc.x, vc.y, vc.z)
                    verts_world.append(pos)
                    weights_per_vert.append([(bone_idx, 1.0)])
                else:
                    # Multi-bone: blend
                    px = py = pz = 0.0
                    w_list = []
                    for bone_idx, vi in va_list:
                        vc = vpu.vertices[vi]
                        weight = vc.w
                        if bone_idx < len(matrices):
                            tp = _transform_point(matrices[bone_idx],
                                                  vc.x * weight, vc.y * weight, vc.z * weight)
                        else:
                            tp = (vc.x * weight, vc.y * weight, vc.z * weight)
                        px += tp[0]; py += tp[1]; pz += tp[2]
                        w_list.append((bone_idx, weight))
                    verts_world.append((px, py, pz))
                    weights_per_vert.append(w_list)

            all_positions.extend(verts_world)
            all_weights.extend(weights_per_vert)

            # UV from indices array (U/V are int32 in 0–4096 range → divide by 4096)
            uvs_this = [(idx.u / 4096.0, idx.v / 4096.0) for idx in vpu.indices]
            all_uvs_flat.extend(uvs_this)

            # Triangle strip via ring buffer
            for x, index_entry in enumerate(vpu.indices):
                global_vi = vertex_base + index_entry.index
                uv_vi = uv_base + x
                ring[ring_index & 3] = (global_vi, uv_vi)
                ring_index += 1

                fn = index_entry.function
                if fn in (FUNC_DRAW_TRIANGLE, FUNC_DRAW_DOUBLE_SIDED):
                    t = (
                        ring[(ring_index - TORDER[0]) & 3],
                        ring[(ring_index - TORDER[1]) & 3],
                        ring[(ring_index - TORDER[2]) & 3],
                    )
                    if None not in t:
                        part.faces.append(t)
                if fn in (FUNC_DRAW_TRIANGLE_INV, FUNC_DRAW_DOUBLE_SIDED):
                    t = (
                        ring[(ring_index - TORDER[0]) & 3],
                        ring[(ring_index - TORDER[2]) & 3],
                        ring[(ring_index - TORDER[1]) & 3],
                    )
                    if None not in t:
                        part.faces.append(t)

            vertex_base += len(verts_world)
            uv_base += len(uvs_this)

        part.positions = all_positions
        part.uvs = all_uvs_flat
        part.bone_weights = all_weights
        all_parts.append(part)

    return all_parts
