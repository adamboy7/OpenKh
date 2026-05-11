"""
KH2 MDLX Importer — Blender addon.

Installation:
  1. Zip the entire kh2_mdlx_importer/ folder.
  2. In Blender: Edit > Preferences > Add-ons > Install > select the zip.
  3. Enable "Import-Export: KH2 MDLX Importer".

Usage:
  File > Import > KH2 MDLX (.mdlx)
"""

bl_info = {
    "name":        "KH2 MDLX Importer",
    "author":      "OpenKH",
    "version":     (1, 0, 0),
    "blender":     (3, 0, 0),
    "location":    "File > Import > KH2 MDLX (.mdlx)",
    "description": "Import Kingdom Hearts 2 MDLX character/object model files",
    "category":    "Import-Export",
    "doc_url":     "https://github.com/Xeeynamo/OpenKh",
}

import bpy

from .io_import_mdlx import ImportMDLX


def _menu_func_import(self, context):
    self.layout.operator(ImportMDLX.bl_idname, text="KH2 MDLX (.mdlx)")


def register():
    bpy.utils.register_class(ImportMDLX)
    bpy.types.TOPBAR_MT_file_import.append(_menu_func_import)


def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(_menu_func_import)
    bpy.utils.unregister_class(ImportMDLX)
