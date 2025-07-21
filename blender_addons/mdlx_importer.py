bl_info = {
    "name": "OpenKH MDLX Importer",
    "author": "OpenAI Codex",
    "blender": (2, 80, 0),
    "version": (0, 1),
    "location": "File > Import > Kingdom Hearts MDLX",
    "description": "Import Kingdom Hearts MDLX models",
    "category": "Import-Export",
}

import bpy
import struct
import io
from mathutils import Matrix, Vector, Quaternion
from bpy_extras.io_utils import ImportHelper

# -----------------------------------------------------
# Utility classes to parse BAR and MDLX structures
# -----------------------------------------------------

def read_u16(f):
    return struct.unpack('<H', f.read(2))[0]

def read_u32(f):
    return struct.unpack('<I', f.read(4))[0]

def read_s16(f):
    return struct.unpack('<h', f.read(2))[0]

def read_s32(f):
    return struct.unpack('<i', f.read(4))[0]

def read_f32(f):
    return struct.unpack('<f', f.read(4))[0]

class BarEntry:
    def __init__(self, etype, duplicate, name, offset, size):
        self.type = etype
        self.duplicate = duplicate
        self.name = name
        self.offset = offset
        self.size = size

class BarFile:
    def __init__(self, data: bytes):
        self.entries = []
        f = memoryview(data)
        pos = 0
        magic = f[pos:pos+4].tobytes(); pos += 4
        count = struct.unpack_from('<I', f, pos)[0]; pos += 4
        pos += 8
        for _ in range(count):
            etype, dup = struct.unpack_from('<HH', f, pos); pos += 4
            name = f[pos:pos+4].tobytes().decode('ascii').rstrip('\0'); pos += 4
            off, size = struct.unpack_from('<II', f, pos); pos += 8
            self.entries.append(BarEntry(etype, dup, name, off, size))
        self.data = data

    def entry(self, etype):
        for e in self.entries:
            if e.type == etype:
                return self.data[e.offset:e.offset+e.size]
        return None

# -----------------------------------------------------
# Very small subset of the MDLX format parser
# -----------------------------------------------------
class MdlxModel:
    class Bone:
        def __init__(self, f):
            self.index = read_u16(f)
            f.read(2)
            self.parent = read_s32(f)
            f.read(8)
            self.scale = (read_f32(f), read_f32(f), read_f32(f), read_f32(f))
            self.rot = (read_f32(f), read_f32(f), read_f32(f), read_f32(f))
            self.trans = (read_f32(f), read_f32(f), read_f32(f), read_f32(f))

    def __init__(self, data: bytes):
        f = memoryview(data)
        stream = memoryview(data)
        pos = 0
        pos = 0x90
        version = struct.unpack_from('<I', stream, pos)[0]; pos += 4
        pos += 8
        next_hdr = struct.unpack_from('<I', stream, pos)[0]; pos += 4
        bone_count = struct.unpack_from('<H', stream, pos)[0]; pos += 2
        pos += 2
        bone_offset = struct.unpack_from('<I', stream, pos)[0]; pos += 4
        unknown_offset = struct.unpack_from('<I', stream, pos)[0]; pos += 4
        subpart_count = struct.unpack_from('<H', stream, pos)[0]; pos += 2
        pos += 2

        fobj = memoryview(data)
        bones = []
        p = bone_offset
        for i in range(bone_count):
            bone_bytes = fobj[p:p+0x40].tobytes()
            bones.append(MdlxModel.Bone(io.BytesIO(bone_bytes)))
            p += 0x40
        self.bones = bones
        # NOTE: Mesh parsing is omitted in this simplified example
        self.meshes = []

class MdlxFile:
    def __init__(self, path):
        with open(path, 'rb') as fin:
            data = fin.read()
        bar = BarFile(data)
        model_data = bar.entry(4)
        if model_data:
            self.model = MdlxModel(model_data)
        else:
            raise ValueError("No model data in MDLX")
        self.textures = []  # TODO: parse TM2 textures

# -----------------------------------------------------
# Blender import operator
# -----------------------------------------------------
class IMPORT_OT_mdlx(bpy.types.Operator, ImportHelper):
    bl_idname = "import_scene.openkh_mdlx"
    bl_label = "Import MDLX"
    filename_ext = ".mdlx"

    filter_glob: bpy.props.StringProperty(default="*.mdlx", options={'HIDDEN'})

    def execute(self, context):
        mdlx = MdlxFile(self.filepath)
        self._create_armature(context, mdlx.model)
        # Mesh import not implemented in this simplified example
        return {'FINISHED'}

    def _create_armature(self, context, model: MdlxModel):
        arm_data = bpy.data.armatures.new("MDLXArmature")
        arm_obj = bpy.data.objects.new("MDLXArmature", arm_data)
        context.collection.objects.link(arm_obj)
        bpy.context.view_layer.objects.active = arm_obj
        bpy.ops.object.mode_set(mode='EDIT')

        edit_bones = arm_obj.data.edit_bones
        bone_map = {}
        for b in model.bones:
            bone = edit_bones.new(f"bone_{b.index}")
            head = Vector((b.trans[0], b.trans[1], b.trans[2]))
            bone.head = head
            bone.tail = head + Vector((0, 0.1, 0))
            bone_map[b.index] = bone

        for b in model.bones:
            if b.parent >= 0:
                parent = bone_map.get(b.parent)
                if parent:
                    bone_map[b.index].parent = parent
        bpy.ops.object.mode_set(mode='OBJECT')

class MDLX_PT_import_panel(bpy.types.Panel):
    bl_label = "OpenKH MDLX Import"
    bl_idname = "MDLX_PT_import_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'MDLX'

    def draw(self, context):
        layout = self.layout
        layout.operator(IMPORT_OT_mdlx.bl_idname, text="Import MDLX")

classes = [IMPORT_OT_mdlx, MDLX_PT_import_panel]

def menu_func_import(self, context):
    self.layout.operator(IMPORT_OT_mdlx.bl_idname, text="Kingdom Hearts MDLX")

def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)


def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    for cls in classes:
        bpy.utils.unregister_class(cls)

if __name__ == "__main__":
    register()
