#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace UnityEngine.XR.Content.Interaction.Research.Editor
{
    /// <summary>
    /// Moves the research system + supporting environment out of the XRI sample scene
    /// and into a standalone VRResearch_Master scene, via Editor APIs only (no manual
    /// scene YAML edits). Menu: Tools > VR Research > Migrate Scene Now.
    /// </summary>
    public static class SceneMigrationUtility
    {
        const string SourceScenePath = "Assets/XRI_Examples/Scenes/XRI_Examples_Main.unity";
        const string TargetSceneDir = "Assets/VRMaintenanceResearch/Scenes";
        const string TargetScenePath = TargetSceneDir + "/VRResearch_Master.unity";
        const string TempPrefabDir = "Assets/VRMaintenanceResearch/Prefabs/Temp_Migration";
        const string XROriginPrefabPath = "Assets/XRI_Examples/Global/Prefabs/Complete XR Origin Set Up Variant.prefab";

        static readonly string[] MigrationObjectNames =
        {
            "Blockout_Environment",
            "Research_System",
            "Task_A_ComputerAssembly",
            "Task_B_FanRepair",
            "EventSystem",
        };

        [MenuItem("Tools/VR Research/Migrate Scene Now")]
        public static void MigrateSceneNow()
        {
            EnsureFolders();

            var extractedPrefabPaths = ExtractPrefabsFromSourceScene();
            if (extractedPrefabPaths == null)
                return; // error already logged, source scene already closed unsaved

            BuildTargetScene(extractedPrefabPaths);

            CleanupTempPrefabs(extractedPrefabPaths.Values);

            Debug.Log($"[SceneMigration] Migration complete: {TargetScenePath}");
        }

        static void EnsureFolders()
        {
            CreateFolderRecursive("Assets/VRMaintenanceResearch");
            CreateFolderRecursive(TargetSceneDir);
            CreateFolderRecursive("Assets/VRMaintenanceResearch/Prefabs");
            CreateFolderRecursive(TempPrefabDir);
        }

        static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // SaveAsPrefabAsset clones the hierarchy into a new asset without touching or
        // connecting the source GameObject, so the open source scene is never dirtied.
        static Dictionary<string, string> ExtractPrefabsFromSourceScene()
        {
            var result = new Dictionary<string, string>();
            var sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var objName in MigrationObjectNames)
                {
                    var go = FindRootInScene(sourceScene, objName);
                    if (go == null)
                    {
                        Debug.LogError($"[SceneMigration] Source object '{objName}' not found in {SourceScenePath}. Aborting migration.");
                        return null;
                    }

                    string prefabPath = $"{TempPrefabDir}/{objName}.prefab";
                    PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool success);
                    if (!success)
                    {
                        Debug.LogError($"[SceneMigration] Failed to save '{objName}' as prefab at {prefabPath}.");
                        return null;
                    }
                    result[objName] = prefabPath;
                    Debug.Log($"[SceneMigration] Extracted '{objName}' -> {prefabPath}");
                }
            }
            finally
            {
                // removeScene: true, saveScene never called - source file on disk is untouched.
                EditorSceneManager.CloseScene(sourceScene, true);
            }
            return result;
        }

        static GameObject FindRootInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindByNameRecursive(root.transform, name);
                if (found != null) return found;
            }
            return null;
        }

        static GameObject FindByNameRecursive(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            foreach (Transform child in t)
            {
                var found = FindByNameRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static void BuildTargetScene(Dictionary<string, string> prefabPaths)
        {
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var xrOriginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XROriginPrefabPath);
            if (xrOriginPrefab == null)
            {
                Debug.LogError($"[SceneMigration] XR Origin prefab not found at {XROriginPrefabPath}. Target scene not saved.");
                return;
            }
            var xrOriginInstance = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab, newScene);

            var instances = new Dictionary<string, GameObject>();
            foreach (var kv in prefabPaths)
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value);
                instances[kv.Key] = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, newScene);
            }

            RelinkReferences(xrOriginInstance, instances);

            // The 5 migrated objects are only instances of a *temporary* prefab used to
            // carry data out of the source scene; unpack them so the target scene no
            // longer depends on Temp_Migration files once those get deleted below.
            // The XR Origin instance stays a real (non-temp) prefab link, left packed.
            foreach (var instance in instances.Values)
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            EditorSceneManager.SaveScene(newScene, TargetScenePath);
            Debug.Log($"[SceneMigration] Saved target scene: {TargetScenePath}");
        }

        // Only cross-hierarchy references break on prefab extraction: VRMovementTracker's
        // head/controller transforms live in the XR Origin prefab, and VRSessionManager's
        // task groups/door blockers/teleport areas live in the Task_A/Task_B/Blockout_Environment
        // prefabs - all outside Research_System's own hierarchy. Everything else (tracker/
        // logger/counterbalancer) is internal to Research_System and survives extraction as-is.
        static void RelinkReferences(GameObject xrOriginInstance, Dictionary<string, GameObject> instances)
        {
            if (!instances.TryGetValue("Research_System", out var researchSystem) || researchSystem == null)
                return;

            var cameraOffset = xrOriginInstance.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;
            Transform left = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform right = cameraOffset != null ? cameraOffset.Find("Right Controller") : null;

            var tracker = researchSystem.GetComponent<VRMovementTracker>();
            if (tracker != null)
            {
                var so = new SerializedObject(tracker);
                so.FindProperty("_headTransform").objectReferenceValue = head;
                so.FindProperty("_leftControllerTransform").objectReferenceValue = left;
                so.FindProperty("_rightControllerTransform").objectReferenceValue = right;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[SceneMigration] VRMovementTracker relinked: head={head != null} left={left != null} right={right != null}");
            }
            else
            {
                Debug.LogWarning("[SceneMigration] VRMovementTracker not found on Research_System - head/controller transforms not relinked.");
            }

            var sessionManager = researchSystem.GetComponent<VRSessionManager>();
            if (sessionManager != null)
            {
                var soSession = new SerializedObject(sessionManager);
                if (instances.TryGetValue("Task_A_ComputerAssembly", out var taskA))
                    soSession.FindProperty("_taskAGroup").objectReferenceValue = taskA;
                if (instances.TryGetValue("Task_B_FanRepair", out var taskB))
                    soSession.FindProperty("_taskBGroup").objectReferenceValue = taskB;

                if (instances.TryGetValue("Blockout_Environment", out var blockout) && blockout != null)
                {
                    var doorA = FindByNameRecursive(blockout.transform, "Door_Blocker_A");
                    var doorB = FindByNameRecursive(blockout.transform, "Door_Blocker_B");
                    var floorComputer = FindByNameRecursive(blockout.transform, "Computer_Room_Floor");
                    var floorFan = FindByNameRecursive(blockout.transform, "Fan_Room_Floor");
                    if (doorA != null) soSession.FindProperty("_doorBlockerA").objectReferenceValue = doorA;
                    if (doorB != null) soSession.FindProperty("_doorBlockerB").objectReferenceValue = doorB;
                    if (floorComputer != null) soSession.FindProperty("_teleportAreaComputer").objectReferenceValue = floorComputer.GetComponent<TeleportationArea>();
                    if (floorFan != null) soSession.FindProperty("_teleportAreaFan").objectReferenceValue = floorFan.GetComponent<TeleportationArea>();
                    Debug.Log($"[SceneMigration] Gating refs relinked: doorA={doorA != null} doorB={doorB != null} teleComputer={floorComputer != null} teleFan={floorFan != null}");
                }

                soSession.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[SceneMigration] VRSessionManager task group references relinked.");
            }
            else
            {
                Debug.LogWarning("[SceneMigration] VRSessionManager not found on Research_System - task groups not relinked.");
            }
        }

        static void CleanupTempPrefabs(IEnumerable<string> paths)
        {
            foreach (var p in paths)
                AssetDatabase.DeleteAsset(p);
            AssetDatabase.Refresh();
            Debug.Log("[SceneMigration] Temp_Migration prefabs cleaned up.");
        }
    }
}
#endif
