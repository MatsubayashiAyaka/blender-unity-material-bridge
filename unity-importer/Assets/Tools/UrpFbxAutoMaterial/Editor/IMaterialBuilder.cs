// Assets/Tools/UrpFbxAutoMaterial/Editor/IMaterialBuilder.cs
// v1.1.0
#nullable enable
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// マテリアルビルダーのインターフェース
    /// </summary>
    public interface IMaterialBuilder
    {
        /// <summary>
        /// シェーダーが利用可能かどうか
        /// </summary>
        bool IsShaderAvailable();

        /// <summary>
        /// シェーダーが見つからない場合のエラーメッセージ
        /// </summary>
        string GetShaderNotFoundMessage();

        /// <summary>
        /// マテリアルを作成または更新する
        /// </summary>
        Material? BuildOrUpdate(
            string matName,
            MaterialDef def,
            string fbxFolderAssetPath,
            string materialsFolderAssetPath,
            string generatedFolderAssetPath);
    }
}
