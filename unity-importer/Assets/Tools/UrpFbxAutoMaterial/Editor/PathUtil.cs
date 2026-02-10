// Assets/Tools/UrpFbxAutoMaterial/Editor/PathUtil.cs
// v1.3.0 - Fixed folder creation stability issues
#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    public static class PathUtil
    {
        /// <summary>
        /// パス区切り文字を正規化（バックスラッシュをスラッシュに変換）
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return path.Replace("\\", "/");
        }

        /// <summary>
        /// アセットパスから親フォルダを取得
        /// </summary>
        public static string GetFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "Assets";
            var dir = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(dir) ? "Assets" : Normalize(dir);
        }

        /// <summary>
        /// サブフォルダを作成し、作成完了を保証する
        /// </summary>
        /// <param name="parentFolder">親フォルダのアセットパス</param>
        /// <param name="name">作成するフォルダ名</param>
        /// <returns>作成されたフォルダのアセットパス。失敗時は空文字列</returns>
        public static string EnsureSubfolder(string parentFolder, string name)
        {
            if (string.IsNullOrEmpty(parentFolder))
                parentFolder = "Assets";
            if (string.IsNullOrEmpty(name))
                return parentFolder;
                
            parentFolder = Normalize(parentFolder).TrimEnd('/');
            var path = $"{parentFolder}/{name}";
            
            // 既にフォルダが存在する場合はそのまま返す
            if (AssetDatabase.IsValidFolder(path))
            {
                return path;
            }
            
            // 親フォルダが存在しない場合は再帰的に作成
            if (!AssetDatabase.IsValidFolder(parentFolder))
            {
                var parentDir = GetFolder(parentFolder);
                var parentName = Path.GetFileName(parentFolder);
                var createdParent = EnsureSubfolder(parentDir, parentName);
                if (string.IsNullOrEmpty(createdParent))
                {
                    LogUtil.Error($"Failed to create parent folder: {parentFolder}");
                    return string.Empty;
                }
                parentFolder = createdParent;
                path = $"{parentFolder}/{name}";
            }
            
            // フォルダを作成
            var guid = AssetDatabase.CreateFolder(parentFolder, name);
            
            // 作成結果を検証
            if (string.IsNullOrEmpty(guid))
            {
                LogUtil.Error($"AssetDatabase.CreateFolder returned empty GUID for: {path}");
                return string.Empty;
            }
            
            // 作成完了を同期的に待機
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            
            // 作成確認（防御的チェック）
            if (!AssetDatabase.IsValidFolder(path))
            {
                // 少し待ってから再確認（稀なタイミング問題対策）
                System.Threading.Thread.Sleep(50);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                
                if (!AssetDatabase.IsValidFolder(path))
                {
                    LogUtil.Error($"Folder creation verification failed: {path}");
                    return string.Empty;
                }
            }
            
            LogUtil.Verbose($"Created folder: {path} (GUID: {guid})");
            return path;
        }

        /// <summary>
        /// ベースフォルダと相対パスを結合してアセットパスを生成
        /// </summary>
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

        /// <summary>
        /// アセットパスをフルパスに変換
        /// </summary>
        public static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;
                
            assetPath = Normalize(assetPath);

            if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) &&
                !assetPath.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
            {
                // Assets/ で始まらない場合はそのまま返す（既にフルパスの可能性）
                return assetPath;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
        }

        /// <summary>
        /// ファイルが存在するかチェック（アセットパス対応）
        /// </summary>
        public static bool FileExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
                
            var fullPath = AssetPathToFullPath(assetPath);
            return File.Exists(fullPath);
        }
    }
}
