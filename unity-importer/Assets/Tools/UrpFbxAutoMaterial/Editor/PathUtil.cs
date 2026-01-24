// Assets/Tools/UrpFbxAutoMaterial/Editor/PathUtil.cs
#nullable enable
using System.IO;
using UnityEditor;

namespace UrpFbxAutoMaterial
{
    public static class PathUtil
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return path.Replace("\\", "/");
        }

        public static string GetFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "Assets";
            var dir = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(dir) ? "Assets" : Normalize(dir);
        }

        public static string EnsureSubfolder(string parentFolder, string name)
        {
            if (string.IsNullOrEmpty(parentFolder))
                parentFolder = "Assets";
            if (string.IsNullOrEmpty(name))
                return parentFolder;
                
            parentFolder = Normalize(parentFolder).TrimEnd('/');
            var path = $"{parentFolder}/{name}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parentFolder, name);
            }
            return path;
        }

        public static string CombineToAssetPath(string baseFolderAssetPath, string relative)
        {
            if (string.IsNullOrEmpty(baseFolderAssetPath))
                baseFolderAssetPath = "Assets";
            if (string.IsNullOrEmpty(relative))
                return baseFolderAssetPath;
                
            baseFolderAssetPath = Normalize(baseFolderAssetPath).TrimEnd('/');
            relative = Normalize(relative).TrimStart('/');
            return $"{baseFolderAssetPath}/{relative}";
        }
    }
}
