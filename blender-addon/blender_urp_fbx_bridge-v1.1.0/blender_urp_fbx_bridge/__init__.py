# SPDX-License-Identifier: MIT
# Blender URP FBX Material Bridge
# v1.1.0 - URP/Lit shader support
# Exports: FBX + .materials.json + Textures + _report

bl_info = {
    "name": "Unity URP FBX Material Bridge",
    "author": "Matsubayashi Ayaka",
    "version": (1, 1, 0),
    "blender": (3, 6, 0),
    "location": "View3D > Sidebar > Unity Export",
    "description": "Export selected meshes as FBX + URP manifest + texture folder for Unity importer.",
    "warning": "",
    "category": "Import-Export",
}

import bpy
import os
import json
import shutil
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from bpy.types import Operator, Panel, PropertyGroup
from bpy.props import StringProperty, PointerProperty, BoolProperty


# -----------------------------
# Color Space Conversion
# -----------------------------

def linear_to_srgb_channel(c: float) -> float:
    if c <= 0.0:
        return 0.0
    elif c < 0.0031308:
        return 12.92 * c
    elif c < 1.0:
        return 1.055 * pow(c, 1.0 / 2.4) - 0.055
    else:
        return 1.0


def linear_to_srgb_rgba(linear_rgba: tuple) -> tuple:
    r, g, b = linear_rgba[0], linear_rgba[1], linear_rgba[2]
    a = linear_rgba[3] if len(linear_rgba) >= 4 else 1.0
    return (
        linear_to_srgb_channel(r),
        linear_to_srgb_channel(g),
        linear_to_srgb_channel(b),
        a
    )


# -----------------------------
# Data (Manifest v1.1.0)
# -----------------------------

def iso_now_local() -> str:
    try:
        dt = datetime.now().astimezone()
        return dt.isoformat(timespec="seconds")
    except Exception:
        return datetime.now().isoformat(timespec="seconds")


def sanitize_filename(name: str) -> str:
    invalid = '<>:"/\\|?*\0'
    for ch in invalid:
        name = name.replace(ch, "_")
    name = name.strip()
    return name if name else "Unnamed"


def ensure_dir(p: Path):
    p.mkdir(parents=True, exist_ok=True)


def write_text(p: Path, text: str):
    ensure_dir(p.parent)
    p.write_text(text, encoding="utf-8")


def write_json(p: Path, data):
    ensure_dir(p.parent)
    p.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def rel_path(from_dir: Path, to_path: Path) -> str:
    return to_path.relative_to(from_dir).as_posix()


# -----------------------------
# Node utilities
# -----------------------------

