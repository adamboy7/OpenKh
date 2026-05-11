"""
Blender scene builder for KH2 MDLX models.
Creates armatures, skinned meshes, UV maps, and materials.
"""

import bpy
import mathutils


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def build_armature(name: str, bones: list, matrices: list, collection) -> bpy.types.Object:
    """
    Create a Blender armature from parsed KH2 bones and their T-pose matrices.
    `matrices` is a list of row-major 4x4 tuples (from mdlx_parser.build_tpose_matrices).
    """
    arm_data = bpy.data.armatures.new(name + "_Armature")
    arm_obj  = bpy.data.objects.new(name + "_Armature", arm_data)
    collection.objects.link(arm_obj)

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")

    bone_names = []
    for i, bone in enumerate(bones):
        bname = f"Bone_{i:03d}"
        eb = arm_data.edit_bones.new(bname)

        # Convert row-major 4x4 tuple → mathutils.Matrix (column-major)
        mat = _mat4_to_mathutils(matrices[i])
        eb.head = mat.translation
        # Tail: offset along the bone's local Z axis
        tail_offset = mat.to_3x3() @ mathutils.Vector((0.0, 0.0, 0.05))
        eb.tail = eb.head + tail_offset
        eb.use_inherit_rotation = True
        bone_names.append(bname)

    # Set parent relationships
    for i, bone in enumerate(bones):
        if bone.parent >= 0 and bone.parent < len(bone_names):
            arm_data.edit_bones[bone_names[i]].parent = \
                arm_data.edit_bones[bone_names[bone.parent]]

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def _mat4_to_mathutils(m):
    """Convert 4x4 tuple to mathutils.Matrix (translation in 4th column)."""
    return mathutils.Matrix((
        (m[0],  m[1],  m[2],  m[3]),
        (m[4],  m[5],  m[6],  m[7]),
        (m[8],  m[9],  m[10], m[11]),
        (m[12], m[13], m[14], m[15]),
    ))


# ---------------------------------------------------------------------------
# Mesh
# ---------------------------------------------------------------------------

def build_mesh(name: str, mesh_parts: list, textures: list,
               armature_obj, collection) -> bpy.types.Object:
    """
    Aggregate all MeshParts into a single Blender mesh object with UV map,
    vertex groups, and per-texture materials.
    """
    all_verts = []
    all_faces = []
    # UV per loop (face_vertex): indexed by [face_idx][corner]
    face_uvs = []
    face_mat_indices = []
    vert_bone_weights = []  # per vertex: [(bone_idx, weight), ...]

    mat_slot_map = {}   # texture_index → material slot index

    vert_offset = 0

    for part in mesh_parts:
        if not part.positions or not part.faces:
            continue

        # Positions
        for pos in part.positions:
            all_verts.append(pos)
        for bw in part.bone_weights:
            vert_bone_weights.append(bw)

        # Faces (vi, vj, vk) → absolute vertex indices; also store per-corner UVs
        for face_tuple in part.faces:
            vi_tuple = tuple(
                v[0] + vert_offset if isinstance(v, tuple) else v + vert_offset
                for v in face_tuple
            )
            # Each corner references a position index and a UV index
            corner_uvs = []
            for corner in face_tuple:
                pos_idx = corner[0] if isinstance(corner, tuple) else corner
                uv_idx  = corner[1] if isinstance(corner, tuple) else corner
                if uv_idx < len(part.uvs):
                    u, v = part.uvs[uv_idx]
                else:
                    u, v = 0.0, 0.0
                # Flip V: KH2 V=0 is top, Blender V=0 is bottom
                corner_uvs.append((u, 1.0 - v))
            all_faces.append(vi_tuple)
            face_uvs.append(corner_uvs)
            face_mat_indices.append(part.texture_index)

        vert_offset += len(part.positions)

    if not all_verts or not all_faces:
        return None

    mesh_data = bpy.data.meshes.new(name + "_Mesh")
    mesh_data.from_pydata(all_verts, [], all_faces)
    mesh_data.update()

    # --- Materials ---
    for part in mesh_parts:
        ti = part.texture_index
        if ti not in mat_slot_map:
            tex = textures[ti] if textures and ti < len(textures) else None
            mat = create_material(f"{name}_Mat{ti}", tex)
            mesh_data.materials.append(mat)
            mat_slot_map[ti] = len(mesh_data.materials) - 1

    for poly, mat_ti in zip(mesh_data.polygons, face_mat_indices):
        poly.material_index = mat_slot_map.get(mat_ti, 0)

    # --- UV layer ---
    uv_layer = mesh_data.uv_layers.new(name="UVMap")
    loop_idx = 0
    for poly, corner_uvs in zip(mesh_data.polygons, face_uvs):
        for i, loop in enumerate(poly.loop_indices):
            uv_layer.data[loop].uv = corner_uvs[i] if i < len(corner_uvs) else (0.0, 0.0)
            loop_idx += 1

    mesh_obj = bpy.data.objects.new(name, mesh_data)
    collection.objects.link(mesh_obj)

    # --- Armature modifier ---
    if armature_obj is not None:
        mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
        mod.object = armature_obj
        mesh_obj.parent = armature_obj

    # --- Vertex groups ---
    for vi, bw_list in enumerate(vert_bone_weights):
        for bone_idx, weight in bw_list:
            grp_name = f"Bone_{bone_idx:03d}"
            if grp_name not in mesh_obj.vertex_groups:
                mesh_obj.vertex_groups.new(name=grp_name)
            mesh_obj.vertex_groups[grp_name].add([vi], weight, "ADD")

    return mesh_obj


# ---------------------------------------------------------------------------
# Material / Texture
# ---------------------------------------------------------------------------

def create_material(name: str, texture) -> bpy.types.Material:
    """
    Create a Blender Principled BSDF material wired to a texture image.
    `texture` is a DecodedTexture or None (→ placeholder colour).
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    output   = nodes.new("ShaderNodeOutputMaterial")
    bsdf     = nodes.new("ShaderNodeBsdfPrincipled")
    tex_node = nodes.new("ShaderNodeTexImage")
    output.location   = (400, 0)
    bsdf.location     = (0, 0)
    tex_node.location = (-300, 0)

    if texture is not None:
        img = bpy.data.images.new(
            name + "_img",
            width=texture.width,
            height=texture.height,
            alpha=True,
        )
        # Convert RGBA8888 bytes to Blender float pixels (RGBA, 0.0–1.0)
        # Blender stores pixels bottom-to-top; KH2 textures are top-to-bottom
        w, h = texture.width, texture.height
        src = texture.pixels
        float_pixels = [0.0] * (w * h * 4)
        for row in range(h):
            src_row = (h - 1 - row)  # flip vertically
            for col in range(w):
                si = (src_row * w + col) * 4
                di = (row * w + col) * 4
                float_pixels[di]     = src[si]     / 255.0
                float_pixels[di + 1] = src[si + 1] / 255.0
                float_pixels[di + 2] = src[si + 2] / 255.0
                float_pixels[di + 3] = src[si + 3] / 255.0
        img.pixels = float_pixels
        img.pack()
        tex_node.image = img

    links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(tex_node.outputs["Alpha"], bsdf.inputs["Alpha"])
    links.new(bsdf.outputs["BSDF"],     output.inputs["Surface"])
    mat.blend_method = "HASHED"  # handle alpha
    return mat
