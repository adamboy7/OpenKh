"""
Blender import operator for KH2 MDLX files.
Registers under File > Import > KH2 MDLX (.mdlx).
"""

import os
import bpy
from bpy.props import StringProperty, BoolProperty
from bpy_extras.io_utils import ImportHelper


class ImportMDLX(bpy.types.Operator, ImportHelper):
    bl_idname  = "import_scene.kh2_mdlx"
    bl_label   = "Import KH2 MDLX"
    bl_options = {"REGISTER", "UNDO"}
    bl_description = "Import Kingdom Hearts 2 MDLX model file"

    filename_ext = ".mdlx"
    filter_glob: StringProperty(default="*.mdlx", options={"HIDDEN"})

    import_shadow: BoolProperty(
        name="Import Shadow Model",
        description="Also import the shadow sub-model (type 4) if present",
        default=False,
    )

    def execute(self, context):
        from .bar_reader      import read_bar, get_entries_by_type
        from .mdlx_parser     import (parse_mdlx_entity, build_tpose_matrices,
                                       process_vertices, TYPE_ENTITY, TYPE_SHADOW)
        from .texture_decoder import decode_model_textures
        from .blender_builder import build_armature, build_mesh

        filepath = self.filepath
        if not os.path.isfile(filepath):
            self.report({"ERROR"}, f"File not found: {filepath}")
            return {"CANCELLED"}

        with open(filepath, "rb") as fh:
            raw = fh.read()

        # --- Parse BAR container ---
        try:
            entries = read_bar(raw)
        except ValueError as exc:
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}

        model_entries = get_entries_by_type(entries, 4)  # Model
        tex_entries   = get_entries_by_type(entries, 7)  # ModelTexture

        if not model_entries:
            self.report({"ERROR"}, "No model data (BAR type 4) found in file")
            return {"CANCELLED"}

        # --- Decode textures ---
        textures = []
        if tex_entries:
            try:
                textures = decode_model_textures(tex_entries[0].data)
            except Exception as exc:
                self.report({"WARNING"}, f"Texture decode failed: {exc}")

        # --- Parse entity sub-models ---
        try:
            submodels = parse_mdlx_entity(model_entries[0].data)
        except Exception as exc:
            self.report({"ERROR"}, f"Model parse failed: {exc}")
            return {"CANCELLED"}

        # Filter by type
        wanted_types = {TYPE_ENTITY}
        if self.import_shadow:
            wanted_types.add(TYPE_SHADOW)
        submodels = [sm for sm in submodels if sm.type in wanted_types]

        if not submodels:
            self.report({"WARNING"}, "No entity sub-models found in file")
            return {"FINISHED"}

        name = os.path.splitext(os.path.basename(filepath))[0]

        # Create a dedicated collection
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)

        for sm_idx, sm in enumerate(submodels):
            sm_name = f"{name}" if len(submodels) == 1 else f"{name}_{sm_idx}"

            # T-pose matrices
            try:
                matrices = build_tpose_matrices(sm.bones)
            except Exception as exc:
                self.report({"WARNING"}, f"T-pose build failed for sub-model {sm_idx}: {exc}")
                matrices = []

            # Armature
            arm_obj = None
            if sm.bones and matrices:
                try:
                    arm_obj = build_armature(sm_name, sm.bones, matrices, col)
                except Exception as exc:
                    self.report({"WARNING"}, f"Armature build failed: {exc}")

            # Mesh
            try:
                mesh_parts = process_vertices(sm, matrices)
                if mesh_parts:
                    build_mesh(sm_name, mesh_parts, textures, arm_obj, col)
            except Exception as exc:
                self.report({"WARNING"}, f"Mesh build failed for sub-model {sm_idx}: {exc}")

        self.report({"INFO"}, f"Imported {name}: {len(submodels)} sub-model(s), "
                              f"{len(textures)} texture(s)")
        return {"FINISHED"}
