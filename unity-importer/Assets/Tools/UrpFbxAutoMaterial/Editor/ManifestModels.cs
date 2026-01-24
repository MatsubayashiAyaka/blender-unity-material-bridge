// Assets/Tools/UrpFbxAutoMaterial/Editor/ManifestModels.cs
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// Manifest JSON のルートオブジェクト
    /// </summary>
    public sealed class MaterialManifest
    {
        [JsonProperty("manifest_version")]
        public string? ManifestVersion { get; set; }

        [JsonProperty("pipeline")]
        public string? Pipeline { get; set; }

        [JsonProperty("asset")]
        public AssetInfo? Asset { get; set; }

        [JsonProperty("meshes")]
        public List<MeshEntry> Meshes { get; set; } = new();

        [JsonProperty("materials")]
        public Dictionary<string, MaterialDef> Materials { get; set; } = new();
    }

    /// <summary>
    /// アセット情報
    /// </summary>
    public sealed class AssetInfo
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("export_time_iso")]
        public string? ExportTimeIso { get; set; }

        [JsonProperty("blender_version")]
        public string? BlenderVersion { get; set; }

        [JsonProperty("unit_scale")]
        public float UnitScale { get; set; } = 1.0f;

        [JsonProperty("axis")]
        public AxisInfo? Axis { get; set; }
    }

    /// <summary>
    /// 軸設定
    /// </summary>
    public sealed class AxisInfo
    {
        [JsonProperty("forward")]
        public string? Forward { get; set; }

        [JsonProperty("up")]
        public string? Up { get; set; }
    }

    /// <summary>
    /// メッシュエントリ
    /// </summary>
    public sealed class MeshEntry
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("material_slots")]
        public List<string> MaterialSlots { get; set; } = new();

        [JsonProperty("object_name")]
        public string? ObjectName { get; set; }

        [JsonProperty("mesh_data_name")]
        public string? MeshDataName { get; set; }
    }

    /// <summary>
    /// マテリアル定義
    /// </summary>
    public sealed class MaterialDef
    {
        [JsonProperty("shader")]
        public string? Shader { get; set; }

        [JsonProperty("surface")]
        public string? Surface { get; set; }

        [JsonProperty("alpha_clip")]
        public AlphaClipInfo? AlphaClip { get; set; }

        [JsonProperty("base_color_factor")]
        public float[]? BaseColorFactor { get; set; }

        [JsonProperty("textures")]
        public TextureRefs? Textures { get; set; }

        [JsonProperty("params")]
        public MaterialParams? Params { get; set; }
    }

    /// <summary>
    /// アルファクリップ設定
    /// </summary>
    public sealed class AlphaClipInfo
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("threshold")]
        public float Threshold { get; set; } = 0.5f;
    }

    /// <summary>
    /// テクスチャ参照群
    /// </summary>
    public sealed class TextureRefs
    {
        [JsonProperty("base_color")]
        public TextureRef? BaseColor { get; set; }

        [JsonProperty("metallic")]
        public TextureRef? Metallic { get; set; }

        [JsonProperty("roughness")]
        public TextureRef? Roughness { get; set; }

        [JsonProperty("normal")]
        public TextureRef? Normal { get; set; }

        [JsonProperty("emission")]
        public TextureRef? Emission { get; set; }

        [JsonProperty("ao")]
        public TextureRef? AO { get; set; }
    }

    /// <summary>
    /// 個別テクスチャ参照
    /// </summary>
    public sealed class TextureRef
    {
        [JsonProperty("path")]
        public string? Path { get; set; }

        [JsonProperty("srgb")]
        public bool Srgb { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("scale")]
        public float Scale { get; set; } = 1.0f;

        /// <summary>
        /// パスが有効かどうかを検証（親ディレクトリ参照を禁止）
        /// </summary>
        public bool IsValidPath()
        {
            if (string.IsNullOrEmpty(Path))
                return false;
            // 親ディレクトリ参照 "../" を禁止
            return !Path.Contains("../") && !Path.Contains("..\\");
        }
    }

    /// <summary>
    /// マテリアルパラメータ
    /// </summary>
    public sealed class MaterialParams
    {
        [JsonProperty("metallic")]
        public float Metallic { get; set; }

        [JsonProperty("roughness")]
        public float Roughness { get; set; } = 0.5f;

        [JsonProperty("emission_color")]
        public float[]? EmissionColor { get; set; }

        [JsonProperty("emission_strength")]
        public float EmissionStrength { get; set; } = 1.0f;
    }

    /// <summary>
    /// 内部使用：メッシュ→マテリアル割当情報
    /// </summary>
    public sealed class MeshMaterialAssignment
    {
        public string MeshName { get; set; } = string.Empty;
        public List<string> MaterialNames { get; set; } = new();
    }
}
