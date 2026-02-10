# Unity Importer - URP FBX Auto Material

Blender からエクスポートされた FBX のマテリアル情報を読み取り、URP/Lit マテリアルを自動生成する Unity エディタ拡張です。

## インストール

[メインの README](../README.md) の「Step 2: Unity インポーターのインストール」を参照してください。

> ⚠️ **重要**: 先に Newtonsoft.Json パッケージをインストールしてください。

## 動作の仕組み

1. FBX ファイルがインポートされると、同名の `.materials.json` ファイルを検出
2. JSON に基づいて URP/Lit マテリアルを生成
3. テクスチャのインポート設定を自動調整（sRGB, Normal Map 等）
4. FBX アセットにマテリアルを永続的にリマップ

## 生成されるファイル

```
AssetName/
├── Materials/           # 生成されたマテリアル
│   └── MatName.mat
└── Generated/           # 生成されたテクスチャ
    └── MatName_PackedMetallicGloss.png
```

## ログレベルの変更

```csharp
using UrpFbxAutoMaterial;

// 最小限（エラーと警告のみ）
LogUtil.CurrentLevel = LogLevel.Minimal;

// 通常（デフォルト）
LogUtil.CurrentLevel = LogLevel.Normal;

// 詳細（デバッグ情報）
LogUtil.CurrentLevel = LogLevel.Verbose;
```

## 動作要件

- Unity 2021.3 LTS 以降
- Universal Render Pipeline 12.0 以降
- Newtonsoft.Json パッケージ

## バージョン

1.1.0