def find_active_output_node(nt: bpy.types.NodeTree):
    outs = [n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"]
    for n in outs:
        if getattr(n, "is_active_output", False):
            return n
    return outs[0] if outs else None


def socket_linked_node(sock):
    if not sock or not sock.is_linked:
        return None
    link = sock.links[0]
    return link.from_node


def find_principled_node(mat: bpy.types.Material):
    if not mat.use_nodes or not mat.node_tree:
        return None, "Material has no node tree."
    nt = mat.node_tree
    out = find_active_output_node(nt)
    if out and out.inputs.get("Surface") and out.inputs["Surface"].is_linked:
        n = socket_linked_node(out.inputs["Surface"])
        if n and n.type == "BSDF_PRINCIPLED":
            return n, None
    for n in nt.nodes:
        if n.type == "BSDF_PRINCIPLED":
            return n, "Principled BSDF not directly connected to active output (fallback to first found)."
    return None, "No Principled BSDF found."


def _first_link(sock):
    if not sock or not sock.is_linked or not sock.links:
        return None, None, None
    link = sock.links[0]
    return link.from_node, link.from_socket, link


def _find_group_output_node(nt: bpy.types.NodeTree):
    outs = [n for n in nt.nodes if n.type == "GROUP_OUTPUT"]
    for n in outs:
        if getattr(n, "is_active_output", False):
            return n
    return outs[0] if outs else None


def _map_group_output_input(group_node, output_socket):
    nt = group_node.node_tree
    if not nt:
        return None, "Group node has no node tree."
    group_out = _find_group_output_node(nt)
    if not group_out:
        return None, "Group node tree has no Group Output node."
    inner = group_out.inputs.get(output_socket.name)
    if not inner:
        return None, f"Group Output has no input '{output_socket.name}'."
    return inner, None


def resolve_image_socket(sock, report_notes: list, depth=0, max_group_depth=1):
    if not sock or not sock.is_linked:
        return None, None
    from_node, from_socket, link = _first_link(sock)
    if not from_node:
        return None, None

    if from_node.type == "TEX_IMAGE":
        img = from_node.image
        if img:
            return img, None
        return None, f"Image Texture node '{from_node.name}' has no image assigned."

    if from_node.type == "REROUTE":
        return resolve_image_socket(from_node.inputs[0], report_notes, depth=depth, max_group_depth=max_group_depth)

    if from_node.type == "GROUP":
        if depth >= max_group_depth:
            return None, "Group depth exceeded (max 1)."
        inner_sock, note = _map_group_output_input(from_node, from_socket)
        if note:
            return None, note
        img, note2 = resolve_image_socket(inner_sock, report_notes, depth=depth+1, max_group_depth=max_group_depth)
        if img and not note2:
            report_notes.append(f"[GroupResolved] '{from_node.name}' -> image")
        return img, note2

    return None, f"Unsupported node feeding '{sock.name}': {from_node.type}"


def resolve_color_constant(sock, report_notes: list, depth=0, max_group_depth=1):
    if not sock or not sock.is_linked:
        return None, None
    from_node, from_socket, link = _first_link(sock)
    if not from_node:
        return None, None

    if from_node.type == "RGB":
        try:
            v = from_node.outputs[0].default_value
            linear_col = (float(v[0]), float(v[1]), float(v[2]), float(v[3]))
            return linear_to_srgb_rgba(linear_col), None
        except Exception:
            return None, "RGB node default_value not readable."

    if from_node.type == "REROUTE":
        return resolve_color_constant(from_node.inputs[0], report_notes, depth=depth, max_group_depth=max_group_depth)

    if from_node.type == "GROUP":
        if depth >= max_group_depth:
            return None, "Group depth exceeded (max 1)."
        inner_sock, note = _map_group_output_input(from_node, from_socket)
        if note:
            return None, note
        col, note2 = resolve_color_constant(inner_sock, report_notes, depth=depth+1, max_group_depth=max_group_depth)
        if col is not None and not note2:
            report_notes.append(f"[GroupResolved] '{from_node.name}' -> color constant")
        return col, note2

    return None, f"Unsupported node feeding '{sock.name}' for constant color: {from_node.type}"


# -----------------------------
# Texture export
# -----------------------------

def copy_or_bake_texture(img, dest_folder: Path, mat_name: str, channel_hint: str, report_notes: list):
    if not img:
        return None

    safe_mat_name = sanitize_filename(mat_name)

    if img.packed_file:
        ext = ".png"
        if img.file_format:
            fmt = img.file_format.upper()
            if fmt in ("JPEG", "JPG"):
                ext = ".jpg"
            elif fmt == "PNG":
                ext = ".png"
            elif fmt == "TARGA":
                ext = ".tga"
        out_name = f"{safe_mat_name}_{channel_hint}{ext}"
        out_path = dest_folder / out_name
        try:
            original_path = img.filepath_raw
            img.filepath_raw = str(out_path)
            img.save()
            img.filepath_raw = original_path
            report_notes.append(f"[TexturePacked] {img.name} -> {out_name}")
            return out_path
        except Exception as e:
            report_notes.append(f"[TexturePacked] Failed to save packed image '{img.name}': {e}")
            return None
    else:
        src = bpy.path.abspath(img.filepath)
        if not src or not os.path.isfile(src):
            report_notes.append(f"[Texture] '{img.name}' file not found: {src}")
            return None
        ext = Path(src).suffix.lower() or ".png"
        out_name = f"{safe_mat_name}_{channel_hint}{ext}"
        out_path = dest_folder / out_name
        try:
            shutil.copy2(src, out_path)
            report_notes.append(f"[TextureCopied] {img.name} -> {out_name}")
            return out_path
        except Exception as e:
            report_notes.append(f"[TextureCopy] failed '{img.name}' -> {out_name}: {e}")
            return None


# -----------------------------
# Gather selected mesh
# -----------------------------

def gather_selected_mesh_objects(context):
    objs = [o for o in context.selected_objects if o.type == "MESH"]
    return objs


# -----------------------------
# FBX export
# -----------------------------

def export_fbx_selected(path: Path, axis_forward: str, axis_up: str, report_notes: list):
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(path),
            use_selection=True,
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_ALL",
            axis_forward=axis_forward,
            axis_up=axis_up,
            bake_space_transform=True,
            object_types={'MESH', 'ARMATURE'},
            use_mesh_modifiers=True,
            mesh_smooth_type='FACE',
            add_leaf_bones=False,
            path_mode='COPY',
            embed_textures=False,
        )
        report_notes.append(f"[FBX] Exported: {path.name}")
        return True
    except Exception as e:
        report_notes.append(f"[FBX] Export failed: {e}")
        return False


