# CHANGELOG

このプロジェクトの変更履歴を記録します。

---

## [1.0.0]

### Added
- Blender から Unity URP へのマテリアル自動転送機能
- Principled BSDF マテリアルのエクスポート
- FBX + JSON + テクスチャの一括出力
- ノードグループ対応（1階層）
- FBX 軸設定のカスタマイズ

### Unity Importer
- FBX インポート時の URP/Lit マテリアル自動生成
- ModelImporter リマップによる永続的なマテリアル割当
- テクスチャインポート設定の自動調整（sRGB / Normal Map）
- Packed MetallicGloss マップの自動生成
- ログレベル設定（Minimal / Normal / Verbose）

### Technical
- Linear → sRGB 色空間変換による正確な色再現
- Metallic(R) + Smoothness(A) チャンネルパック生成
- URP/Lit シェーダー構成に最適化したテクスチャ処理
