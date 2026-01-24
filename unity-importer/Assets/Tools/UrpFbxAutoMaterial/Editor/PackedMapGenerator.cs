// Assets/Tools/UrpFbxAutoMaterial/Editor/PackedMapGenerator.cs
#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// URP 用の Packed MetallicGloss テクスチャを生成する
    /// RGB = Metallic, A = Smoothness (= 1 - Roughness)
    /// </summary>
    public static class PackedMapGenerator
    {
        /// <summary>
        /// Packed MetallicGloss テクスチャを生成して Asset パスを返す
        /// </summary>
        /// <returns>生成されたテクスチャの Asset パス。テクスチャがない場合は空文字列</returns>
        public static string BuildPackedMetallicGloss(
            string matName,
            Texture2D? metallicTex,
            Texture2D? roughnessTex,
            float metallicParam,
            float roughnessParam,
            string generatedFolderAssetPath)
        {
            // 最適化: テクスチャが両方ない場合は生成しない（パラメータ値のみで対応）
            if (metallicTex == null && roughnessTex == null)
            {
                LogUtil.Verbose($"PackedMap: Skipping for '{matName}' - using parameter values");
                return string.Empty;
            }

            if (string.IsNullOrEmpty(generatedFolderAssetPath))
                return string.Empty;

            generatedFolderAssetPath = PathUtil.Normalize(generatedFolderAssetPath).TrimEnd('/');
            var outAssetPath = $"{generatedFolderAssetPath}/{SanitizeFileName(matName)}_PackedMetallicGloss.png";
            var outFullPath = AssetPathToFullPath(outAssetPath);

            // 最適化: タイムスタンプ比較でスキップ判定
            if (CanSkipRegeneration(outFullPath, metallicTex, roughnessTex))
            {
                LogUtil.Verbose($"PackedMap: Skipping regeneration for '{matName}' - up to date");
                return outAssetPath;
            }

            // 解像度決定（roughness 優先、次に metallic、デフォルト 512）
            int width = 512;
            int height = 512;

            if (roughnessTex != null)
            {
                width = roughnessTex.width;
                height = roughnessTex.height;
            }
            else if (metallicTex != null)
            {
                width = metallicTex.width;
                height = metallicTex.height;
            }

            // テクスチャ読み出し
            Color[] metalPixels = metallicTex != null
                ? ReadPixelsViaRT(metallicTex, width, height, linear: true)
                : MakeSolidColor(width, height, metallicParam);

            float[] roughPixels = roughnessTex != null
                ? ReadGrayViaRT(roughnessTex, width, height)
                : MakeSolidGray(width, height, roughnessParam);

            // 合成: RGB = Metallic, A = Smoothness
            var outPixels = new Color[width * height];
            for (int i = 0; i < outPixels.Length; i++)
            {
                var m = metalPixels[i];
                float rough = Mathf.Clamp01(roughPixels[i]);
                float smooth = 1.0f - rough;
                outPixels[i] = new Color(m.r, m.g, m.b, smooth);
            }

            // PNG 書き出し
            WritePng(outAssetPath, outFullPath, width, height, outPixels);

            // データテクスチャとして設定（sRGB OFF）
            TextureImportConfigurator.ForceDataTexture(outAssetPath);

            LogUtil.Verbose($"PackedMap: Generated {outAssetPath} ({width}x{height})");

            return outAssetPath;
        }

        /// <summary>
        /// 再生成をスキップできるかどうかを判定
        /// </summary>
        private static bool CanSkipRegeneration(string outFullPath, Texture2D? metallicTex, Texture2D? roughnessTex)
        {
            if (!File.Exists(outFullPath))
                return false;

            try
            {
                var outTime = File.GetLastWriteTimeUtc(outFullPath);
                var srcTime = GetNewestSourceTime(metallicTex, roughnessTex);

                // 出力ファイルがソースより新しければスキップ
                return outTime > srcTime;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ソーステクスチャの最新更新時刻を取得
        /// </summary>
        private static DateTime GetNewestSourceTime(Texture2D? tex1, Texture2D? tex2)
        {
            DateTime newest = DateTime.MinValue;

            if (tex1 != null)
            {
                var path = AssetDatabase.GetAssetPath(tex1);
                if (!string.IsNullOrEmpty(path))
                {
                    var fullPath = AssetPathToFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        var time = File.GetLastWriteTimeUtc(fullPath);
                        if (time > newest) newest = time;
                    }
                }
            }

            if (tex2 != null)
            {
                var path = AssetDatabase.GetAssetPath(tex2);
                if (!string.IsNullOrEmpty(path))
                {
                    var fullPath = AssetPathToFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        var time = File.GetLastWriteTimeUtc(fullPath);
                        if (time > newest) newest = time;
                    }
                }
            }

            // ソースがない場合は常に再生成
            return newest == DateTime.MinValue ? DateTime.MaxValue : newest;
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private static Color[] MakeSolidColor(int width, int height, float value)
        {
            value = Mathf.Clamp01(value);
            var color = new Color(value, value, value, value);
            var arr = new Color[width * height];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = color;
            return arr;
        }

        private static float[] MakeSolidGray(int width, int height, float value)
        {
            value = Mathf.Clamp01(value);
            var arr = new float[width * height];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = value;
            return arr;
        }

        private static float[] ReadGrayViaRT(Texture2D src, int width, int height)
        {
            var colors = ReadPixelsViaRT(src, width, height, linear: true);
            var gray = new float[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                // R チャンネルを roughness として使用
                gray[i] = colors[i].r;
            }
            return gray;
        }

        private static Color[] ReadPixelsViaRT(Texture2D src, int width, int height, bool linear)
        {
            var readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWrite);

            try
            {
                Graphics.Blit(src, rt);

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;

                var tmp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
                tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tmp.Apply();

                RenderTexture.active = prevActive;

                var pixels = tmp.GetPixels();
                UnityEngine.Object.DestroyImmediate(tmp);

                return pixels;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void WritePng(string outAssetPath, string outFullPath, int width, int height, Color[] pixels)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
            tex.SetPixels(pixels);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            // ディレクトリ作成
            var dir = Path.GetDirectoryName(outFullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outFullPath, png);

            // AssetDatabase に反映
            AssetDatabase.ImportAsset(outAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;
                
            assetPath = PathUtil.Normalize(assetPath);

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"assetPath must start with 'Assets/': {assetPath}");
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
        }
    }
}
