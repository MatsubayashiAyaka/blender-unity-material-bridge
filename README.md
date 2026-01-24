# Blender Unity URP Material Bridge

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Blender](https://img.shields.io/badge/Blender-3.6+-orange.svg)](https://www.blender.org/)
[![Unity](https://img.shields.io/badge/Unity-2021.3+-black.svg)](https://unity.com/)

Blender から Unity URP へマテリアルを自動転送するツールです。

## 特徴

- **ワンクリックエクスポート**: Blender から FBX + マテリアル情報 + テクスチャを一括出力
- **自動インポート**: Unity で FBX をインポートすると URP/Lit マテリアルを自動生成
- **テクスチャ対応**: Base Color, Metallic, Roughness, Normal, Emission, AO
- **元の色の再現**: Linear → sRGB 色空間変換により正確な色を再現
- **永続的なマテリアル**: FBX アセットにマテリアルがリマップされます

---

## 動作要件

| コンポーネント | バージョン |
|---------------|-----------|
| Blender | 3.6 以降 |
| Unity | 2021.3 LTS 以降 |
| Universal Render Pipeline (URP) | 12.0 以降 |

---

## インストール手順

### Step 1: Blender アドオンのインストール

#### 1-1. ダウンロード

[Releases](../../releases) ページから `blender_urp_fbx_bridge_v1.0.0.zip` をダウンロードします。

> ⚠️ **注意**: zip ファイルは**展開せずにそのまま**使用します。

#### 1-2. Blender でインストール

1. **Blender を起動**します

2. メニューから **`編集`** → **`プリファレンス`** を開きます
   - 英語版: `Edit` → `Preferences`

3. 左側のメニューから **`アドオン`** を選択します
   - 英語版: `Add-ons`

4. 右上の **`インストール...`** ボタンをクリックします
   - 英語版: `Install...`

5. ダウンロードした **`blender_urp_fbx_bridge_v1.0.0.zip`** を選択し、**`アドオンをインストール`** をクリックします

6. インストール後、**`Unity URP FBX Material Bridge`** にチェックを入れて有効化します

7. プリファレンスウィンドウを閉じます

#### 1-3. 確認

サイドバー（`N` キー）に **`Unity Export`** タブが表示されていれば成功です。

---

### Step 2: Unity インポーターのインストール

#### 2-1. Newtonsoft.Json パッケージのインストール（必須）

Unity インポーターは JSON パースに Newtonsoft.Json を使用します。**このパッケージを先にインストールしてください。**

1. Unity プロジェクトを開きます

2. メニューから **`Window`** → **`Package Manager`** を開きます

3. Package Manager ウィンドウの左上にある **`+`** ボタンをクリックします

4. **`Add package by name...`** を選択します

5. 以下の名前を入力して **`Add`** をクリックします:
   ```
   com.unity.nuget.newtonsoft-json
   ```

6. インストールが完了するまで待ちます（数秒〜数十秒）

#### 2-2. インポーターのダウンロードと配置

1. [Releases](../../releases) ページから **`UrpFbxAutoMaterial_v1.0.0.zip`** をダウンロードします

2. zip ファイルを**展開（解凍）**します
   - Windows: 右クリック → `すべて展開`
   - macOS: ダブルクリック

3. 展開すると以下のフォルダ構造が現れます:
   ```
   UrpFbxAutoMaterial_v1.0.0/
   └── Assets/
       └── Tools/
           └── UrpFbxAutoMaterial/
               └── Editor/
                   ├── FbxUrpAutoMaterialPostprocessor.cs
                   ├── LogUtil.cs
                   ├── ManifestLoader.cs
                   ├── ManifestModels.cs
                   ├── PackedMapGenerator.cs
                   ├── PathUtil.cs
                   ├── RendererAssigner.cs
                   ├── TextureImportConfigurator.cs
                   ├── UrpMaterialBuilder.cs
                   └── Settings/
                       └── UrpFbxAutoMaterialSettings.cs
   ```

4. **`Tools`** フォルダを Unity プロジェクトの **`Assets`** フォルダ直下にコピーします

   コピー後の構造:
   ```
   YourUnityProject/
   └── Assets/
       └── Tools/
           └── UrpFbxAutoMaterial/
               └── Editor/
                   └── (上記のファイル群)
   ```

5. Unity に戻ると、自動的にスクリプトがコンパイルされます

#### 2-3. 確認

Console ウィンドウにエラーが表示されていなければ成功です。

---

## 使い方

### Blender でエクスポート

1. **エクスポートしたいメッシュオブジェクトを選択**します
   - 複数選択可（`Shift` + クリック）

2. **サイドバー**を開きます
   - `N` キーを押す、または `表示` → `サイドバー`

3. **`Unity Export`** タブをクリックします

4. 以下を設定します:
   - **Export Root**: 出力先フォルダ（例: Unity プロジェクトの `Assets/Models/`）
   - **Asset Name**: アセット名（フォルダ名と FBX ファイル名になります）

5. **`Export (FBX + Manifest)`** ボタンをクリックします

### Unity でインポート

1. Unity の Project ウィンドウに、エクスポートしたフォルダが表示されます
   - 初回インポート時に NormalMap settings ダイアログが表示される場合は Fix now をクリックしてください

2. **自動的に**以下が行われます:
   - URP/Lit マテリアルの生成
   - テクスチャ設定の自動調整
   - FBX へのマテリアルリマップ

3. **完了**

---

## 対応しているマテリアルプロパティ

| Blender (Principled BSDF) | Unity (URP/Lit) |
|---------------------------|-----------------|
| Base Color | Base Map / Base Color |
| Metallic | Metallic Map / Metallic |
| Roughness | Smoothness (反転) |
| Normal Map | Normal Map |
| Emission | Emission Map / Emission Color |

---

## 出力されるファイル構造

```
AssetName/
├── AssetName.fbx              # FBX ファイル
├── AssetName.materials.json   # マテリアル情報（JSON）
├── Textures/                  # テクスチャフォルダ
│   ├── MatName_BaseColor.png
│   ├── MatName_Normal.png
│   └── ...
├── Materials/                 # 生成されたマテリアル（Unity）
│   └── MatName.mat
├── Generated/                 # 生成されたテクスチャ（Unity）
│   └── MatName_PackedMetallicGloss.png
└── _report/                   # エクスポートレポート
    ├── export_report.txt
    └── export_report.json
```

---

## トラブルシューティング

### エラー: `Newtonsoft` が見つからない

**原因**: Newtonsoft.Json パッケージがインストールされていません。

**解決方法**: [Step 2-1](#2-1-newtonsoftjson-パッケージのインストール必須) を実行してください。

### マテリアルが適用されない

**原因**: FBX 内のマテリアル名と Manifest のマテリアル名が一致しない可能性があります。

**解決方法**:
1. Blender でマテリアル名を確認
2. 特殊文字（日本語など）を避ける
3. 再エクスポート

### テクスチャが読み込まれない

**原因**: テクスチャファイルがまだインポートされていない可能性があります。

**解決方法**:
1. Unity で `Assets` → `Refresh` (`Ctrl+R` / `Cmd+R`)
2. FBX を右クリック → `Reimport`

### 色が違う

**原因**: 古いバージョンの Blender アドオンを使用している可能性があります。

**解決方法**: 最新版をインストールしてください。

---

## ライセンス

MIT License - 詳細は [LICENSE](LICENSE) を参照してください。

## 作者

**Matsubayashi Ayaka**
