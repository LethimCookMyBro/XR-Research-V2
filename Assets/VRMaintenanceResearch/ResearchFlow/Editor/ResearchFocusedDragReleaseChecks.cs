#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFocusedDragReleaseChecks
    {
        public static string Run(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("FocusedDragRelease") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var view = root.AddComponent<ResearchFlowView>();
                var head = new GameObject("Head", typeof(Camera)); head.transform.SetParent(root.transform);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1;
                head.transform.position = new Vector3(500, 1.65f, 500); view.Head = head.transform;
                var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Canvas)); panelObject.transform.SetParent(root.transform);
                var rect = (RectTransform)panelObject.transform; rect.sizeDelta = new Vector2(700, 500); rect.localScale = Vector3.one * .001f;
                var panel = panelObject.AddComponent<ResearchPanelMotion>(); panel.enabled = false; panel.view = view; panel.panelId = "reference";
                var grip = new GameObject("Grip", typeof(RectTransform)); grip.transform.SetParent(panel.transform);
                var handle = grip.AddComponent<ResearchDragHandle>(); handle.panel = panel; handle.enabled = false;
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                void Set(string name, object value) => typeof(ResearchDragHandle).GetField(name, flags).SetValue(handle, value);
                var clearPosition = head.transform.position + Vector3.forward * 1.2f;
                panel.transform.SetPositionAndRotation(clearPosition, Quaternion.identity);
                if (!panel.IsReadableFromHead || !panel.PlacementClear(clearPosition, Quaternion.identity))
                    throw new InvalidOperationException("Fixture start must be readable and world-clear.");
                Set("hasClearPose", true); Set("lastClearPosition", clearPosition); Set("lastClearRotation", Quaternion.identity);
                Set("dragging", true); Set("moved", true);
                panel.transform.position += Vector3.right * 4;
                bool beforeReadable = panel.IsReadableFromHead;
                bool beforeWorldClear = panel.PlacementClear(panel.transform.position, panel.transform.rotation);
                if (beforeReadable || !beforeWorldClear) throw new InvalidOperationException("Fixture must reproduce clear but off-view release.");
                typeof(ResearchDragHandle).GetMethod("Finish", flags).Invoke(handle, new object[] { true, "focused_valid_release" });
                bool passed = handle.ReleaseRecovered && panel.IsReadableFromHead && !handle.IsDragging;
                string summary = "DRAG_RELEASE_" + (passed ? "PASS" : "FAIL") + " before_readable=" + beforeReadable + " world_clear=" + beforeWorldClear
                    + " after_readable=" + panel.IsReadableFromHead + " recovered=" + handle.ReleaseRecovered;
                File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(new { summary, passed, beforeReadable, beforeWorldClear,
                    afterReadable = panel.IsReadableFromHead, recovered = handle.ReleaseRecovered, hardware_verified = false,
                    scope = "Actual ResearchDragHandle release with synthetic camera and world-clear off-view panel in isolated Unity preview scene." }, Formatting.Indented));
                return summary;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif
