// Assets/Tools/UrpFbxAutoMaterial/Editor/ManifestLoader.cs
#nullable enable
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace UrpFbxAutoMaterial
{
    public static class ManifestLoader
    {
        /// <summary>
        /// Manifest JSON をロードしてパースする
        /// </summary>
        /// <param name="jsonAssetPath">Unity アセットパス (例: "Assets/.../MyModel.materials.json")</param>
        /// <returns>パース成功時は MaterialManifest、失敗時は null</returns>
        public static MaterialManifest? Load(string jsonAssetPath)
        {
            if (string.IsNullOrEmpty(jsonAssetPath))
            {
                LogUtil.Error("ManifestLoader: jsonAssetPath is null or empty.");
                return null;
            }

            if (!File.Exists(jsonAssetPath))
            {
                LogUtil.Warn($"ManifestLoader: File not found: {jsonAssetPath}");
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(jsonAssetPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogUtil.Error($"ManifestLoader: Failed to read file '{jsonAssetPath}': {ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                LogUtil.Warn($"ManifestLoader: File is empty: {jsonAssetPath}");
                return null;
            }

            try
            {
                var manifest = JsonConvert.DeserializeObject<MaterialManifest>(json);

                if (manifest == null)
                {
                    LogUtil.Warn($"ManifestLoader: Deserialization returned null for: {jsonAssetPath}");
                    return null;
                }

                // 基本検証
                if (!ValidateManifest(manifest, jsonAssetPath))
                {
                    return null;
                }

                LogUtil.Verbose($"Manifest: v{manifest.ManifestVersion ?? "unknown"} with {manifest.Meshes.Count} meshes, {manifest.Materials.Count} materials");

                return manifest;
            }
            catch (JsonReaderException ex)
            {
                LogUtil.Error($"ManifestLoader: JSON syntax error in '{jsonAssetPath}' at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}");
                return null;
            }
            catch (JsonSerializationException ex)
            {
                LogUtil.Error($"ManifestLoader: JSON structure error in '{jsonAssetPath}': {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogUtil.Error($"ManifestLoader: Unexpected error parsing '{jsonAssetPath}': {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Manifest の基本検証
        /// </summary>
        private static bool ValidateManifest(MaterialManifest manifest, string path)
        {
            // Pipeline チェック（警告のみ、処理は続行）
            if (!string.IsNullOrEmpty(manifest.Pipeline) &&
                !manifest.Pipeline.Equals("UnityURP", StringComparison.OrdinalIgnoreCase))
            {
                LogUtil.Warn($"ManifestLoader: Unexpected pipeline '{manifest.Pipeline}' in {path}. Expected 'UnityURP'.");
            }

            // Meshes チェック
            if (manifest.Meshes.Count == 0)
            {
                LogUtil.Warn($"ManifestLoader: No meshes defined in {path}");
            }
            else
            {
                for (int i = 0; i < manifest.Meshes.Count; i++)
                {
                    var mesh = manifest.Meshes[i];
                    if (mesh == null)
                        continue;
                    if (string.IsNullOrEmpty(mesh.Name))
                    {
                        LogUtil.Warn($"ManifestLoader: meshes[{i}].name is empty in {path}");
                    }
                }
            }

            // Materials チェック
            if (manifest.Materials.Count == 0)
            {
                LogUtil.Warn($"ManifestLoader: No materials defined in {path}");
            }
            else
            {
                // テクスチャパスの検証
                foreach (var kv in manifest.Materials)
                {
                    var matName = kv.Key;
                    var def = kv.Value;
                    if (def?.Textures == null)
                        continue;

                    ValidateTexturePath(def.Textures.BaseColor, matName, "base_color", path);
                    ValidateTexturePath(def.Textures.Metallic, matName, "metallic", path);
                    ValidateTexturePath(def.Textures.Roughness, matName, "roughness", path);
                    ValidateTexturePath(def.Textures.Normal, matName, "normal", path);
                    ValidateTexturePath(def.Textures.Emission, matName, "emission", path);
                    ValidateTexturePath(def.Textures.AO, matName, "ao", path);
                }
            }

            return true;
        }

        /// <summary>
        /// テクスチャパスの検証
        /// </summary>
        private static void ValidateTexturePath(TextureRef? tref, string matName, string texType, string manifestPath)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path))
                return;

            if (!tref.IsValidPath())
            {
                LogUtil.Warn($"ManifestLoader: Invalid texture path in material '{matName}'.textures.{texType}: '{tref.Path}' (parent directory reference '../' is forbidden) in {manifestPath}");
            }
        }
    }
}
