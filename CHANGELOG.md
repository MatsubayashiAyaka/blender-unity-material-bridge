# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-01-23

### 🎉 Initial Release

Blender から Unity URP へマテリアルを自動転送するツールの初回リリースです。

### Blender Addon (v1.0.0)

- Principled BSDF マテリアルのエクスポート
- FBX + マテリアル情報（JSON）+ テクスチャの一括出力
- Linear → sRGB 色空間変換による正確な色の再現
- ノードグループ対応（1階層）
- FBX 軸設定のカスタマイズ

### Unity Importer (v1.0.0)

- FBX インポート時の URP/Lit マテリアル自動生成
- ModelImporter リマップによる永続的なマテリアル割当
- テクスチャインポート設定の自動調整（sRGB, Normal Map）
- Packed MetallicGloss マップの自動生成
- ログレベル設定（Minimal, Normal, Verbose）

### 対応プロパティ

| プロパティ | テクスチャ | パラメータ |
|----------|---------|-----------|
| Base Color | ✅ | ✅ |
| Metallic | ✅ | ✅ |
| Roughness | ✅ | ✅ |
| Normal | ✅ | Scale |
| Emission | ✅ | Color + Strength |
| AO | ✅ | - |
