# SPDX-License-Identifier: MIT
# Blender URP FBX Material Bridge (MVP)
# Exports: FBX + .materials.json + Textures + _report

bl_info = {
    "name": "Unity URP FBX Material Bridge",
    "author": "Matsubayashi Ayaka",
    "version": (1, 0, 0),
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
    """
    Convert a single color channel from Linear to sRGB.
    Uses the standard sRGB transfer function.
    """
    if c <= 0.0:
        return 0.0
    elif c < 0.0031308:
        return 12.92 * c
    elif c < 1.0:
        return 1.055 * pow(c, 1.0 / 2.4) - 0.055
    else:
        return 1.0


def linear_to_srgb_rgba(linear_rgba: tuple) -> tuple:
    """
    Convert RGBA from Linear to sRGB.
    RGB channels are converted, Alpha is preserved.
    """
    r, g, b = linear_rgba[0], linear_rgba[1], linear_rgba[2]
    a = linear_rgba[3] if len(linear_rgba) >= 4 else 1.0
    return (
        linear_to_srgb_channel(r),
        linear_to_srgb_channel(g),
        linear_to_srgb_channel(b),
        a  # Alpha is not converted
    )


# -----------------------------
# Data (Manifest v1.0.0)
# -----------------------------

def iso_now_local() -> str:
    # Blender doesn't provide TZ reliably; use local time with offset if possible.
    # Fallback: naive local iso.
    try:
        dt = datetime.now().astimezone()
        return dt.isoformat(timespec="seconds")
    except Exception:
        return datetime.now().isoformat(timespec="seconds")


def sanitize_filename(name: str) -> str:
    # Keep it simple; Unity + filesystem safe
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
# Node utilities (MVP)
# -----------------------------

def find_active_output_node(nt: bpy.types.NodeTree):
    # Material Output with is_active_output True if exists
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
        # MVP: don't fully traverse complex graphs; fallback to first Principled
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


def _map_group_output_input(group_node, from_socket):
    """
    Map outer group output socket -> internal Group Output input socket.
    Prefer name match; fallback to index match.
    """
    nt = getattr(group_node, "node_tree", None)
    if not nt:
        return None, "Group node has no node_tree."

    out_node = _find_group_output_node(nt)
    if not out_node:
        return None, "Group output node not found."

    # name match
    try:
        n = from_socket.name if from_socket else ""
        if n and n in out_node.inputs:
            return out_node.inputs[n], None
    except Exception:
        pass

    # index match
    try:
        if from_socket:
            outs = list(group_node.outputs)
            idx = outs.index(from_socket)
            if idx < len(out_node.inputs):
                return out_node.inputs[idx], None
    except Exception:
        pass

    return None, "Could not map group output socket to internal Group Output input."


def resolve_image_socket(sock, report_notes: list, depth: int = 0, max_group_depth: int = 1):
    """
    Resolve an Image datablock from a socket.
    Supports:
      - Image Texture direct
      - Reroute chains
      - Node Group (one level) that outputs Image Texture
    """
    if not sock or not sock.is_linked:
        return None, None

    from_node, from_socket, _ = _first_link(sock)
    if not from_node:
        return None, None

    if from_node.type == "TEX_IMAGE":
        img = getattr(from_node, "image", None)
        if img:
            return img, None
        return None, "Image Texture node has no image."

    if from_node.type == "REROUTE":
        try:
            return resolve_image_socket(from_node.inputs[0], report_notes, depth=depth, max_group_depth=max_group_depth)
        except Exception:
            return None, "Reroute node could not be traversed."

    if from_node.type == "GROUP":
        if depth >= max_group_depth:
            return None, "Group depth exceeded (max 1)."
        inner_sock, note = _map_group_output_input(from_node, from_socket)
        if note:
            return None, note
        img, note2 = resolve_image_socket(inner_sock, report_notes, depth=depth+1, max_group_depth=max_group_depth)
        if img and not note2:
            report_notes.append(f"[GroupResolved] '{from_node.name}' -> Image '{img.name}'")
        return img, note2

    return None, f"Unsupported node feeding '{sock.name}': {from_node.type}"


def resolve_color_constant_linear(sock, report_notes: list, depth: int = 0, max_group_depth: int = 1):
    """
    Resolve a constant RGBA (in Linear space) if driven by:
      - RGB node
      - Reroute
      - Node Group (one level) that outputs RGB
    
    Returns Linear values (caller must convert to sRGB if needed).
    """
    if not sock or not sock.is_linked:
        return None, None

    from_node, from_socket, _ = _first_link(sock)
    if not from_node:
        return None, None

    if from_node.type == "RGB":
        try:
            v = from_node.outputs[0].default_value
            # Returns Linear space values
            return (float(v[0]), float(v[1]), float(v[2]), float(v[3])), None
        except Exception:
            return None, "RGB node default_value not readable."

    if from_node.type == "REROUTE":
        try:
            return resolve_color_constant_linear(from_node.inputs[0], report_notes, depth=depth, max_group_depth=max_group_depth)
        except Exception:
            return None, "Reroute node could not be traversed."

    if from_node.type == "GROUP":
        if depth >= max_group_depth:
            return None, "Group depth exceeded (max 1)."
        inner_sock, note = _map_group_output_input(from_node, from_socket)
        if note:
            return None, note
        col, note2 = resolve_color_constant_linear(inner_sock, report_notes, depth=depth+1, max_group_depth=max_group_depth)
        if col and not note2:
            report_notes.append(f"[GroupResolved] '{from_node.name}' -> RGB constant")
        return col, note2

    return None, f"Unsupported node feeding '{sock.name}' for constant color: {from_node.type}"


def resolve_color_constant(sock, report_notes: list, depth: int = 0, max_group_depth: int = 1):
    """
    Resolve a constant RGBA and convert from Linear to sRGB.
    """
    linear_col, note = resolve_color_constant_linear(sock, report_notes, depth, max_group_depth)
    if linear_col is not None:
        return linear_to_srgb_rgba(linear_col), note
    return None, note


def resolve_float_constant(sock, report_notes: list, depth: int = 0, max_group_depth: int = 1):
    """
    Resolve a constant float if driven by:
      - VALUE node
      - Reroute
      - Node Group (one level) that outputs VALUE
    """
    if not sock or not sock.is_linked:
        return None, None

    from_node, from_socket, _ = _first_link(sock)
    if not from_node:
        return None, None

    if from_node.type == "VALUE":
        try:
            return float(from_node.outputs[0].default_value), None
        except Exception:
            return None, "Value node default_value not readable."

    if from_node.type == "REROUTE":
        try:
            return resolve_float_constant(from_node.inputs[0], report_notes, depth=depth, max_group_depth=max_group_depth)
        except Exception:
            return None, "Reroute node could not be traversed."

    if from_node.type == "GROUP":
        if depth >= max_group_depth:
            return None, "Group depth exceeded (max 1)."
        inner_sock, note = _map_group_output_input(from_node, from_socket)
        if note:
            return None, note
        val, note2 = resolve_float_constant(inner_sock, report_notes, depth=depth+1, max_group_depth=max_group_depth)
        if val is not None and not note2:
            report_notes.append(f"[GroupResolved] '{from_node.name}' -> VALUE constant")
        return val, note2

    return None, f"Unsupported node feeding '{sock.name}' for constant float: {from_node.type}"


def get_image_from_socket(sock, report_notes: list):
    return resolve_image_socket(sock, report_notes, depth=0, max_group_depth=1)

def get_normal_image_and_scale(principled, report_notes: list):
    """Return (image, scale, note). Expect Normal input -> Normal Map node -> Image Texture."""
    sock = principled.inputs.get("Normal")
    if not sock or not sock.is_linked:
        return None, 1.0, None
    n = socket_linked_node(sock)
    if not n:
        return None, 1.0, None
    if n.type != "NORMAL_MAP":
        return None, 1.0, f"Normal input not via Normal Map node (got {n.type})."
    scale = float(getattr(n.inputs.get("Strength"), "default_value", 1.0))
    color_in = n.inputs.get("Color")
    img, note = get_image_from_socket(color_in, report_notes)
    return img, scale, note


def get_emission(principled, report_notes: list):
    """
    Return (emission_img, emission_color_rgba_srgb, strength, note).
    Supports simple Group indirection (v1.2.0).
    Color is converted from Linear to sRGB.
    """
    emi_sock = principled.inputs.get("Emission")
    strength_sock = principled.inputs.get("Emission Strength")
    strength = float(getattr(strength_sock, "default_value", 1.0)) if strength_sock else 1.0

    img, note_img = get_image_from_socket(emi_sock, report_notes)

    # Prefer resolved constant if driven by RGB/Group/Reroute
    col = (0.0, 0.0, 0.0, 1.0)
    const_col, const_note = resolve_color_constant(emi_sock, report_notes, depth=0, max_group_depth=1)
    if const_col is not None:
        col = const_col  # Already converted to sRGB by resolve_color_constant
    else:
        try:
            if emi_sock and hasattr(emi_sock, "default_value") and not emi_sock.is_linked:
                v = emi_sock.default_value  # Linear space
                linear_col = (float(v[0]), float(v[1]), float(v[2]), float(v[3]))
                col = linear_to_srgb_rgba(linear_col)  # Convert to sRGB
        except Exception:
            pass

    note = note_img or const_note
    return img, col, strength, note


def socket_default_rgba_linear(sock, fallback=(1.0, 1.0, 1.0, 1.0)):
    """
    Get the default RGBA value from a socket in Linear space.
    """
    try:
        if sock and hasattr(sock, "default_value"):
            v = sock.default_value
            if hasattr(v, "__len__") and len(v) >= 4:
                return (float(v[0]), float(v[1]), float(v[2]), float(v[3]))
    except Exception:
        pass
    return fallback


def socket_default_rgba(sock, fallback=(1.0, 1.0, 1.0, 1.0)):
    """
    Get the default RGBA value from a socket and convert from Linear to sRGB.
    """
    linear_col = socket_default_rgba_linear(sock, fallback)
    # Only convert if we got a non-fallback value
    if linear_col != fallback:
        return linear_to_srgb_rgba(linear_col)
    return fallback


def socket_default_float(sock, fallback=0.0):
    try:
        if sock and hasattr(sock, "default_value"):
            return float(sock.default_value)
    except Exception:
        pass
    return float(fallback)


# -----------------------------
# Texture export
# -----------------------------

def export_image_to_png(img: bpy.types.Image, dst_path: Path, report_notes: list):
    """
    Copy or write image to dst_path as PNG.
    Strategy:
      - If img.filepath exists on disk: copy bytes (no conversion).
      - Else: save_render to dst_path (writes pixels).
    """
    ensure_dir(dst_path.parent)
    src_fp = bpy.path.abspath(img.filepath) if getattr(img, "filepath", "") else ""
    src_fp = os.path.normpath(src_fp) if src_fp else ""

    # If already a file and exists, copy (even if not png; but dst_path is png: we still copy bytes? That would mismatch extension)
    # MVP policy: enforce PNG output. If source isn't png, attempt to write PNG via save_render.
    ext = os.path.splitext(src_fp)[1].lower() if src_fp else ""
    if src_fp and os.path.exists(src_fp) and ext == ".png":
        try:
            shutil.copy2(src_fp, str(dst_path))
            return True
        except Exception as e:
            report_notes.append(f"Failed to copy PNG '{src_fp}' -> '{dst_path}': {e}")

    # Try to save as PNG from image datablock
    try:
        # save_render works even if packed / generated
        img.save_render(filepath=str(dst_path))
        return True
    except Exception as e:
        report_notes.append(f"Failed to save_render image '{img.name}' to '{dst_path}': {e}")
        return False


# -----------------------------
# Export logic
# -----------------------------

ROLE_SRGB = {
    "BaseColor": True,
    "Emission": True,
    "Metallic": False,
    "Roughness": False,
    "Normal": False,
    "AO": False,
}

def build_manifest(asset_name: str, meshes: list, materials: dict, axis_forward: str, axis_up: str):
    return {
        "manifest_version": "1.0.0",
        "pipeline": "UnityURP",
        "asset": {
            "name": asset_name,
            "export_time_iso": iso_now_local(),
            "blender_version": bpy.app.version_string,
            "unit_scale": 1.0,
            "axis": {"forward": axis_forward, "up": axis_up},
        },
        "meshes": meshes,
        "materials": materials,
    }


def gather_selected_mesh_objects(context):
    objs = []
    for o in context.selected_objects:
        if o and o.type == "MESH":
            objs.append(o)
    return objs


def ensure_unique_material_names(materials: set, report_notes: list):
    # Blender guarantees unique datablock names; we just note duplicates in slots across meshes.
    return


def export_fbx_selected(dst_fbx: Path, axis_forward: str, axis_up: str, report_notes: list):
    # Use selected objects only; ensure selection matches.
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(dst_fbx),
            use_selection=True,
            object_types={'MESH'},
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            bake_anim=False,
            path_mode='AUTO',
            embed_textures=False,
            axis_forward=axis_forward,
            axis_up=axis_up,
        )
        return True
    except Exception as e:
        report_notes.append(f"FBX export failed: {e}")
        return False


