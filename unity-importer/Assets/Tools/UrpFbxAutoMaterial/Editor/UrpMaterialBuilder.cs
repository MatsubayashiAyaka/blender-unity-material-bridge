// Assets/Tools/UrpFbxAutoMaterial/Editor/UrpMaterialBuilder.cs
#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// URP/Lit マテリアルを生成・更新する
    /// </summary>
    public static class UrpMaterialBuilder
    {
        // URP Smoothness Source の定数
        private const float SmoothnessSource_MetallicAlpha = 0f;

        /// <summary>
        /// マテリアルを生成または更新する
        /// </summary>
        public static Material? BuildOrUpdate(
            string matName,
            MaterialDef def,
            Shader urpLit,
            string fbxFolderAssetPath,
            string materialsFolderAssetPath,
            string generatedFolderAssetPath)
        {
            if (string.IsNullOrEmpty(matName) || def == null || urpLit == null)
                return null;

            var matAssetPath = $"{materialsFolderAssetPath}/{SanitizeFileName(matName)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);

            bool isNew = mat == null;
            if (isNew)
            {
                mat = new Material(urpLit);
                AssetDatabase.CreateAsset(mat, matAssetPath);
                LogUtil.Verbose($"Created new material: {matAssetPath}");
            }
            else
            {
                mat!.shader = urpLit;
                LogUtil.Verbose($"Updating existing material: {matAssetPath}");
            }

            // BaseColor factor
            if (mat.HasProperty("_BaseColor"))
            {
                var color = ToColor(def.BaseColorFactor, Color.white);
                mat.SetColor("_BaseColor", color);
            }

            // BaseMap
            var baseTex = LoadTexture(def.Textures?.BaseColor, fbxFolderAssetPath);
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", baseTex);
            }

            // Normal
            var normalTex = LoadTexture(def.Textures?.Normal, fbxFolderAssetPath);
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normalTex);
                if (normalTex != null)
                {
                    mat.EnableKeyword("_NORMALMAP");
                    if (mat.HasProperty("_BumpScale"))
                    {
                        var scale = def.Textures?.Normal?.Scale ?? 1.0f;
                        mat.SetFloat("_BumpScale", scale);
                    }
                }
            }

            // AO
            var aoTex = LoadTexture(def.Textures?.AO, fbxFolderAssetPath);
            if (mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", aoTex);
            }

            // Emission
            var emissionTex = LoadTexture(def.Textures?.Emission, fbxFolderAssetPath);
            if (mat.HasProperty("_EmissionMap"))
            {
                mat.SetTexture("_EmissionMap", emissionTex);
                if (emissionTex != null)
                    mat.EnableKeyword("_EMISSION");
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                var emissionColor = ToColor(def.Params?.EmissionColor, Color.black);
                float emissionStrength = def.Params?.EmissionStrength ?? 1.0f;
                mat.SetColor("_EmissionColor", emissionColor * emissionStrength);

                if (emissionTex != null || emissionColor.maxColorComponent > 0.0f)
                {
                    mat.EnableKeyword("_EMISSION");
                }
            }

            // Metallic / Roughness (Packed Map)
            var metallicTex = LoadTexture(def.Textures?.Metallic, fbxFolderAssetPath);
            var roughnessTex = LoadTexture(def.Textures?.Roughness, fbxFolderAssetPath);

            float metallicParam = def.Params?.Metallic ?? 0.0f;
            float roughnessParam = def.Params?.Roughness ?? 0.5f;

            // PackedMap を生成（テクスチャがある場合のみ）
            string packedPath = PackedMapGenerator.BuildPackedMetallicGloss(
                matName,
                metallicTex,
                roughnessTex,
                metallicParam,
                roughnessParam,
                generatedFolderAssetPath
            );

            // Smoothness Source を明示的に設定（Metallic Alpha）
            if (mat.HasProperty("_SmoothnessTextureChannel"))
            {
                mat.SetFloat("_SmoothnessTextureChannel", SmoothnessSource_MetallicAlpha);
            }

            if (!string.IsNullOrEmpty(packedPath))
            {
                var packedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);

                if (packedTex != null && mat.HasProperty("_MetallicGlossMap"))
                {
                    mat.SetTexture("_MetallicGlossMap", packedTex);

                    // Smoothness はマップの Alpha を使用するため 1.0 に設定
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 1.0f);

                    // Metallic も 1.0 に設定（マップから読み取るため）
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 1.0f);

                    LogUtil.Verbose($"Applied PackedMetallicGloss map");
                }
            }
            else
            {
                // PackedMap がない場合はパラメータ値を直接使用
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", metallicParam);

                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 1.0f - roughnessParam);

                // MetallicGlossMap をクリア（以前の設定が残っている可能性）
                if (mat.HasProperty("_MetallicGlossMap"))
                    mat.SetTexture("_MetallicGlossMap", null);

                LogUtil.Verbose($"Using params: Metallic={metallicParam}, Smoothness={1.0f - roughnessParam}");
            }

            // Surface / AlphaClip
            ApplySurfaceSettings(mat, def);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// ファイル名をサニタイズ
        /// </summary>
        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        /// <summary>
        /// テクスチャをロード（パス検証付き）
        /// </summary>
        private static Texture2D? LoadTexture(TextureRef? tref, string fbxFolderAssetPath)
        {
            if (tref == null || string.IsNullOrEmpty(tref.Path))
                return null;

            // パス検証（親ディレクトリ参照禁止）
            if (!tref.IsValidPath())
            {
                LogUtil.Warn($"Invalid texture path (skipped): {tref.Path}");
                return null;
            }

            var texAssetPath = PathUtil.CombineToAssetPath(fbxFolderAssetPath, tref.Path);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texAssetPath);
            
            if (tex == null)
            {
                LogUtil.Warn($"Failed to load texture: {texAssetPath}");
            }

            return tex;
        }

        /// <summary>
        /// float 配列から Color を生成
        /// </summary>
        private static Color ToColor(float[]? rgba, Color fallback)
        {
            if (rgba == null || rgba.Length < 3)
                return fallback;

            float r = rgba[0];
            float g = rgba[1];
            float b = rgba[2];
            float a = rgba.Length >= 4 ? rgba[3] : 1.0f;

            return new Color(r, g, b, a);
        }

        /// <summary>
        /// サーフェス設定を適用
        /// </summary>
        private static void ApplySurfaceSettings(Material mat, MaterialDef def)
        {
            string surface = (def.Surface ?? "Opaque").Trim();
            bool isTransparent = surface.Equals("Transparent", StringComparison.OrdinalIgnoreCase);
            bool alphaClip = def.AlphaClip?.Enabled ?? false;
            float cutoff = def.AlphaClip?.Threshold ?? 0.5f;

            // Alpha Clip
            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);

            if (mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", cutoff);

            if (alphaClip)
                mat.EnableKeyword("_ALPHATEST_ON");
            else
                mat.DisableKeyword("_ALPHATEST_ON");

            // Surface Type
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", isTransparent ? 1f : 0f);

            if (isTransparent)
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
