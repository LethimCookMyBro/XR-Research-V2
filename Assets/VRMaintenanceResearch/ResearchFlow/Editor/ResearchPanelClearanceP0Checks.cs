#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchPanelClearanceP0Checks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Requires stopped Editor without a running XR subsystem.");
            var output = Path.GetFullPath(evidenceDirectory);
            if (!Path.IsPathRooted(evidenceDirectory) || Directory.Exists(output) || File.Exists(output))
                throw new IOException("Use a fresh absolute external evidence directory.");
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
            if (output.Equals(project, StringComparison.OrdinalIgnoreCase) || output.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Keep fixture evidence outside the project.");
            Directory.CreateDirectory(output);
            var caller = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var rows = new List<object>();
            int checks = 0, failed = 0;
            string policy = ResearchUIPolicy.Active;
            var applyPolicy = typeof(ResearchUIPolicy).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
            GameObject root = null, ownerRoot = null, blocker = null;
            try
            {
                root = New("Environment_P0PanelClearanceFixture", scene);
                var view = root.AddComponent<ResearchFlowView>(); view.enabled = false;
                var head = New("SyntheticMonoHead", scene, typeof(Camera)); head.transform.SetParent(root.transform, false);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1.5f;
                var nominalHead = new Vector3(2000, 1.65f, 2000); head.transform.position = nominalHead;
                view.Head = head.transform;
                var panel = Panel("Main", root.transform, scene); view.mainPanel = panel; panel.view = view; panel.panelId = "task_information";
                var peer = Panel("Reader", root.transform, scene); view.referencePanel = peer; peer.view = view; peer.panelId = "reference";
                peer.gameObject.SetActive(false);
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Environment_P0Obstacle";
                SceneManager.MoveGameObjectToScene(blocker, scene); blocker.SetActive(false);
                var collider = blocker.GetComponent<BoxCollider>(); var renderer = blocker.GetComponent<Renderer>();
                var pose = nominalHead + Vector3.forward * 1.2f;
                void Reset()
                {
                    view.controller = null; head.transform.SetPositionAndRotation(nominalHead, Quaternion.identity);
                    panel.transform.SetPositionAndRotation(pose, Quaternion.identity);
                    ((RectTransform)panel.transform).pivot = Vector2.one * .5f;
                    ((RectTransform)panel.transform).sizeDelta = new Vector2(840, 760);
                    peer.gameObject.SetActive(false); blocker.SetActive(false); collider.enabled = true; renderer.enabled = true;
                    Physics.SyncTransforms();
                }
                void Check(string name, Func<bool> test)
                {
                    checks++;
                    try
                    {
                        bool passed = test(); if (!passed) failed++;
                        rows.Add(new { name, passed, rejection = panel.LastRejection,
                            actual = XYZ(panel.transform.position), expected_resting_pose = XYZ(pose) });
                    }
                    catch (Exception error) { failed++; rows.Add(new { name, passed = false, error = error.ToString() }); }
                }
                void Obstacle(Vector3 position, Vector3 size, bool render, bool physical)
                {
                    blocker.transform.position = position; blocker.transform.localScale = size;
                    renderer.enabled = render; collider.enabled = physical; blocker.SetActive(true); Physics.SyncTransforms();
                }
                Reset();
                Obstacle(pose, Vector3.one * .08f, false, true);
                if (!Physics.OverlapBox(pose, Vector3.one * .05f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Contains(collider))
                    throw new InvalidOperationException("Fixture collider is absent from the default Physics query. Do not use this run as a baseline.");
                Reset();
                Check("clear_panel_is_available", () => panel.PlacementClear(pose, Quaternion.identity));
                var cachedEnvironment = (Renderer[])typeof(ResearchPanelMotion).GetField("environment", Hidden).GetValue(panel);
                if (cachedEnvironment == null || !cachedEnvironment.Contains(renderer))
                    throw new InvalidOperationException("Fixture renderer is absent from the production environment cache. Do not use this run as a baseline.");
                Check("full_rect_between_previous_sample_rays", () =>
                {
                    Reset(); Obstacle(nominalHead + new Vector3(.19f, .07f, .6f), new Vector3(.012f, .012f, .02f), true, false);
                    return !panel.PlacementClear(pose, Quaternion.identity);
                });
                Check("collider_only_foreground_obstruction", () =>
                {
                    Reset(); Obstacle(nominalHead + Vector3.forward * .6f, Vector3.one * .08f, false, true);
                    return !panel.PlacementClear(pose, Quaternion.identity);
                });
                Check("synthetic_left_eye_only_obstruction", () =>
                {
                    Reset(); head.transform.position = nominalHead - Vector3.right * .032f;
                    Obstacle(nominalHead + new Vector3(-.221f, .07f, .6f), new Vector3(.004f, .012f, .01f), false, true);
                    return !panel.PlacementClear(pose, Quaternion.identity);
                });
                Check("synthetic_right_eye_clear_for_left_edge_obstacle", () =>
                {
                    Reset(); head.transform.position = nominalHead + Vector3.right * .032f;
                    Obstacle(nominalHead + new Vector3(-.221f, .07f, .6f), new Vector3(.004f, .012f, .01f), false, true);
                    return panel.PlacementClear(pose, Quaternion.identity);
                });
                Check("button_extent_outside_root_rect_has_physical_clearance", () =>
                {
                    Reset(); ((RectTransform)panel.transform).sizeDelta = new Vector2(400, 200);
                    var control = New("EdgeButton", scene, typeof(RectTransform), typeof(Image), typeof(Button));
                    control.transform.SetParent(panel.transform, false); var rect = (RectTransform)control.transform;
                    rect.anchoredPosition = new Vector2(300, 0); rect.sizeDelta = new Vector2(80, 80);
                    try
                    {
                        Obstacle(pose + Vector3.right * .30f, Vector3.one * .02f, false, true);
                        return !panel.PlacementClear(pose, Quaternion.identity);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(control); }
                });
                Check("noncentral_rect_pivot_has_physical_clearance", () =>
                {
                    Reset(); var rect = (RectTransform)panel.transform; rect.pivot = Vector2.zero;
                    Obstacle(pose + new Vector3(.70f, .60f, 0), Vector3.one * .04f, false, true);
                    return !panel.PlacementClear(pose, Quaternion.identity);
                });
                Check("reader_cannot_cover_main_actions_at_separate_depth", () =>
                {
                    Reset(); peer.gameObject.SetActive(true);
                    peer.transform.SetPositionAndRotation(nominalHead + Vector3.forward * .6f, Quaternion.identity);
                    ((RectTransform)peer.transform).sizeDelta = new Vector2(300, 150);
                    return !panel.PlacementClear(pose, Quaternion.identity);
                });
                var handle = panel.gameObject.AddComponent<ResearchDragHandle>(); handle.panel = panel;
                var pointer = new PointerEventData(null) { pointerId = 731 };
                var begin = typeof(ResearchDragHandle).GetMethod("Begin", Hidden);
                var move = typeof(ResearchDragHandle).GetMethod("MoveAt", Hidden);
                using (ResearchUIPolicy.LegacyRegressionScope())
                {
                    Check("held_drag_rejects_blocked_pose_before_render", () =>
                    {
                        Reset(); begin.Invoke(handle, new object[] { pointer, pose });
                        var target = pose + Vector3.right * .25f;
                        Obstacle(target, Vector3.one * .12f, false, true);
                        move.Invoke(handle, new object[] { target, 1f / 72 });
                        bool safe = Vector3.Distance(panel.transform.position, pose) < .001f && handle.RejectedSamples == 1;
                        blocker.SetActive(false); Physics.SyncTransforms(); handle.OnEndDrag(pointer);
                        return safe;
                    });
                    Check("unobstructed_drag_still_moves", () =>
                    {
                        Reset(); begin.Invoke(handle, new object[] { pointer, pose });
                        move.Invoke(handle, new object[] { pose + Vector3.right * .05f, 1f / 72 });
                        bool moved = Vector3.Distance(panel.transform.position, pose) > .005f && handle.RejectedSamples == 0;
                        handle.OnEndDrag(pointer); return moved;
                    });
                }
                Check("fixed_anchor_new_obstruction_never_bypasses_clearance", () =>
                {
                    Reset(); ownerRoot = New("InactiveP0Owner", scene); ownerRoot.SetActive(false);
                    var owner = ownerRoot.AddComponent<ResearchSessionFlowController>(); owner.view = view; view.controller = owner;
                    var station = New("Station", scene).transform; station.SetParent(ownerRoot.transform, false);
                    station.position = new Vector3(nominalHead.x, 0, nominalHead.z); owner.stations = new[] { station, station, station };
                    Set(owner, "CurrentStationIndex", 1); Set(owner, "StationEyeHeight", 1.65d);
                    Set(owner, "Record", new ParticipantRecord { participant_id = "DEV_P0_GEOMETRY", test_data = true,
                        group = ParticipantGroup.Dev, language = ParticipantLanguage.Thai, task_order = TaskOrder.AB,
                        input_mode = ExperimentInputMode.Controller, task_a_layout = "P", session_state = RegistryState.InProgress });
                    Set(owner, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    applyPolicy.Invoke(null, new object[] { ResearchUIPolicy.FixedDemo });
                    panel.centered = false; panel.Recenter(false, false);
                    if (panel.PlacementBlocked) throw new InvalidOperationException("Clear station-anchor fixture did not place before obstruction.");
                    Obstacle((nominalHead + panel.transform.position) * .5f, Vector3.one * .10f, true, false);
                    if (panel.PlacementClear(panel.transform.position, panel.transform.rotation)) throw new InvalidOperationException("The fixture did not create a real rejected sightline.");
                    typeof(ResearchPanelMotion).GetField("nextBlockedRetry", Hidden).SetValue(panel, Time.unscaledTime + 10);
                    typeof(ResearchPanelMotion).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
                    return panel.PlacementBlocked && !panel.GetComponent<Canvas>().enabled && !panel.GetComponent<GraphicRaycaster>().enabled;
                });
            }
            finally
            {
                applyPolicy.Invoke(null, new object[] { policy });
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (ownerRoot) UnityEngine.Object.DestroyImmediate(ownerRoot);
                if (blocker) UnityEngine.Object.DestroyImmediate(blocker);
                if (caller.IsValid()) SceneManager.SetActiveScene(caller);
                EditorSceneManager.CloseScene(scene, true); Physics.SyncTransforms();
            }
            string status = failed == 0 ? "P0_PANEL_CLEARANCE_PASS" : "P0_PANEL_CLEARANCE_FAIL";
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new
            {
                status, checks, failed, rows, hardware_verified = false,
                scope = "Owned preview-scene public PlacementClear and production drag pose pipeline. Synthetic mono geometry at separate -/+32 mm eye origins. No native XR stereo branch, native ray acquisition, actual render, lens acceptance, or participant session."
            }, Formatting.Indented));
            return status + " checks=" + checks + "; failed=" + failed + "; " + output;
        }
        static GameObject New(string name, Scene scene, params Type[] components)
        { var go = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, scene); return go; }
        static ResearchPanelMotion Panel(string name, Transform parent, Scene scene)
        {
            var go = New(name, scene, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(ResearchPanelMotion));
            go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * .001f;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var result = go.GetComponent<ResearchPanelMotion>(); result.enabled = false; return result;
        }
        static void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
        static float[] XYZ(Vector3 value) => new[] { value.x, value.y, value.z };
    }
}
#endif