# -----------------------------
# Manifest builder
# -----------------------------

def build_manifest(asset_name, meshes, materials, axis_forward, axis_up):
    return {
        "manifest_version": "1.1.0",
        "pipeline": "UnityURP",
        "asset": {
            "name": asset_name,
            "export_time_iso": iso_now_local(),
            "blender_version": ".".join(str(x) for x in bpy.app.version),
            "unit_scale": 1.0,
            "axis": {
                "forward": axis_forward,
                "up": axis_up,
            }
        },
        "meshes": meshes,
        "materials": materials,
    }


# -----------------------------
# Material entry builder
# -----------------------------

def make_material_entry(mat: bpy.types.Material, textures_dir: Path, asset_dir: Path, report_notes: list):
    entry_notes = []
    tex_refs = {}
    base_factor = (1.0, 1.0, 1.0, 1.0)
    metallic_val = 0.0
    roughness_val = 0.5
    emission_color = (0.0, 0.0, 0.0, 1.0)
    emission_strength = 1.0

    principled, note = find_principled_node(mat)
    if note:
        entry_notes.append(note)

    if principled:
        # Base Color
        bc_socket = principled.inputs.get("Base Color")
        if bc_socket:
            img, note = resolve_image_socket(bc_socket, entry_notes)
            if note:
                entry_notes.append(note)
            if img:
                out_path = copy_or_bake_texture(img, textures_dir, mat.name, "BaseColor", entry_notes)
                if out_path:
                    tex_refs["base_color"] = {"path": rel_path(asset_dir, out_path), "srgb": True}
            if bc_socket.is_linked:
                col, note2 = resolve_color_constant(bc_socket, entry_notes)
                if col is not None:
                    base_factor = col
                elif note2:
                    entry_notes.append(note2)
            else:
                try:
                    dv = bc_socket.default_value
                    linear_col = (float(dv[0]), float(dv[1]), float(dv[2]), float(dv[3]))
                    base_factor = linear_to_srgb_rgba(linear_col)
                except Exception:
                    pass

        # Metallic
        met_socket = principled.inputs.get("Metallic")
        if met_socket:
            img, note = resolve_image_socket(met_socket, entry_notes)
            if note:
                entry_notes.append(note)
            if img:
                out_path = copy_or_bake_texture(img, textures_dir, mat.name, "Metallic", entry_notes)
                if out_path:
                    tex_refs["metallic"] = {"path": rel_path(asset_dir, out_path), "srgb": False}
            else:
                try:
                    metallic_val = float(met_socket.default_value)
                except Exception:
                    pass

        # Roughness
        rough_socket = principled.inputs.get("Roughness")
        if rough_socket:
            img, note = resolve_image_socket(rough_socket, entry_notes)
            if note:
                entry_notes.append(note)
            if img:
                out_path = copy_or_bake_texture(img, textures_dir, mat.name, "Roughness", entry_notes)
                if out_path:
                    tex_refs["roughness"] = {"path": rel_path(asset_dir, out_path), "srgb": False}
            else:
                try:
                    roughness_val = float(rough_socket.default_value)
                except Exception:
                    pass

        # Normal
        norm_socket = principled.inputs.get("Normal")
        if norm_socket:
            nmap_node = socket_linked_node(norm_socket)
            if nmap_node and nmap_node.type == "NORMAL_MAP":
                color_in = nmap_node.inputs.get("Color")
                if color_in:
                    img, note = resolve_image_socket(color_in, entry_notes)
                    if note:
                        entry_notes.append(note)
                    if img:
                        out_path = copy_or_bake_texture(img, textures_dir, mat.name, "Normal", entry_notes)
                        if out_path:
                            strength = nmap_node.inputs.get("Strength")
                            scale = float(strength.default_value) if strength else 1.0
                            tex_refs["normal"] = {"path": rel_path(asset_dir, out_path), "srgb": False, "scale": scale}

        # Emission
        em_socket = principled.inputs.get("Emission Color")
        if em_socket:
            img, note = resolve_image_socket(em_socket, entry_notes)
            if note:
                entry_notes.append(note)
            if img:
                out_path = copy_or_bake_texture(img, textures_dir, mat.name, "Emission", entry_notes)
                if out_path:
                    tex_refs["emission"] = {"path": rel_path(asset_dir, out_path), "srgb": True}
            else:
                try:
                    dv = em_socket.default_value
                    linear_col = (float(dv[0]), float(dv[1]), float(dv[2]), 1.0)
                    emission_color = linear_to_srgb_rgba(linear_col)
                except Exception:
                    pass

        em_str_socket = principled.inputs.get("Emission Strength")
        if em_str_socket:
            try:
                emission_strength = float(em_str_socket.default_value)
            except Exception:
                pass

    material_entry = {
        "shader": "Principled BSDF" if principled else "Unknown",
        "surface": "Opaque",
        "alpha_clip": {"enabled": False, "threshold": 0.5},
        "base_color_factor": list(base_factor),
        "textures": tex_refs,
        "params": {
            "metallic": metallic_val,
            "roughness": roughness_val,
            "emission_color": list(emission_color),
            "emission_strength": emission_strength,
        },
    }

    if entry_notes:
        material_entry["_notes"] = entry_notes

    return material_entry


