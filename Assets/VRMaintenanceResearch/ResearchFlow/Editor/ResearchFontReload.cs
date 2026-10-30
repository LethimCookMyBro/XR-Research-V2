#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Play Mode fills dynamic TMP atlases in memory and the build guard then refuses. Drop the loaded copies so the committed files load again.
    public static class ResearchFontReload
    {
        static readonly string[] Paths = {
            "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_English.asset",
            "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Japanese.asset",
            "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_Thai.asset",
            "Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset" };

        public static string Reload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new System.InvalidOperationException("Editor not idle");
            foreach (var path in Paths)
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is ScriptableObject || asset is Texture2D || asset is Material) Resources.UnloadAsset(asset);
            return string.Join(" | ", Paths.Select(path => System.IO.Path.GetFileName(path) + " dirty=" + AssetDatabase.LoadAllAssetsAtPath(path).Count(EditorUtility.IsDirty)));
        }
    }
}
#endif
