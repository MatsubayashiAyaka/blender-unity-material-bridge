// Assets/Tools/UrpFbxAutoMaterial/Editor/FbxUrpAutoMaterialPostprocessor.cs
// v1.1.0 - Simplified URP-only version
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    public sealed class FbxUrpAutoMaterialPostprocessor : AssetPostprocessor
    {
        private static bool s_isProcessing;
        private static readonly HashSet<string> s_processedThisSession = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_pendingReimport = new(StringComparer.OrdinalIgnoreCase);

        private static readonly UrpMaterialBuilder s_materialBuilder = new();

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (s_isProcessing)
                return;

            var fbxToProcess = new List<(string fbxPath, string jsonPath)>();

            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsGeneratedPath(assetPath))
                    continue;

                if (s_pendingReimport.Contains(assetPath))
                {
                    s_pendingReimport.Remove(assetPath);
                    LogUtil.Verbose($"Reimport completed (skipping reprocess): {assetPath}");
                    continue;
                }

                if (s_processedThisSession.Contains(assetPath))
                {
                    LogUtil.Verbose($"Already processed this session, skipping: {assetPath}");
                    continue;
                }

                var jsonPath = Path.ChangeExtension(assetPath, ".materials.json");
                if (!PathUtil.FileExists(jsonPath))
                    continue;

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

            s_isProcessing = true;
            try
            {
                foreach (var (fbxPath, jsonPath) in fbxToProcess)
                {
                    try
                    {
                        ProcessFbx(fbxPath, jsonPath);
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
                TextureImportConfigurator.ClearPendingSettings();
            }
        }

        private static bool IsGeneratedPath(string assetPath)
        {
            var normalized = assetPath.Replace("\\", "/");
            return normalized.Contains("/Generated/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/Materials/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAlreadyProcessed(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
                return false;

            if (importer.materialLocation == ModelImporterMaterialLocation.External)
            {
                var externalMap = importer.GetExternalObjectMap();
                if (externalMap.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ProcessFbx(string fbxPath, string jsonPath)
        {
            LogUtil.Info($"Processing: {fbxPath}");

            var manifest = ManifestLoader.Load(jsonPath);
            if (manifest == null)
            {
                LogUtil.Warn($"Failed to load manifest: {jsonPath}");
                return;
            }

            LogUtil.Verbose($"Manifest loaded: {manifest.Materials.Count} materials");

            if (!s_materialBuilder.IsShaderAvailable())
            {
                LogUtil.Error(s_materialBuilder.GetShaderNotFoundMessage());
                return;
            }

            var modelImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (modelImporter == null)
            {
                LogUtil.Error($"Failed to get ModelImporter for: {fbxPath}");
                return;
            }

            var fbxMaterialNames = GetFbxMaterialNames(fbxPath);
            LogUtil.Verbose($"FBX contains {fbxMaterialNames.Count} materials: [{string.Join(", ", fbxMaterialNames)}]");

            if (fbxMaterialNames.Count == 0)
            {
                LogUtil.Warn($"No materials found in FBX: {fbxPath}");
                return;
            }

            string fbxFolder = PathUtil.GetFolder(fbxPath);
            string materialsFolder = PathUtil.EnsureSubfolder(fbxFolder, "Materials");
            string generatedFolder = PathUtil.EnsureSubfolder(fbxFolder, "Generated");

            if (string.IsNullOrEmpty(materialsFolder) || string.IsNullOrEmpty(generatedFolder))
            {
                LogUtil.Error($"Failed to create folders in: {fbxFolder}");
                return;
            }

            // テクスチャのインポート設定を事前登録（Fix Now対策）
            TextureImportConfigurator.RegisterTextureSettingsForManifest(manifest, fbxFolder);
            var texturePaths = TextureImportConfigurator.CollectTexturePaths(manifest, fbxFolder);
            
            LogUtil.Verbose($"Collected {texturePaths.Count} texture paths");

            if (texturePaths.Count > 0)
            {
                LogUtil.Verbose($"Importing {texturePaths.Count} textures...");
                TextureImportConfigurator.ForceImportTextures(texturePaths);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                
                // フォールバック：OnPreprocessTextureが働かなかった場合の対策
                TextureImportConfigurator.ApplyForManifest(manifest, fbxFolder);
            }

            // マテリアル作成
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
                    var mat = s_materialBuilder.BuildOrUpdate(
                        matName, def,
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

            AssetDatabase.SaveAssets();

            // マテリアルリマップを設定
            bool remapChanged = ConfigureMaterialRemapping(modelImporter, builtMaterials, fbxMaterialNames);

            if (remapChanged)
            {
                s_pendingReimport.Add(fbxPath);
                LogUtil.Verbose($"Applying material remapping and reimporting");
                modelImporter.SaveAndReimport();
            }

            LogUtil.Info($"Completed: {fbxPath} ({builtMaterials.Count} materials)");
        }

        private static List<string> GetFbxMaterialNames(string fbxPath)
        {
            var result = new List<string>();
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var materials = subAssets.OfType<Material>().ToList();

            foreach (var mat in materials)
            {
                if (mat != null && !string.IsNullOrEmpty(mat.name))
                    result.Add(mat.name);
            }

            if (result.Count == 0)
            {
                var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    var externalMap = importer.GetExternalObjectMap();
                    foreach (var kv in externalMap)
                    {
                        if (kv.Key.type == typeof(Material))
                            result.Add(kv.Key.name);
                    }
                }
            }

            return result;
        }

        private static bool ConfigureMaterialRemapping(
            ModelImporter importer,
            Dictionary<string, Material> materials,
            List<string> fbxMaterialNames)
        {
            bool changed = false;

            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                changed = true;
                LogUtil.Verbose("Set material location to External");
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                changed = true;
            }

            if (importer.materialName != ModelImporterMaterialName.BasedOnMaterialName)
            {
                importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                changed = true;
            }

            var existingMap = importer.GetExternalObjectMap();

            foreach (var kv in materials)
            {
                var manifestMatName = kv.Key;
                var material = kv.Value;

                if (material == null)
                    continue;

                string? fbxMatName = FindMatchingFbxMaterialName(manifestMatName, fbxMaterialNames);

                if (fbxMatName == null)
                {
                    LogUtil.Warn($"No matching FBX material found for '{manifestMatName}'");
                    continue;
                }

                var sourceId = new AssetImporter.SourceAssetIdentifier(typeof(Material), fbxMatName);

                if (existingMap.TryGetValue(sourceId, out var existingObj))
                {
                    if (existingObj == material)
                        continue;
                }

                importer.AddRemap(sourceId, material);
                changed = true;
                LogUtil.Verbose($"Added remap: '{fbxMatName}' -> '{material.name}'");
            }

            return changed;
        }

        private static string? FindMatchingFbxMaterialName(string manifestMatName, List<string> fbxMaterialNames)
        {
            if (string.IsNullOrEmpty(manifestMatName))
                return null;

            var exact = fbxMaterialNames.FirstOrDefault(n =>
                string.Equals(n, manifestMatName, StringComparison.Ordinal));
            if (exact != null) return exact;

            var caseInsensitive = fbxMaterialNames.FirstOrDefault(n =>
                string.Equals(n, manifestMatName, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitive != null) return caseInsensitive;

            var contains = fbxMaterialNames.FirstOrDefault(n =>
                n.Contains(manifestMatName, StringComparison.OrdinalIgnoreCase));
            if (contains != null) return contains;

            var reverse = fbxMaterialNames.FirstOrDefault(n =>
                manifestMatName.Contains(n, StringComparison.OrdinalIgnoreCase));
            return reverse;
        }

        [InitializeOnLoadMethod]
        private static void ClearSessionData()
        {
            s_processedThisSession.Clear();
            s_pendingReimport.Clear();
            TextureImportConfigurator.ClearPendingSettings();
        }
    }
}