# -----------------------------
# UI + Operator
# -----------------------------

class URPFBXBRIDGE_Settings(PropertyGroup):
    export_root: StringProperty(
        name="Export Root",
        subtype='DIR_PATH',
        description="Root folder where AssetName/ will be created",
        default="",
    )
    asset_name: StringProperty(
        name="Asset Name",
        description="Folder name and FBX/manifest base name",
        default="",
    )
    axis_forward: StringProperty(
        name="Axis Forward",
        description="FBX forward axis (Unity typically -Z)",
        default="-Z",
    )
    axis_up: StringProperty(
        name="Axis Up",
        description="FBX up axis (Unity typically Y)",
        default="Y",
    )
    open_folder_after: BoolProperty(
        name="Open Folder After Export",
        description="Open the exported folder in file browser (best effort)",
        default=False,
    )


class URPFBXBRIDGE_OT_export(Operator):
    bl_idname = "urp_fbx_bridge.export"
    bl_label = "Export (FBX + Manifest)"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        s = context.scene.urp_fbx_bridge_settings

        export_root = Path(bpy.path.abspath(s.export_root)).expanduser() if s.export_root else None
        asset_name = sanitize_filename(s.asset_name.strip()) if s.asset_name else ""

        if not export_root or not str(export_root):
            self.report({'ERROR'}, "Export Root is empty.")
            return {'CANCELLED'}

        if not asset_name:
            blend = bpy.path.basename(bpy.data.filepath)
            asset_name = sanitize_filename(Path(blend).stem if blend else "Asset")

        objs = gather_selected_mesh_objects(context)
        if not objs:
            self.report({'ERROR'}, "No mesh objects selected.")
            return {'CANCELLED'}

        asset_dir = export_root / asset_name
        textures_dir = asset_dir / "Textures"
        report_dir = asset_dir / "_report"

        ensure_dir(textures_dir)
        ensure_dir(report_dir)

        report_notes = []
        report_notes.append(f"Asset: {asset_name}")
        report_notes.append(f"Pipeline: UnityURP")
        report_notes.append(f"Export time: {iso_now_local()}")
        report_notes.append(f"Selected meshes: {len(objs)}")

        meshes_out = []
        mats_used = {}
        mats_seen = set()

        for o in objs:
            slots = []
            for ms in o.material_slots:
                mat = ms.material
                if mat:
                    slots.append(mat.name)
                    if mat.name not in mats_seen:
                        mats_seen.add(mat.name)
                        mats_used[mat.name] = mat
                else:
                    slots.append("")
            meshes_out.append({
                "name": o.data.name if o.data else o.name,
                "material_slots": slots,
                "object_name": o.name,
                "mesh_data_name": o.data.name if o.data else "",
            })

        materials_out = {}
        for mat_name, mat in mats_used.items():
            try:
                materials_out[mat_name] = make_material_entry(mat, textures_dir, asset_dir, report_notes)
            except Exception as e:
                report_notes.append(f"[Material:{mat_name}] Exception: {e}")

        manifest = build_manifest(
            asset_name=asset_name,
            meshes=meshes_out,
            materials=materials_out,
            axis_forward=s.axis_forward.strip() or "-Z",
            axis_up=s.axis_up.strip() or "Y",
        )

        manifest_path = asset_dir / f"{asset_name}.materials.json"
        write_json(manifest_path, manifest)

        fbx_path = asset_dir / f"{asset_name}.fbx"
        ok = export_fbx_selected(fbx_path, manifest["asset"]["axis"]["forward"], manifest["asset"]["axis"]["up"], report_notes)
        if not ok:
            write_text(report_dir / "export_report.txt", "\n".join(report_notes))
            write_json(report_dir / "export_report.json", {"notes": report_notes})
            self.report({'ERROR'}, "FBX export failed. See export_report.")
            return {'CANCELLED'}

        write_text(report_dir / "export_report.txt", "\n".join(report_notes))
        write_json(report_dir / "export_report.json", {"notes": report_notes})

        self.report({'INFO'}, f"Exported: {asset_dir}")
        return {'FINISHED'}


