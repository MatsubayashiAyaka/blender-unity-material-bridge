# CHANGELOG

このプロジェクトの変更履歴を記録します。

---

## [1.1.0] - 2025-02-09

### Changed
- Autodesk Interactive シェーダーサポートを削除し、URP/Lit のみに簡素化
- `IMaterialBuilder` インターフェースの簡素化
- コードアーキテクチャの整理

### Fixed
- Normal Map インポート時の「Fix Now」ダイアログを防止
  - `OnPreprocessTexture` で事前にテクスチャタイプを設定
- テクスチャパス処理の改善
- マテリアルリマップが再インポート後に保持されない問題を修正
- 各種 null 参照警告の修正

### Removed
- Autodesk Interactive シェーダーサポート（安定性のため削除、将来バージョンで再追加予定）
- Blender アドオンの Pipeline 選択 UI

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