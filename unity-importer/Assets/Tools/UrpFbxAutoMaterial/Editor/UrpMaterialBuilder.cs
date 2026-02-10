// Assets/Tools/UrpFbxAutoMaterial/Editor/UrpMaterialBuilder.cs
// v1.1.0 - URP/Lit material builder with texture support
#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    public sealed class UrpMaterialBuilder : IMaterialBuilder
    {
        private const float SmoothnessSource_MetallicAlpha = 0f;
        private const string ShaderName = "Universal Render Pipeline/Lit";
        private Shader? _cachedShader;

        public bool IsShaderAvailable() => GetShader() != null;
        public string GetShaderNotFoundMessage() =>
            "URP/Lit shader not found. Make sure Universal Render Pipeline is installed.";

        private Shader? GetShader()
        {
            if (_cachedShader == null)
                _cachedShader = Shader.Find(ShaderName);
            return _cachedShader;
        }

        public Material? BuildOrUpdate(
            string matName,
            MaterialDef def,
            string fbxFolderAssetPath,
            string materialsFolderAssetPath,
            string generatedFolderAssetPath)
        {
            var shader = GetShader();
            if (string.IsNullOrEmpty(matName) || def == null || shader == null)
                return null;

            if (!AssetDatabase.IsValidFolder(materialsFolderAssetPath))
            {
                LogUtil.Error($"Materials folder does not exist: {materialsFolderAssetPath}");
                return null;
            }

            var matAssetPath = $"{materialsFolderAssetPath}/{SanitizeFileName(matName)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);

            bool isNew = mat == null;
            if (isNew)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matAssetPath);
                LogUtil.Verbose($"Created new URP/Lit material: {matAssetPath}");
            }
            else
            {
                mat!.shader = shader;
                LogUtil.Verbose($"Updating existing URP/Lit material: {matAssetPath}");
            }

            // Base Color
            if (mat.HasProperty("_BaseColor"))
            {
                var color = ToColor(def.BaseColorFactor, Color.white);
                mat.SetColor("_BaseColor", color);
            }

            // Metallic / Roughness パラメータ
            float metallicParam = def.Params?.Metallic ?? 0.0f;
            float roughnessParam = def.Params?.Roughness ?? 0.5f;

            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallicParam);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 1.0f - roughnessParam);
            if (mat.HasProperty("_SmoothnessTextureChannel"))
                mat.SetFloat("_SmoothnessTextureChannel", SmoothnessSource_MetallicAlpha);

            // Emission
            if (mat.HasProperty("_EmissionColor"))
            {
                var emissionColor = ToColor(def.Params?.EmissionColor, Color.black);
                float emissionStrength = def.Params?.EmissionStrength ?? 1.0f;
                mat.SetColor("_EmissionColor", emissionColor * emissionStrength);
            }

            // Surface Settings
            ApplySurfaceSettings(mat, def);

            // ===== テクスチャ設定 =====
            
            // BaseMap
            var baseTex = LoadTexture(def.Textures?.BaseColor, fbxFolderAssetPath);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", baseTex);

            // Normal Map
            var normalTex = LoadTexture(def.Textures?.Normal, fbxFolderAssetPath);
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normalTex);
                if (normalTex != null)
                {
                    mat.EnableKeyword("_NORMALMAP");
                    if (mat.HasProperty("_BumpScale"))
                        mat.SetFloat("_BumpScale", def.Textures?.Normal?.Scale ?? 1.0f);
                }
            }

            // Occlusion (AO)
            var aoTex = LoadTexture(def.Textures?.AO, fbxFolderAssetPath);
            if (mat.HasProperty("_OcclusionMap"))
                mat.SetTexture("_OcclusionMap", aoTex);

            // Emission Map
            var emissionTex = LoadTexture(def.Textures?.Emission, fbxFolderAssetPath);
            if (mat.HasProperty("_EmissionMap"))
            {
                mat.SetTexture("_EmissionMap", emissionTex);
                if (emissionTex != null)
                    mat.EnableKeyword("_EMISSION");
            }

            // Metallic / Roughness (Packed Map)
            var metallicTex = LoadTexture(def.Textures?.Metallic, fbxFolderAssetPath);
            var roughnessTex = LoadTexture(def.Textures?.Roughness, fbxFolderAssetPath);

            string packedPath = PackedMapGenerator.BuildPackedMetallicGloss(
                matName,
                metallicTex,
                roughnessTex,
                metallicParam,
                roughnessParam,
                generatedFolderAssetPath
            );

            if (!string.IsNullOrEmpty(packedPath))
            {
                var packedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);
                if (packedTex != null && mat.HasProperty("_MetallicGlossMap"))
                {
                    mat.SetTexture("_MetallicGlossMap", packedTex);
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 1.0f);
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 1.0f);
                }
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private static Texture2D? LoadTexture(TextureRef? tref, string fbxFolderAssetPath)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path)) return null;
            if (!tref.IsValidPath())
            {
                LogUtil.Warn($"Invalid texture path: {tref.Path}");
                return null;
            }
            var texAssetPath = PathUtil.CombineToAssetPath(fbxFolderAssetPath, tref.Path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetPath);
        }

        private static Color ToColor(float[]? rgba, Color fallback)
        {
            if (rgba == null || rgba.Length < 3) return fallback;
            return new Color(rgba[0], rgba[1], rgba[2], rgba.Length >= 4 ? rgba[3] : 1f);
        }

        private static void ApplySurfaceSettings(Material mat, MaterialDef def)
        {
            string surface = (def.Surface ?? "Opaque").Trim();
            bool isTransparent = surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase);
            bool alphaClip = def.AlphaClip?.Enabled ?? false;
            float cutoff = def.AlphaClip?.Threshold ?? 0.5f;

            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", cutoff);

            if (alphaClip)
                mat.EnableKeyword("_ALPHATEST_ON");
            else
                mat.DisableKeyword("_ALPHATEST_ON");

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", isTransparent ? 1f : 0f);

            if (isTransparent)
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
