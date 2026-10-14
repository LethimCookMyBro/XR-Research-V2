#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Copies the verified S1 Task A/B subtrees from the physical TEST scene into the canonical ResearchFlow scene.
    // Both scenes share every other root, so references leaving a subtree are re-bound by hierarchy path. Only the canonical scene is saved.
    public static class ResearchS1SceneIntegration
    {
        const string Source = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_S1_Physical_TEST.unity";
        static readonly string[] Subtrees = { "ResearchFlow_Session/ResearchFlow_TaskA_Port/ResearchS1TaskA_Workspace", "ResearchFlow_Session/ResearchFlow_TaskB_Port/ResearchS1TaskB" };

        public static string IntegrateIntoCanonical()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Integrate in Edit Mode.");
            var canonical = EditorSceneManager.OpenScene(ResearchFlowSceneBuilder.ScenePath, OpenSceneMode.Single);
            var source = EditorSceneManager.OpenScene(Source, OpenSceneMode.Additive);
            try
            {
                var report = new List<string>();
                foreach (var path in Subtrees)
                {
                    var from = Find(source, path) ?? throw new InvalidOperationException("Source subtree missing: " + path);
                    var parentPath = path.Substring(0, path.LastIndexOf('/'));
                    var parent = Find(canonical, parentPath) ?? throw new InvalidOperationException("Canonical parent missing: " + parentPath);
                    var existing = Find(canonical, path);
                    if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                    SceneManager.SetActiveScene(canonical);
                    Selection.objects = new UnityEngine.Object[] { from.gameObject };
                    Unsupported.CopyGameObjectsToPasteboard();
                    Unsupported.PasteGameObjectsFromPasteboard();
                    var copy = Selection.activeGameObject.transform;
                    if (copy.gameObject.scene != canonical) { copy.SetParent(null, false); SceneManager.MoveGameObjectToScene(copy.gameObject, canonical); }
                    copy.SetParent(parent, false);
                    copy.name = from.name;
                    copy.SetLocalPositionAndRotation(from.localPosition, from.localRotation); copy.localScale = from.localScale;
                    report.Add(path + " rebound=" + Rebind(from, copy, canonical));
                }
                // The S1 parts replace static environment props (e.g. Computer_TaskCase) that the TEST scene switched off.
                var shared = source.GetRootGameObjects().First(g => g.name == "Environment_V2").transform;
                foreach (var node in shared.GetComponentsInChildren<Transform>(true))
                {
                    var twin = Find(canonical, PathOf(node));
                    if (twin && twin.gameObject.activeSelf != node.gameObject.activeSelf)
                    {
                        twin.gameObject.SetActive(node.gameObject.activeSelf);
                        report.Add(PathOf(node) + " active=" + node.gameObject.activeSelf);
                    }
                }
                EditorSceneManager.CloseScene(source, true);
                EditorSceneManager.MarkSceneDirty(canonical);
                if (!EditorSceneManager.SaveScene(canonical)) throw new InvalidOperationException("Canonical scene save failed.");
                return "S1_CANONICAL_INTEGRATED " + string.Join("; ", report);
            }
            finally
            {
                if (source.isLoaded) EditorSceneManager.CloseScene(source, true);
            }
        }

        // Every object reference in the copy is set to what the source referenced: inside the subtree -> the copied twin,
        // other scene objects -> the same hierarchy path in the canonical scene, assets -> unchanged.
        static int Rebind(Transform from, Transform copy, Scene canonical)
        {
            var twins = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var sourceNodes = from.GetComponentsInChildren<Transform>(true);
            var copyNodes = copy.GetComponentsInChildren<Transform>(true);
            if (sourceNodes.Length != copyNodes.Length) throw new InvalidOperationException("Pasted hierarchy differs from source.");
            for (int i = 0; i < sourceNodes.Length; i++)
            {
                twins[sourceNodes[i].gameObject] = copyNodes[i].gameObject;
                var a = sourceNodes[i].GetComponents<Component>(); var b = copyNodes[i].GetComponents<Component>();
                if (a.Length != b.Length) throw new InvalidOperationException("Component mismatch at " + sourceNodes[i].name);
                for (int k = 0; k < a.Length; k++) if (a[k]) twins[a[k]] = b[k];
            }
            int changed = 0;
            foreach (var pair in twins.Where(p => p.Key is Component).ToArray())
            {
                var original = new SerializedObject(pair.Key); var target = new SerializedObject(pair.Value);
                var it = original.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || Structural.Contains(it.propertyPath) || it.propertyPath.StartsWith("m_Children", StringComparison.Ordinal)) continue;
                    var value = it.objectReferenceValue;
                    UnityEngine.Object expected = value;
                    if (value && twins.TryGetValue(value, out var twin)) expected = twin;
                    else if (value && SceneOf(value).IsValid() && SceneOf(value) != canonical) expected = Counterpart(value, canonical)
                        ?? throw new InvalidOperationException("No canonical counterpart for " + value.name + " at " + it.propertyPath);
                    var property = target.FindProperty(it.propertyPath);
                    if (property != null && property.objectReferenceValue != expected) { property.objectReferenceValue = expected; changed++; }
                }
                target.ApplyModifiedPropertiesWithoutUndo();
            }
            return changed;
        }

        // Hierarchy and prefab-instance bookkeeping belongs to the paste itself.
        static readonly HashSet<string> Structural = new HashSet<string> { "m_GameObject", "m_Father", "m_PrefabInstance", "m_CorrespondingSourceObject", "m_PrefabAsset" };

        static Scene SceneOf(UnityEngine.Object value) => value is GameObject g ? g.scene : value is Component c ? c.gameObject.scene : default;

        static UnityEngine.Object Counterpart(UnityEngine.Object value, Scene canonical)
        {
            var go = value as GameObject ?? ((Component)value).gameObject;
            var twin = Find(canonical, PathOf(go.transform));
            if (!twin) return null;
            if (value is GameObject) return twin.gameObject;
            var all = go.GetComponents(value.GetType()); var twins = twin.GetComponents(value.GetType());
            int index = Array.IndexOf(all, value);
            return index >= 0 && index < twins.Length ? twins[index] : null;
        }

        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        static Transform Find(Scene scene, string path)
        {
            var parts = path.Split('/');
            var node = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0])?.transform;
            for (int i = 1; node && i < parts.Length; i++) node = node.Cast<Transform>().FirstOrDefault(t => t.name == parts[i]);
            return node;
        }
    }
}
#endif