def make_material_entry(mat: bpy.types.Material, textures_dir: Path, asset_dir: Path, report_notes: list):
    mat_name = mat.name
    entry_notes = []

    principled, note = find_principled_node(mat)
    if note:
        entry_notes.append(note)

    # defaults (in sRGB space for colors)
    base_factor = (1.0, 1.0, 1.0, 1.0)
    metallic_val = 0.0
    roughness_val = 0.5
    normal_scale = 1.0
    emission_color = (0.0, 0.0, 0.0, 1.0)
    emission_strength = 1.0

    tex_refs = {}

    if not principled:
        # No principled: fallback to viewport diffuse if available
        try:
            if hasattr(mat, "diffuse_color"):
                dc = mat.diffuse_color  # This is already in sRGB space in Blender viewport
                base_factor = (float(dc[0]), float(dc[1]), float(dc[2]), float(dc[3]))
        except Exception:
            pass
        entry_notes.append("No Principled BSDF; exported only base_color_factor fallback.")
    else:
        # Base color
        base_sock = principled.inputs.get("Base Color")
        img, n = get_image_from_socket(base_sock, report_notes)
        if n:
            entry_notes.append(n)
        if img:
            dst = textures_dir / f"{sanitize_filename(mat_name)}_BaseColor.png"
            ok = export_image_to_png(img, dst, entry_notes)
            if ok:
                tex_refs["base_color"] = {"path": rel_path(asset_dir, dst), "srgb": True}
        else:
            # fallback color (also supports RGB/Group constant in v1.2.0)
            # resolve_color_constant returns sRGB values
            const_col, const_note = resolve_color_constant(base_sock, report_notes, depth=0, max_group_depth=1)
            if const_note:
                entry_notes.append(const_note)
            if const_col is not None:
                base_factor = const_col
            else:
                # socket_default_rgba returns sRGB values
                base_factor = socket_default_rgba(base_sock, base_factor)

        # Metallic
        m_sock = principled.inputs.get("Metallic")
        img, n = get_image_from_socket(m_sock, report_notes)
        if n:
            entry_notes.append(n)
        if img:
            dst = textures_dir / f"{sanitize_filename(mat_name)}_Metallic.png"
            ok = export_image_to_png(img, dst, entry_notes)
            if ok:
                tex_refs["metallic"] = {"path": rel_path(asset_dir, dst), "srgb": False}
        else:
            const_val, const_note = resolve_float_constant(m_sock, report_notes, depth=0, max_group_depth=1)
            if const_note:
                entry_notes.append(const_note)
            if const_val is not None:
                metallic_val = float(const_val)
            else:
                metallic_val = socket_default_float(m_sock, metallic_val)

        # Roughness
        r_sock = principled.inputs.get("Roughness")
        img, n = get_image_from_socket(r_sock, report_notes)
        if n:
            entry_notes.append(n)
        if img:
            dst = textures_dir / f"{sanitize_filename(mat_name)}_Roughness.png"
            ok = export_image_to_png(img, dst, entry_notes)
            if ok:
                tex_refs["roughness"] = {"path": rel_path(asset_dir, dst), "srgb": False}
        else:
            const_val, const_note = resolve_float_constant(r_sock, report_notes, depth=0, max_group_depth=1)
            if const_note:
                entry_notes.append(const_note)
            if const_val is not None:
                roughness_val = float(const_val)
            else:
                roughness_val = socket_default_float(r_sock, roughness_val)

        # Normal
        n_img, n_scale, n_note = get_normal_image_and_scale(principled, report_notes)
        if n_note:
            entry_notes.append(n_note)
        if n_img:
            dst = textures_dir / f"{sanitize_filename(mat_name)}_Normal.png"
            ok = export_image_to_png(n_img, dst, entry_notes)
            if ok:
                normal_scale = float(n_scale)
                tex_refs["normal"] = {
                    "path": rel_path(asset_dir, dst),
                    "srgb": False,
                    "type": "normal",
                    "scale": normal_scale,
                }

        # Emission (optional)
        # get_emission returns sRGB color
        e_img, e_col, e_strength, e_note = get_emission(principled, report_notes)
        if e_note:
            entry_notes.append(e_note)
        emission_color = e_col
        emission_strength = float(e_strength)
        if e_img:
            dst = textures_dir / f"{sanitize_filename(mat_name)}_Emission.png"
            ok = export_image_to_png(e_img, dst, entry_notes)
            if ok:
                tex_refs["emission"] = {"path": rel_path(asset_dir, dst), "srgb": True}

    # Compose entry
    material_entry = {
        "shader": "URP/Lit",
        "surface": "Opaque",
        "alpha_clip": {"enabled": False, "threshold": 0.5},
        "base_color_factor": list(base_factor),
        "textures": tex_refs,
        "params": {
            "metallic": float(metallic_val),
            "roughness": float(roughness_val),
            "emission_color": list(emission_color),
            "emission_strength": float(emission_strength),
        },
    }

    # Promote notes
    for n in entry_notes:
        report_notes.append(f"[Material:{mat_name}] {n}")

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
            # default to blend file name
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
        report_notes.append(f"Export time: {iso_now_local()}")
        report_notes.append(f"Selected meshes: {len(objs)}")

        # Build mesh list + material slot list
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
                    slots.append("")  # empty slot
            meshes_out.append({
                "name": o.data.name if o.data else o.name,  # v1.2.0: prefer mesh datablock name (better Unity match)
                "material_slots": slots,
                # Optional debug fields (ignored by Unity importer if unknown)
                "object_name": o.name,
                "mesh_data_name": o.data.name if o.data else "",
            })

        # Export materials and textures
        materials_out = {}
        for mat_name, mat in mats_used.items():
            try:
                materials_out[mat_name] = make_material_entry(mat, textures_dir, asset_dir, report_notes)
            except Exception as e:
                report_notes.append(f"[Material:{mat_name}] Exception: {e}")

        # Manifest
        manifest = build_manifest(
            asset_name=asset_name,
            meshes=meshes_out,
            materials=materials_out,
            axis_forward=s.axis_forward.strip() or "-Z",
            axis_up=s.axis_up.strip() or "Y",
        )

        manifest_path = asset_dir / f"{asset_name}.materials.json"
        write_json(manifest_path, manifest)

        # FBX export (selected)
        fbx_path = asset_dir / f"{asset_name}.fbx"
        ok = export_fbx_selected(fbx_path, manifest["asset"]["axis"]["forward"], manifest["asset"]["axis"]["up"], report_notes)
        if not ok:
            write_text(report_dir / "export_report.txt", "\n".join(report_notes))
            write_json(report_dir / "export_report.json", {"notes": report_notes})
            self.report({'ERROR'}, "FBX export failed. See export_report.")
            return {'CANCELLED'}

        # Reports
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

        # Label-on-top layout prevents truncated field names in narrow sidebars.
        # Tooltips come from each property's 'description' (hover over the input fields).
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