class URPFBXBRIDGE_PT_panel(Panel):
    bl_label = "Unity URP Export"
    bl_idname = "URPFBXBRIDGE_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Unity Export"

    def draw(self, context):
        layout = self.layout
        s = context.scene.urp_fbx_bridge_settings

        col = layout.column(align=True)

        col.label(text="Export Root")
        col.prop(s, "export_root", text="")

        col.separator(factor=0.5)

        col.label(text="Asset Name")
        col.prop(s, "asset_name", text="")

        col.separator(factor=0.75)

        box = col.box()
        box.label(text="FBX Axis")
        row = box.row(align=True)
        row.prop(s, "axis_forward", text="Forward")
        row.prop(s, "axis_up", text="Up")

        col.separator(factor=1.0)

        col.operator("urp_fbx_bridge.export", icon='EXPORT')


classes = (
    URPFBXBRIDGE_Settings,
    URPFBXBRIDGE_OT_export,
    URPFBXBRIDGE_PT_panel,
)

def register():
    for c in classes:
        bpy.utils.register_class(c)
    bpy.types.Scene.urp_fbx_bridge_settings = PointerProperty(type=URPFBXBRIDGE_Settings)

def unregister():
    for c in reversed(classes):
        bpy.utils.unregister_class(c)
    del bpy.types.Scene.urp_fbx_bridge_settings

if __name__ == "__main__":
    register()
