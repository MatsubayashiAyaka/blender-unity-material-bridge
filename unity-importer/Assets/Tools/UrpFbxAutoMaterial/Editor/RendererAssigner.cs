// Assets/Tools/UrpFbxAutoMaterial/Editor/RendererAssigner.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UrpFbxAutoMaterial
{
    /// <summary>
    /// FBX 内のレンダラーにマテリアルを割り当てる
    /// </summary>
    public static class RendererAssigner
    {
        /// <summary>
        /// Manifest に基づいてマテリアルを割り当てる
        /// </summary>
        public static void AssignMaterials(
            string fbxPath,
            MaterialManifest manifest,
            Dictionary<string, Material> materials)
        {
            if (string.IsNullOrEmpty(fbxPath) || manifest == null || materials == null || materials.Count == 0)
                return;

            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                LogUtil.Warn($"Failed to load FBX as GameObject: {fbxPath}");
                return;
            }

            // メッシュ名→マテリアルスロットのマッピングを構築
            var meshToMaterials = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var meshEntry in manifest.Meshes)
            {
                if (meshEntry == null || string.IsNullOrEmpty(meshEntry.Name))
                    continue;
                meshToMaterials[meshEntry.Name] = meshEntry.MaterialSlots;
            }

            // すべてのレンダラーを取得
            var renderers = fbxAsset.GetComponentsInChildren<Renderer>(includeInactive: true);

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                // メッシュ名を取得
                string? meshName = GetMeshName(renderer);
                if (string.IsNullOrEmpty(meshName))
                    continue;

                // マテリアルスロットを取得
                if (!TryGetMaterialSlots(meshName, meshToMaterials, out var slots))
                    continue;

                if (slots == null || slots.Count == 0)
                    continue;

                // マテリアル配列を構築
                var newMaterials = new Material[slots.Count];
                bool anyAssigned = false;

                for (int i = 0; i < slots.Count; i++)
                {
                    var slotName = slots[i];
                    if (string.IsNullOrEmpty(slotName))
                        continue;

                    if (materials.TryGetValue(slotName, out var mat) && mat != null)
                    {
                        newMaterials[i] = mat;
                        anyAssigned = true;
                    }
                }

                if (anyAssigned)
                {
                    renderer.sharedMaterials = newMaterials;
                    LogUtil.Verbose($"Assigned materials to '{meshName}': [{string.Join(", ", slots)}]");
                }
            }
        }

        /// <summary>
        /// レンダラーからメッシュ名を取得
        /// </summary>
        private static string? GetMeshName(Renderer renderer)
        {
            if (renderer == null)
                return null;

            // MeshFilter から取得を試みる
            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    return filter.sharedMesh.name;
                }
            }

            // SkinnedMeshRenderer から取得
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                if (skinnedRenderer.sharedMesh != null)
                {
                    return skinnedRenderer.sharedMesh.name;
                }
            }

            // フォールバック: GameObject 名
            return renderer.gameObject.name;
        }

        /// <summary>
        /// メッシュ名に対応するマテリアルスロットを取得
        /// </summary>
        private static bool TryGetMaterialSlots(
            string meshName,
            Dictionary<string, List<string>> meshToMaterials,
            out List<string>? slots)
        {
            slots = null;

            if (string.IsNullOrEmpty(meshName))
                return false;

            // 完全一致
            if (meshToMaterials.TryGetValue(meshName, out slots))
                return true;

            // 大文字小文字を無視した一致
            var key = meshToMaterials.Keys.FirstOrDefault(k =>
                string.Equals(k, meshName, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                slots = meshToMaterials[key];
                return true;
            }

            // 部分一致（メッシュ名が manifest のキーを含む）
            key = meshToMaterials.Keys.FirstOrDefault(k =>
                meshName.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                slots = meshToMaterials[key];
                return true;
            }

            // 部分一致（manifest のキーがメッシュ名を含む）
            key = meshToMaterials.Keys.FirstOrDefault(k =>
                k.Contains(meshName, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                slots = meshToMaterials[key];
                return true;
            }

            return false;
        }
    }
}
