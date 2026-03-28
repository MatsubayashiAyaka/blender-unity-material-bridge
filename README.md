# Blender → Unity URP Material Bridge

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Blender](https://img.shields.io/badge/Blender-3.6+-orange.svg)](https://www.blender.org/)
[![Unity](https://img.shields.io/badge/Unity-2021.3+-black.svg)](https://unity.com/)

**Stop wasting hours manually recreating URP materials.**  
Export your Blender assets and automatically generate Unity URP/Lit 
materials — textures, colors, and all — in one click.

---

## Features

- **One-Click Export** — Export FBX + material data + textures from 
  Blender all at once
- **Auto Material Generation** — Unity automatically creates URP/Lit 
  materials on import
- **Full Texture Support** — Base Color, Metallic, Roughness, Normal, 
  Emission, AO
- **Accurate Color Reproduction** — Linear → sRGB conversion keeps 
  your colors true
- **Persistent Materials** — Materials are remapped directly to the 
  FBX asset

---

## Requirements

| Component | Version |
|---|---|
| Blender | 3.6 or later |
| Unity | 2021.3 LTS or later |
| Universal Render Pipeline (URP) | 12.0 or later |

---

## Installation

### Step 1: Install the Blender Addon

1. Go to the [Releases](../../releases) page and download  
   `blender_urp_fbx_bridge_v1.0.0.zip`
2. **Do NOT unzip the file**
3. In Blender: `Edit` → `Preferences` → `Add-ons` → `Install...`
4. Select the downloaded `.zip` file and click `Install Add-on`
5. Enable **Unity URP FBX Material Bridge** by checking the checkbox
6. Press `N` in the viewport — you should see a **Unity Export** tab

### Step 2: Install the Unity Importer

#### 2-1. Install Newtonsoft.Json (required)

1. Open your Unity project
2. Go to `Window` → `Package Manager`
3. Click `+` → `Add package by name...`
4. Enter: `com.unity.nuget.newtonsoft-json` and click `Add`

#### 2-2. Copy the Importer Scripts

1. Download `UrpFbxAutoMaterial_v1.0.0.zip` from  
   the [Releases](../../releases) page
2. Unzip it
3. Copy the `Tools` folder into your Unity project's `Assets` folder
4. Unity will recompile automatically — no errors means success 

---

## How to Use

### In Blender

1. Select the mesh objects you want to export
2. Press `N` to open the sidebar → click the **Unity Export** tab
3. Set **Export Root** (e.g. `Assets/Models/` in your Unity project)
4. Set **Asset Name**
5. Click **Export (FBX + Manifest)**

### In Unity

That's it — Unity handles the rest automatically:
- URP/Lit materials are generated
- Textures are configured
- Materials are remapped to the FBX

---

## Output File Structure
```
AssetName/
├── AssetName.fbx
├── AssetName.materials.json
├── Textures/
│   ├── MatName_BaseColor.png
│   ├── MatName_Normal.png
│   └── ...
├── Materials/
│   └── MatName.mat
├── Generated/
│   └── MatName_PackedMetallicGloss.png
└── _report/
    ├── export_report.txt
    └── export_report.json
```

---

## Supported Material Properties

| Blender (Principled BSDF) | Unity (URP/Lit) |
|---|---|
| Base Color | Base Map / Base Color |
| Metallic | Metallic Map / Metallic |
| Roughness | Smoothness (inverted) |
| Normal Map | Normal Map |
| Emission | Emission Map / Emission Color |

---

## Troubleshooting

**`Newtonsoft` not found**  
→ Complete Step 2-1 above to install the package.

**Materials not applied**  
→ Avoid special characters (including Japanese) in material names, 
then re-export.

**Textures not loading**  
→ In Unity: `Assets` → `Refresh` (`Ctrl+R` / `Cmd+R`),  
then right-click the FBX → `Reimport`.

**Colors look wrong**  
→ Make sure you're using the latest version of the addon.

---

## Support

Found a bug or have a question?  
→ Open an [Issue](../../issues) or contact via the store page.

---

## License

MIT License — see [LICENSE](./LICENSE) for details.

## Author

**Matsubayashi Ayaka**
