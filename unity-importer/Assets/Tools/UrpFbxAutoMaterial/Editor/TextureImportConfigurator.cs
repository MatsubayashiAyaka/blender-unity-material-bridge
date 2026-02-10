// Assets/Tools/UrpFbxAutoMaterial/Editor/TextureImportConfigurator.cs
// v1.1.0 - Fixed "Fix Now" dialog by using OnPreprocessTexture
#nullable enable
using System.Collections.Generic;
using UnityEditor;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// テクスチャのインポート設定を Manifest に基づいて構成する
    /// </summary>
    public static class TextureImportConfigurator
    {
        // 保留中のテクスチャ設定（OnPreprocessTexture で使用）
        private static readonly Dictionary<string, TextureImportSettings> s_pendingSettings = 
            new(System.StringComparer.OrdinalIgnoreCase);
        
        private static readonly object s_lock = new();

        /// <summary>
        /// テクスチャのインポート設定
        /// </summary>
        public struct TextureImportSettings
        {
            public bool ExpectedSrgb;
            public bool IsNormalMap;
        }

        /// <summary>
        /// 保留中の設定を登録（OnPreprocessTexture で使用される）
        /// </summary>
        public static void RegisterPendingSettings(string assetPath, bool expectedSrgb, bool isNormalMap)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;
                
            lock (s_lock)
            {
                s_pendingSettings[assetPath] = new TextureImportSettings
                {
                    ExpectedSrgb = expectedSrgb,
                    IsNormalMap = isNormalMap
                };
            }
        }

        /// <summary>
        /// 保留中の設定を取得して削除
        /// </summary>
        public static bool TryGetAndRemovePendingSettings(string assetPath, out TextureImportSettings settings)
        {
            lock (s_lock)
            {
                if (s_pendingSettings.TryGetValue(assetPath, out settings))
                {
                    s_pendingSettings.Remove(assetPath);
                    return true;
                }
            }
            settings = default;
            return false;
        }

        /// <summary>
        /// 保留中の設定をクリア
        /// </summary>
        public static void ClearPendingSettings()
        {
            lock (s_lock)
            {
                s_pendingSettings.Clear();
            }
        }

        /// <summary>
        /// Manifest 内のすべてのテクスチャパスを収集する
        /// </summary>
        public static List<string> CollectTexturePaths(MaterialManifest manifest, string fbxFolderAssetPath)
        {
            var paths = new List<string>();
            
            if (manifest?.Materials == null)
                return paths;

            foreach (var kv in manifest.Materials)
            {
                var def = kv.Value;
                if (def?.Textures == null)
                    continue;

                AddTexturePath(paths, def.Textures.BaseColor, fbxFolderAssetPath);
                AddTexturePath(paths, def.Textures.Emission, fbxFolderAssetPath);
                AddTexturePath(paths, def.Textures.Metallic, fbxFolderAssetPath);
                AddTexturePath(paths, def.Textures.Roughness, fbxFolderAssetPath);
                AddTexturePath(paths, def.Textures.AO, fbxFolderAssetPath);
                AddTexturePath(paths, def.Textures.Normal, fbxFolderAssetPath);
            }

            return paths;
        }

        private static void AddTexturePath(List<string> paths, TextureRef? tref, string fbxFolderAssetPath)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path) || !tref.IsValidPath())
                return;
            
            var texAssetPath = PathUtil.CombineToAssetPath(fbxFolderAssetPath, tref.Path);
            if (!paths.Contains(texAssetPath))
                paths.Add(texAssetPath);
        }

        /// <summary>
        /// Manifest に基づいてテクスチャ設定を事前登録する（OnPreprocessTexture 用）
        /// </summary>
        public static void RegisterTextureSettingsForManifest(MaterialManifest manifest, string fbxFolderAssetPath)
        {
            if (manifest?.Materials == null)
                return;

            foreach (var kv in manifest.Materials)
            {
                var def = kv.Value;
                if (def?.Textures == null)
                    continue;

                // sRGB テクスチャ
                RegisterTextureSettingIfValid(def.Textures.BaseColor, fbxFolderAssetPath, expectedSrgb: true, isNormalMap: false);
                RegisterTextureSettingIfValid(def.Textures.Emission, fbxFolderAssetPath, expectedSrgb: true, isNormalMap: false);

                // Linear テクスチャ
                RegisterTextureSettingIfValid(def.Textures.Metallic, fbxFolderAssetPath, expectedSrgb: false, isNormalMap: false);
                RegisterTextureSettingIfValid(def.Textures.Roughness, fbxFolderAssetPath, expectedSrgb: false, isNormalMap: false);
                RegisterTextureSettingIfValid(def.Textures.AO, fbxFolderAssetPath, expectedSrgb: false, isNormalMap: false);

                // Normal Map
                RegisterTextureSettingIfValid(def.Textures.Normal, fbxFolderAssetPath, expectedSrgb: false, isNormalMap: true);
            }
        }

        private static void RegisterTextureSettingIfValid(TextureRef? tref, string fbxFolderAssetPath, bool expectedSrgb, bool isNormalMap)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path) || !tref.IsValidPath())
                return;

            var texAssetPath = PathUtil.CombineToAssetPath(fbxFolderAssetPath, tref.Path);
            RegisterPendingSettings(texAssetPath, expectedSrgb, isNormalMap);
        }

        /// <summary>
        /// テクスチャを強制的にインポートする
        /// </summary>
        public static void ForceImportTextures(List<string> texturePaths)
        {
            foreach (var path in texturePaths)
            {
                // PathUtil.FileExists を使用（アセットパス対応）
                if (PathUtil.FileExists(path))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    LogUtil.Verbose($"Texture file not found: {path}");
                }
            }
        }

        /// <summary>
        /// Manifest 内のすべてのテクスチャに対してインポート設定を適用
        /// （OnPreprocessTexture が使用できない場合のフォールバック）
        /// </summary>
        public static void ApplyForManifest(MaterialManifest manifest, string fbxFolderAssetPath)
        {
            if (manifest?.Materials == null)
                return;

            foreach (var kv in manifest.Materials)
            {
                var matName = kv.Key;
                var def = kv.Value;

                if (def?.Textures == null)
                    continue;

                // sRGB テクスチャ
                ApplyTexture(def.Textures.BaseColor, fbxFolderAssetPath, matName, "base_color",
                    expectedSrgb: true, normalMap: false);
                ApplyTexture(def.Textures.Emission, fbxFolderAssetPath, matName, "emission",
                    expectedSrgb: true, normalMap: false);

                // Linear テクスチャ
                ApplyTexture(def.Textures.Metallic, fbxFolderAssetPath, matName, "metallic",
                    expectedSrgb: false, normalMap: false);
                ApplyTexture(def.Textures.Roughness, fbxFolderAssetPath, matName, "roughness",
                    expectedSrgb: false, normalMap: false);
                ApplyTexture(def.Textures.AO, fbxFolderAssetPath, matName, "ao",
                    expectedSrgb: false, normalMap: false);

                // Normal Map
                ApplyTexture(def.Textures.Normal, fbxFolderAssetPath, matName, "normal",
                    expectedSrgb: false, normalMap: true);
            }
        }

        /// <summary>
        /// 個別テクスチャのインポート設定を適用
        /// </summary>
        private static void ApplyTexture(
            TextureRef? tref,
            string fbxFolderAssetPath,
            string materialName,
            string textureType,
            bool expectedSrgb,
            bool normalMap)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path))
                return;

            // パス検証（親ディレクトリ参照禁止）
            if (!tref.IsValidPath())
            {
                LogUtil.Warn($"Skipping invalid texture path for '{materialName}'.{textureType}: '{tref.Path}'");
                return;
            }

            var texAssetPath = PathUtil.CombineToAssetPath(fbxFolderAssetPath, tref.Path);

            var importer = AssetImporter.GetAtPath(texAssetPath) as TextureImporter;
            if (importer == null)
            {
                LogUtil.Verbose($"TextureImporter not found: {texAssetPath}");
                return;
            }

            bool changed = false;

            // sRGB 設定
            if (importer.sRGBTexture != expectedSrgb)
            {
                importer.sRGBTexture = expectedSrgb;
                changed = true;
            }

            // Normal Map 設定
            if (normalMap)
            {
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
            }

            // 変更があった場合のみ再インポート
            if (changed)
            {
                LogUtil.Verbose($"Texture settings: {texAssetPath} (sRGB={expectedSrgb}, normalMap={normalMap})");
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// データテクスチャ用の設定を強制適用（PackedMap 等）
        /// </summary>
        public static void ForceDataTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;

            // sRGB を OFF に
            if (importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }
    }

    /// <summary>
    /// テクスチャインポート前に設定を適用するプリプロセッサ
    /// Fix Now ダイアログを回避するため、インポート前に設定を適用
    /// </summary>
    public sealed class TexturePreprocessor : AssetPostprocessor
    {
        /// <summary>
        /// テクスチャインポート前に呼ばれるコールバック
        /// </summary>
        private void OnPreprocessTexture()
        {
            // 登録済みの設定があれば適用
            if (TextureImportConfigurator.TryGetAndRemovePendingSettings(assetPath, out var settings))
            {
                var importer = assetImporter as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = settings.ExpectedSrgb;
                    
                    if (settings.IsNormalMap)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                    }
                    
                    LogUtil.Verbose($"[OnPreprocessTexture] Applied settings: {assetPath} (sRGB={settings.ExpectedSrgb}, normalMap={settings.IsNormalMap})");
                }
            }
        }
    }
}
