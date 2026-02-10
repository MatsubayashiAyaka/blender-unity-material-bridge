# Blender Addon - Unity URP FBX Material Bridge

Blender から URP 互換のマテリアル情報を含む FBX をエクスポートするアドオンです。

## インストール

[メインの README](../README.md) の「Step 1: Blender アドオンのインストール」を参照してください。

## 使い方

1. エクスポートするメッシュオブジェクトを選択
2. サイドバー（`N` キー）→ `Unity Export` タブ
3. **Export Root** と **Asset Name** を設定
4. **Export (FBX + Manifest)** をクリック

## 出力ファイル

```
AssetName/
├── AssetName.fbx              # FBX ファイル
├── AssetName.materials.json   # マテリアル情報
├── Textures/                  # テクスチャ
└── _report/                   # レポート
```

## 対応機能

- Principled BSDF マテリアル
- Base Color, Metallic, Roughness, Normal, Emission テクスチャ
- 色/値パラメータ
- ノードグループ（1階層）
- リルートノード

## 動作要件

- Blender 3.6 以降

## バージョン

1.1.0
