#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine.XR.Content.Interaction.Research.Editor
{
    /// <summary>
    /// Swaps the placeholder fan blade mock for the real research model as a visual-only
    /// child, leaving the mock's physics/interaction components untouched. Menu:
    /// Tools > VR Research > Install Fan Blade Asset.
    /// </summary>
    public static class SceneAssetSwapUtility
    {
        const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity";
        const string FbxPath = "Assets/XRI_Examples/Global/Assets/Prototype_Import/Fan_Table_Yethiel.fbx";
        const string MockName = "Mock_Fan_Blade";
        const string SourceNodeName = "Cylinder.000";
        const string VisualChildName = "Visual_Model";

        // Target the visual model's largest bound dimension at this size, matching the
        // mock's own collider footprint (~0.3m).
        const float TargetBoundsSize = 0.3f;

        [MenuItem("Tools/VR Research/Install Fan Blade Asset")]
        public static void InstallFanBladeAsset()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var mock = FindInScene(scene, MockName);
            if (mock == null)
            {
                Debug.LogError($"[SceneAssetSwap] '{MockName}' not found in {ScenePath}.");
                return;
            }

            var meshRenderer = mock.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogError($"[SceneAssetSwap] '{MockName}' has no MeshRenderer to disable.");
                return;
            }
            meshRenderer.enabled = false;

            var fbxRoot = AssetDatabase.LoadMainAssetAtPath(FbxPath) as GameObject;
            if (fbxRoot == null)
            {
                Debug.LogError($"[SceneAssetSwap] Could not load FBX at {FbxPath}.");
                return;
            }

            // Instantiate the full model temporarily so the target node can be extracted;
            // everything else gets destroyed below, so only Cylinder.000 ever reaches the scene.
            var tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot, scene);
            // Prefab instance children can't be reparented out directly; unpack first (same
            // approach as SceneMigrationUtility.cs).
            PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var sourceNode = FindByNameRecursive(tempInstance.transform, SourceNodeName);
            if (sourceNode == null)
            {
                Debug.LogError($"[SceneAssetSwap] Node '{SourceNodeName}' not found in {FbxPath}.");
                Object.DestroyImmediate(tempInstance);
                return;
            }

            var meshFilter = sourceNode.GetComponent<MeshFilter>();
            var sourceRenderer = sourceNode.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null || sourceRenderer == null)
            {
                Debug.LogError($"[SceneAssetSwap] '{SourceNodeName}' has no renderable mesh.");
                Object.DestroyImmediate(tempInstance);
                return;
            }

            // Measure the node's authored (as-imported) world bounds while it still sits under
            // the identity-transform temp root, so the fit/orientation math below is independent
            // of anything already done to Mock_Fan_Blade's own (non-uniform) transform.
            Bounds worldBounds = sourceRenderer.bounds;
            float maxDim = Mathf.Max(worldBounds.size.x, worldBounds.size.y, worldBounds.size.z);
            float uniformScale = maxDim > 0f ? TargetBoundsSize / maxDim : 1f;

            Quaternion authoredLocalRotation = sourceNode.localRotation;
            Vector3 meshLocalCenter = meshFilter.sharedMesh.bounds.center;

            var existing = mock.transform.Find(VisualChildName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            sourceNode.name = VisualChildName;
            sourceNode.SetParent(mock.transform, worldPositionStays: false);

            // Uniform local scale on Visual_Model itself (as required); Mock_Fan_Blade's own
            // scale is left untouched per instructions, so this is uniform in local space even
            // though the mock's non-uniform parent scale still applies on top in world space.
            sourceNode.localScale = Vector3.one * uniformScale;
            sourceNode.localRotation = authoredLocalRotation;

            // Cancel the mesh's own pivot-to-bounds-center offset so the visual center lands
            // exactly on Mock_Fan_Blade's local origin (its collider is centered there too).
            Vector3 offsetFromPivot = authoredLocalRotation * (meshLocalCenter * uniformScale);
            sourceNode.localPosition = -offsetFromPivot;

            string summary = $"[SceneAssetSwap] Installed '{VisualChildName}' under '{MockName}'. " +
                $"localScale={sourceNode.localScale:F6} localPosition={sourceNode.localPosition:F6} " +
                $"localRotation(euler)={sourceNode.localEulerAngles:F6}";

            Object.DestroyImmediate(tempInstance);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(summary);
        }

        static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindByNameRecursive(root.transform, name);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        static Transform FindByNameRecursive(Transform t, string name)
        {
            if (t.name == name)
                return t;
            foreach (Transform child in t)
            {
                var found = FindByNameRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
#endif
