"""
VPU memory packet parser.
Ported from OpenKh.Ps2.VpuPacket.cs

After VIF unpacking, the 16 KB VU memory contains a structured packet:
all offsets in the header are in QW (quad-word = 16-byte) units.
"""
import struct

# Vertex function flags (index.function field)
FUNC_DRAW_DOUBLE_SIDED   = 0x00
FUNC_STOCK               = 0x10
FUNC_DRAW_TRIANGLE       = 0x20
FUNC_DRAW_TRIANGLE_INV   = 0x30


class VertexIndex:
    __slots__ = ("u", "v", "index", "function")
    def __init__(self, u, v, index, function):
        self.u = u; self.v = v; self.index = index; self.function = function


class VertexCoord:
    __slots__ = ("x", "y", "z", "w")
    def __init__(self, x, y, z, w):
        self.x = x; self.y = y; self.z = z; self.w = w


class VertexColor:
    __slots__ = ("r", "g", "b", "a")
    def __init__(self, r, g, b, a):
        self.r = r; self.g = g; self.b = b; self.a = a


class VertexNormal:
    __slots__ = ("x", "y", "z")
    def __init__(self, x, y, z):
        self.x = x; self.y = y; self.z = z


class VpuPacket:
    def __init__(self):
        self.indices = []         # list of VertexIndex
        self.vertices = []        # list of VertexCoord
        self.colors = []          # list of VertexColor
        self.normals = []         # list of VertexNormal
        self.vertex_range = []    # list of int (per-bone vertex count)
        self.weight_group_count = 0
        # weighted_indices[a][v] = list of (a+1) int vertex indices
        self.weighted_indices = []


def _read_int32(mem, offset):
    return struct.unpack_from("<i", mem, offset)[0]


def _read_float(mem, offset):
    return struct.unpack_from("<f", mem, offset)[0]


def _align16(pos):
    return (pos + 15) & ~15


def read_vpu_packet(mem: bytes) -> VpuPacket:
    """Parse VPU memory produced by VifUnpacker into a VpuPacket."""
    pkt = VpuPacket()

    # --- Header (16 int32s at byte 0) ---
    hdr = struct.unpack_from("<16i", mem, 0)
    # hdr[0]  = Type
    # hdr[1]  = VertexColorPtrInc
    # hdr[2]  = MagicNumber
    # hdr[3]  = VertexBufferPointer
    tri_strip_node_count  = hdr[4]
    tri_strip_node_offset = hdr[5]
    matrix_count_offset   = hdr[6]
    # hdr[7]  = MatrixOffset
    color_count           = hdr[8]
    color_offset          = hdr[9]
    weight_group_count    = hdr[10]
    weight_group_count_offset = hdr[11]
    vertex_coord_count    = hdr[12]
    vertex_coord_offset   = hdr[13]
    # hdr[14] = VertexIndexOffset
    matrix_count          = hdr[15]

    # Check for normals: if vertex_coord_offset != tri_strip_node_offset + index_count + color_count
    has_normals = (vertex_coord_offset !=
                   tri_strip_node_offset + tri_strip_node_count + color_count)
    normal_count = 0
    normal_offset = 0
    if has_normals:
        normal_count  = _read_int32(mem, 64)
        normal_offset = _read_int32(mem, 68)

    # --- VertexRange (int32 array) ---
    if matrix_count > 0:
        base = matrix_count_offset * 16
        pkt.vertex_range = [_read_int32(mem, base + i * 4) for i in range(matrix_count)]

    # --- VertexIndex array ---
    if tri_strip_node_count > 0:
        base = tri_strip_node_offset * 16
        for i in range(tri_strip_node_count):
            o = base + i * 16
            u, v, idx, fn = struct.unpack_from("<4i", mem, o)
            pkt.indices.append(VertexIndex(u, v, idx, fn))

    # --- VertexColor array ---
    if color_count > 0:
        base = color_offset * 16
        for i in range(color_count):
            o = base + i * 16
            r, g, b, a = struct.unpack_from("<4i", mem, o)
            pkt.colors.append(VertexColor(r, g, b, a))

    # --- VertexCoord array ---
    if vertex_coord_count > 0:
        base = vertex_coord_offset * 16
        for i in range(vertex_coord_count):
            o = base + i * 16
            x, y, z, w = struct.unpack_from("<4f", mem, o)
            pkt.vertices.append(VertexCoord(x, y, z, w))

    # --- VertexNormal array ---
    if has_normals and normal_count > 0:
        base = normal_offset * 16
        for i in range(normal_count):
            o = base + i * 16
            x, y, z = struct.unpack_from("<3f", mem, o)
            pkt.normals.append(VertexNormal(x, y, z))

    # --- WeightedIndices (multi-bone vertices) ---
    pkt.weight_group_count = weight_group_count
    if weight_group_count > 0:
        base = weight_group_count_offset * 16
        count_per_amount = [_read_int32(mem, base + i * 4) for i in range(weight_group_count)]
        pos = base + weight_group_count * 4
        groups = []
        for a, count in enumerate(count_per_amount):
            pos = _align16(pos)  # align to QW boundary before each group
            group = []
            for _ in range(count):
                entry = [_read_int32(mem, pos + j * 4) for j in range(a + 1)]
                pos += (a + 1) * 4
                group.append(entry)
            groups.append(group)
        pkt.weighted_indices = groups

    return pkt
