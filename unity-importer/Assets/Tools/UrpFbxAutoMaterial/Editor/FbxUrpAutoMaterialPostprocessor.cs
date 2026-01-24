// Assets/Tools/UrpFbxAutoMaterial/Editor/FbxUrpAutoMaterialPostprocessor.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// FBX インポート時に Manifest JSON を検出し、URP マテリアルを自動生成・割当する
    /// </summary>
    public sealed class FbxUrpAutoMaterialPostprocessor : AssetPostprocessor
    {
        // 処理中フラグ（再帰防止）
        private static bool s_isProcessing;

        // セッション中に処理済みのFBXパス（再処理防止）
        private static readonly HashSet<string> s_processedThisSession = new(StringComparer.OrdinalIgnoreCase);

        // 再インポート待ちのFBXパス
        private static readonly HashSet<string> s_pendingReimport = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// アセットインポート完了後に呼ばれる静的コールバック
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (s_isProcessing)
                return;

            // 処理対象の FBX を収集
            var fbxToProcess = new List<(string fbxPath, string jsonPath)>();

            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 生成フォルダは除外（再帰防止）
                if (IsGeneratedPath(assetPath))
                    continue;

                // 再インポート待ちリストにある場合はスキップ（ループ防止）
                if (s_pendingReimport.Contains(assetPath))
                {
                    s_pendingReimport.Remove(assetPath);
                    LogUtil.Verbose($"Reimport completed (skipping reprocess): {assetPath}");
                    continue;
                }

                // セッション中に既に処理済みならスキップ
                if (s_processedThisSession.Contains(assetPath))
                {
                    LogUtil.Verbose($"Already processed this session, skipping: {assetPath}");
                    continue;
                }

                // Manifest JSON の存在確認
                var jsonPath = Path.ChangeExtension(assetPath, ".materials.json");
                if (!File.Exists(jsonPath))
                    continue;

                // 処理済みか確認（リマップが既に設定されているか）
                if (IsAlreadyProcessed(assetPath))
                {
                    LogUtil.Verbose($"Already has material remapping, skipping: {assetPath}");
                    s_processedThisSession.Add(assetPath);
                    continue;
                }

                fbxToProcess.Add((assetPath, jsonPath));
            }

            if (fbxToProcess.Count == 0)
                return;

            // 処理開始
            s_isProcessing = true;
            try
            {
                foreach (var (fbxPath, jsonPath) in fbxToProcess)
                {
                    try
                    {
                        ProcessFbx(fbxPath, jsonPath);
                        // 処理成功したらセッション記録に追加
                        s_processedThisSession.Add(fbxPath);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Exception(ex);
                        LogUtil.Error($"Failed to process FBX: {fbxPath}");
                    }
                }
            }
            finally
            {
                s_isProcessing = false;
            }
        }

        /// <summary>
        /// 生成フォルダかどうかを判定
        /// </summary>
        private static bool IsGeneratedPath(string assetPath)
        {
            var normalized = assetPath.Replace("\\", "/");
            return normalized.Contains("/Generated/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/Materials/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 既に処理済みかどうかを判定（リマップ設定の有無で判断）
        /// </summary>
        private static bool IsAlreadyProcessed(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
                return false;

            // External モードで、かつリマップが設定されていれば処理済み
            if (importer.materialLocation == ModelImporterMaterialLocation.External)
            {
                var externalMap = importer.GetExternalObjectMap();
                if (externalMap.Count > 0)
                {
                    LogUtil.Verbose($"Found {externalMap.Count} existing remaps for {fbxPath}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// FBX を処理してマテリアルを生成・リマップする
        /// </summary>
        private static void ProcessFbx(string fbxPath, string jsonPath)
        {
            LogUtil.Info($"Processing: {fbxPath}");

            // Manifest をロード
            var manifest = ManifestLoader.Load(jsonPath);
            if (manifest == null)
            {
                LogUtil.Warn($"Failed to load manifest: {jsonPath}");
                return;
            }

            LogUtil.Verbose($"Manifest loaded: {manifest.Materials.Count} materials defined");

            // URP/Lit シェーダーを取得
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                LogUtil.Error("URP/Lit shader not found. Make sure Universal Render Pipeline is installed.");
                return;
            }

            // ModelImporter を取得
            var modelImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (modelImporter == null)
            {
                LogUtil.Error($"Failed to get ModelImporter for: {fbxPath}");
                return;
            }

            // FBX 内のマテリアル名を取得（External設定前に取得する必要がある）
            var fbxMaterialNames = GetFbxMaterialNames(fbxPath);
            LogUtil.Verbose($"FBX contains {fbxMaterialNames.Count} materials: [{string.Join(", ", fbxMaterialNames)}]");

            if (fbxMaterialNames.Count == 0)
            {
                LogUtil.Warn($"No materials found in FBX: {fbxPath}");
                return;
            }

            // フォルダパスを準備
            string fbxFolder = PathUtil.GetFolder(fbxPath);
            string materialsFolder = PathUtil.EnsureSubfolder(fbxFolder, "Materials");
            string generatedFolder = PathUtil.EnsureSubfolder(fbxFolder, "Generated");

            // ★ 重要: テクスチャを先にインポートして設定を適用（NormalMap問題の修正）
            // 1) テクスチャパスを収集
            var texturePaths = TextureImportConfigurator.CollectTexturePaths(manifest, fbxFolder);
            
            // 2) テクスチャを強制インポート
            if (texturePaths.Count > 0)
            {
                LogUtil.Verbose($"Importing {texturePaths.Count} textures...");
                TextureImportConfigurator.ForceImportTextures(texturePaths);
                
                // 3) テクスチャインポート設定を適用（Normal Map 設定を含む）
                TextureImportConfigurator.ApplyForManifest(manifest, fbxFolder);
                
                // 4) 設定を反映させるためにリフレッシュ
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            // 5) マテリアル生成
            var builtMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

            foreach (var kv in manifest.Materials)
            {
                var matName = kv.Key;
                var def = kv.Value;

                if (def == null)
                {
                    LogUtil.Warn($"Material definition is null for '{matName}'");
                    continue;
                }

                try
                {
                    var mat = UrpMaterialBuilder.BuildOrUpdate(
                        matName, def, urpLit,
                        fbxFolder, materialsFolder, generatedFolder
                    );

                    if (mat != null)
                    {
                        builtMaterials[matName] = mat;
                        LogUtil.Verbose($"Material built: '{matName}'");
                    }
                    else
                    {
                        LogUtil.Warn($"Failed to build material: {matName}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"Exception building material '{matName}': {ex.Message}");
                }
            }

            if (builtMaterials.Count == 0)
            {
                LogUtil.Warn($"No materials were built for: {fbxPath}");
                return;
            }

            // 6) アセットデータベースを保存（マテリアルを確定）
            AssetDatabase.SaveAssets();

            // 7) ModelImporter で Material Remapping を設定
            bool remapChanged = ConfigureMaterialRemapping(modelImporter, builtMaterials, fbxMaterialNames);

            if (remapChanged)
            {
                // 再インポート待ちリストに追加（ループ防止）
                s_pendingReimport.Add(fbxPath);

                // 8) 再インポートを実行
                LogUtil.Verbose($"Applying material remapping and reimporting");
                modelImporter.SaveAndReimport();
            }

            LogUtil.Info($"Completed: {fbxPath} ({builtMaterials.Count} materials)");
        }

        /// <summary>
        /// FBX 内のマテリアル名を取得
        /// </summary>
        private static List<string> GetFbxMaterialNames(string fbxPath)
        {
            var result = new List<string>();

            // 方法1: サブアセットからマテリアルを取得
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var materials = subAssets.OfType<Material>().ToList();

            foreach (var mat in materials)
            {
                if (mat != null && !string.IsNullOrEmpty(mat.name))
                    result.Add(mat.name);
            }

            // 方法2: サブアセットにマテリアルがない場合、ModelImporter から取得を試みる
            if (result.Count == 0)
            {
                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    // SourceAssetIdentifier からマテリアル名を取得
                    var externalMap = importer.GetExternalObjectMap();
                    foreach (var kv in externalMap)
                    {
                        if (kv.Key.type == typeof(Material))
                        {
                            result.Add(kv.Key.name);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// ModelImporter に Material Remapping を設定
        /// </summary>
        private static bool ConfigureMaterialRemapping(
            ModelImporter importer,
            Dictionary<string, Material> materials,
            List<string> fbxMaterialNames)
        {
            bool changed = false;

            // マテリアルを External モードに設定
            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                changed = true;
                LogUtil.Verbose("Set material location to External");
            }

            // マテリアルインポートモードを設定
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                changed = true;
                LogUtil.Verbose("Set material import mode to ImportViaMaterialDescription");
            }

            // マテリアル名の検索方法を設定（テクスチャ名ではなくマテリアル名を使用）
            if (importer.materialName != ModelImporterMaterialName.BasedOnMaterialName)
            {
                importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                changed = true;
                LogUtil.Verbose("Set material name to BasedOnMaterialName");
            }

            // 既存のリマップを取得
            var existingMap = importer.GetExternalObjectMap();
            LogUtil.Verbose($"Existing remap count: {existingMap.Count}");

            // 各マテリアルをリマップ
            foreach (var kv in materials)
            {
                var manifestMatName = kv.Key;
                var material = kv.Value;

                if (material == null)
                    continue;

                // FBX 内のマテリアル名とマッチング
                string? fbxMatName = FindMatchingFbxMaterialName(manifestMatName, fbxMaterialNames);

                if (fbxMatName == null)
                {
                    LogUtil.Warn($"No matching FBX material found for '{manifestMatName}'");
                    continue;
                }

                LogUtil.Verbose($"Matching: '{manifestMatName}' -> FBX '{fbxMatName}'");

                var sourceId = new AssetImporter.SourceAssetIdentifier(typeof(Material), fbxMatName);

                // 既に同じマテリアルがリマップされているか確認
                if (existingMap.TryGetValue(sourceId, out var existingObj))
                {
                    if (existingObj == material)
                    {
                        LogUtil.Verbose($"Already remapped correctly: '{fbxMatName}'");
                        continue;
                    }
                    LogUtil.Verbose($"Updating remap: '{fbxMatName}'");
                }

                // リマップを追加/更新
                importer.AddRemap(sourceId, material);
                changed = true;
                LogUtil.Verbose($"Added remap: '{fbxMatName}' -> '{material.name}'");
            }

            return changed;
        }

        /// <summary>
        /// Manifest のマテリアル名と一致する FBX マテリアル名を検索
        /// </summary>
        private static string? FindMatchingFbxMaterialName(string manifestMatName, List<string> fbxMaterialNames)
        {
            if (string.IsNullOrEmpty(manifestMatName))
                return null;

            // 1. 完全一致
            var exact = fbxMaterialNames.FirstOrDefault(n =>
                string.Equals(n, manifestMatName, StringComparison.Ordinal));
            if (exact != null)
                return exact;

            // 2. 大文字小文字を無視した一致
            var caseInsensitive = fbxMaterialNames.FirstOrDefault(n =>
                string.Equals(n, manifestMatName, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitive != null)
                return caseInsensitive;

            // 3. FBX マテリアル名が Manifest マテリアル名を含む
            var contains = fbxMaterialNames.FirstOrDefault(n =>
                n.Contains(manifestMatName, StringComparison.OrdinalIgnoreCase));
            if (contains != null)
                return contains;

            // 4. Manifest マテリアル名が FBX マテリアル名を含む
            var reverse = fbxMaterialNames.FirstOrDefault(n =>
                manifestMatName.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (reverse != null)
                return reverse;

            return null;
        }

        /// <summary>
        /// セッション記録をクリア（エディタ再起動時やスクリプト再コンパイル時）
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ClearSessionData()
        {
            s_processedThisSession.Clear();
            s_pendingReimport.Clear();
        }
    }
}
