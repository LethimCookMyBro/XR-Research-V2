#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Oculus.Interaction;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchP0ActualRenderChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Scope = "VIEW_FIXTURE_ONLY: seeded state and permission text, authored fonts, actual production view and station/environment rendering. External technical TEST logger records placement only. No command dispatch, real participant session, mechanics, workflow approval, native pointer or wearer acceptance.";
        sealed class Cell
        {
            internal ParticipantLanguage language;
            internal string task, kind;
            internal bool technical, details;
            internal ResearchFlowState state;
            internal Cell(ParticipantLanguage lang, string id, string mode, bool dev = true, bool expanded = false, ResearchFlowState flow = ResearchFlowState.Task1Active)
            { language = lang; task = id; kind = mode; technical = dev; details = expanded; state = flow; }
            internal string Label => language + "_" + task + "_" + kind + (technical ? "_DEV" : "_STUDY_TEXT") + (details ? "_details" : "");
        }
        sealed class Fixture
        {
            internal string directory, error, policy;
            internal float eye;
            internal string layout;
            internal Scene caller, owned;
            internal string[] scenes, environment;
            internal double deadline;
            internal readonly Dictionary<Camera, bool> cameras = new Dictionary<Camera, bool>();
            internal readonly Dictionary<Canvas, bool> canvases = new Dictionary<Canvas, bool>();
            internal readonly List<object> observations = new List<object>();
            internal readonly List<string> paths = new List<string>();
            internal ResearchFlowView donor, view;
            internal HandsLabLogger logger;
            internal EventSystem eventSystem;
            internal int dragThreshold;
            internal readonly Dictionary<RayInteractorCursorVisual, Color> cursorColors = new Dictionary<RayInteractorCursorVisual, Color>();
            internal readonly List<string> events = new List<string>();
            internal ResearchSessionFlowController controller;
            internal GameObject root, head;
            internal Camera camera;
            internal IEnumerator work;
            internal int renderedFrame = -1, renderCount;
            internal bool capturing;
        }
        static Fixture current;
        public static string Status { get; private set; } = "IDLE";
        public static string Result => JsonConvert.SerializeObject(new { status = Status, scope = Scope, output = current?.directory, error = current?.error,
            observations = current?.observations, captures = current?.paths, hardware_verified = false, workflow_verified = false, study_approved = false }, Formatting.Indented);

        public static string Begin(string evidenceDirectory, string suite = "core")
        {
            Require(Status != "RUNNING" && Status != "CLEANING", "A render fixture still owns resources."); Ready();
            Require(ResearchFloorPolicy.EditorFixtureAllowed, "Native XR must not be running.");
            Require(suite == "core" || suite == "seated" || suite == "seated-P" || suite == "standing-Q", "Choose a standing/seated P/Q suite.");
            Require(UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsSortMode.None).All(c => c.Record == null && c.Flow == null), "An existing controller has a session or flow.");
            var donor = UnityEngine.Object.FindObjectsByType<ResearchFlowView>(FindObjectsSortMode.None).Single(v => v.controller && v.thaiFont && v.japaneseFont && v.englishFont);
            Require(donor.controller.stations.Length >= 3 && donor.controller.stations[1] && donor.controller.stations[2] && donor.Head && donor.Head.GetComponent<Camera>(), "Authored stations and camera are required.");
            Require(donor.gameObject.scene.path == "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity" , "Canonical scene is required.");
            Require(donor.controller.FloorPolicy && donor.controller.FloorPolicy.Ready, "The donor floor policy must already be ready; the fixture will not repair tracking.");
            string layout = suite == "seated" || suite == "standing-Q" ? "Q" : "P";
            var mechanics = donor.controller.GetComponentsInChildren<ResearchS1TaskAMechanics>(true).Single();
            Require(mechanics.layout == layout, "Authored physical Task A layout must match the requested view suite.");
            string path = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
            Require(Path.IsPathRooted(evidenceDirectory) && !path.StartsWith("\\\\", StringComparison.Ordinal) && !Directory.Exists(path) && !File.Exists(path)
                && !path.Equals(project, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Use a fresh absolute local external directory.");
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)); parent != null; parent = parent.Parent)
                Require(parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) == 0, "Output parent must exist without reparse points.");
            Directory.CreateDirectory(path);
            current = new Fixture { directory = path, donor = donor, eye = suite.StartsWith("seated", StringComparison.Ordinal) ? 1f : 1.65f, layout = layout,
                caller = SceneManager.GetActiveScene(), scenes = SceneStates(), policy = ResearchUIPolicy.Active, environment = EnvironmentStates(), deadline = EditorApplication.timeSinceStartup + 150 };
            current.eventSystem = EventSystem.current; current.dragThreshold = current.eventSystem ? current.eventSystem.pixelDragThreshold : 0;
            foreach (var cursor in UnityEngine.Object.FindObjectsByType<RayInteractorCursorVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None)) current.cursorColors.Add(cursor, cursor.HoverColor);
            Status = "RUNNING"; EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Reload; EditorApplication.playModeStateChanged += PlayChanged;
            RenderPipelineManager.endCameraRendering += Rendered;
            try
            {
                foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.cameraType == CameraType.Game && c.targetDisplay == 0 && !c.targetTexture))
                { current.cameras.Add(camera, camera.enabled); camera.enabled = false; }
                foreach (var canvas in (donor.ParticipantCanvases ?? Array.Empty<Canvas>()).Where(c => c).Distinct())
                { current.canvases.Add(canvas, canvas.enabled); canvas.enabled = false; }
                current.owned = SceneManager.CreateScene("Owned_P0ViewRender_" + Guid.NewGuid().ToString("N"));
                current.work = Sequence(current);
            }
            catch (Exception e) { Finish(e); }
            return Result;
        }
        static IEnumerable<Cell> Cells()
        {
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            {
                foreach (string task in new[] { "A", "B" })
                { yield return new Cell(language, task, "active"); yield return new Cell(language, task, "active", expanded: true); }
                foreach (bool technical in new[] { true, false })
                {
                    yield return new Cell(language, "A", "pause", technical);
                    yield return new Cell(language, "A", "checkpoint1", technical, flow: ResearchFlowState.PostTask1Checkpoint);
                    yield return new Cell(language, "B", "checkpoint2", technical, flow: ResearchFlowState.PostTask2Checkpoint);
                }
                foreach (string kind in new[] { "checking", "fail", "pass", "error" }) yield return new Cell(language, "B", kind);
            }
        }
        static IEnumerator Sequence(Fixture f)
        {
            foreach (var cell in Cells())
            {
                BuildCell(f, cell); double renderStarted = EditorApplication.timeSinceStartup, until = renderStarted + 8; int initial = f.renderCount;
                while (f.renderCount < initial + 3 || f.renderedFrame < Time.frameCount - 1)
                { Require(EditorApplication.timeSinceStartup < until, "No fresh actual camera render for " + cell.Label); yield return null; }
                if (cell.kind == "pass" || cell.kind == "fail")
                { double dwell = EditorApplication.timeSinceStartup + 1.1; while (EditorApplication.timeSinceStartup < dwell) yield return null; }
                f.observations.Add(new { check = "fresh_render_phase", cell_name = cell.Label, seconds = EditorApplication.timeSinceStartup - renderStarted });
                until = EditorApplication.timeSinceStartup + 8;
                f.capturing = true; f.view.StartCoroutine(Capture(f, cell));
                while (f.capturing) { Require(EditorApplication.timeSinceStartup < until, "Capture did not finish for " + cell.Label); yield return null; }
                string path = f.paths.Last();
                while (!File.Exists(path) || new FileInfo(path).Length < 100)
                { Require(EditorApplication.timeSinceStartup < until, "PNG writer did not finish for " + cell.Label); yield return null; }
                Require(File.ReadAllBytes(path).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Capture is not a PNG.");
                DestroyCell(f); yield return null;
            }
        }
        static void BuildCell(Fixture f, Cell cell)
        {
            SceneManager.SetActiveScene(f.owned);
            try
            {
                f.root = new GameObject("P0_VIEW_FIXTURE_ONLY_Controller"); f.root.SetActive(false);
                f.controller = f.root.AddComponent<ResearchSessionFlowController>(); var c = f.controller;
                c.stations = f.donor.controller.stations; c.taskPorts = f.donor.controller.taskPorts;
                c.registryRoot = Path.Combine(f.directory, cell.Label + "_TEST_logger"); Set(c, "Registry", new ResearchParticipantRegistry(c.registryRoot));
                Set(c, "FloorPolicy", f.donor.controller.FloorPolicy);
                Set(c, "Record", new ParticipantRecord { participant_id = "VIEW_FIXTURE_ONLY", session_id = Guid.NewGuid().ToString("N"), run_id = Guid.NewGuid().ToString("N"),
                    group = cell.technical ? ParticipantGroup.Test : cell.language == ParticipantLanguage.Thai ? ParticipantGroup.Thai : ParticipantGroup.Japanese,
                    test_data = cell.technical, language = cell.language, task_order = cell.state == ResearchFlowState.PostTask2Checkpoint ? TaskOrder.AB : cell.task == "A" ? TaskOrder.AB : TaskOrder.BA, task_a_layout = f.layout, session_state = RegistryState.InProgress });
                f.logger = new HandsLabLogger(Path.Combine(c.registryRoot, "Logs"), "SimulatedController", "VIEW_FIXTURE_ONLY P0 UI state", executionEnvironment: "Editor",
                    hardwareVerified: false, phase: "ResearchSession", taskId: cell.task, contentVersion: "research.flow.1", technicalTest: true, testData: true, runId: c.Record.run_id);
                Set(c, "Logger", f.logger); f.events.Add(f.logger.EventsPath);
                TaskOrder order = c.Record.task_order;
                bool? passed = cell.kind == "pass" ? true : cell.kind == "fail" ? false : (bool?)null;
                Set(c, "Flow", new ResearchSessionFlow(order, cell.state, 5, cell.kind == "pause", passed));
                Set(c, "CurrentStationIndex", cell.task == "A" ? 1 : 2); Set(c, "StationEyeHeight", (double)f.eye);
                Set(c, "TechnicalSelfTest", cell.technical);
                if (cell.kind == "pause") Set(c, "PauseKind", "participant");
                if (cell.kind == "error") Set(c, "Error", "VIEW_FIXTURE_ONLY: panel recovery / rendering error state");
                var clock = (ResearchTaskTiming)Field(c, "taskTiming"); clock.Start(0);
                if (cell.kind == "checking") clock.RequestInspection(0, false);
                if (passed.HasValue) Set(clock, "Stage", ResearchInspectionStage.Visible);
                f.view = ResearchFlowView.Build(f.root.transform, f.donor.thaiFont, f.donor.japaneseFont, f.donor.englishFont); f.view.controller = c; c.view = f.view;
                f.head = new GameObject("P0_VIEW_FIXTURE_ONLY_Head"); f.head.SetActive(false); f.camera = f.head.AddComponent<Camera>();
                f.camera.CopyFrom(f.donor.Head.GetComponent<Camera>()); f.camera.targetDisplay = 0; f.camera.targetTexture = null; f.camera.enabled = true;
                var station = c.stations[c.CurrentStationIndex]; f.head.transform.SetPositionAndRotation(station.position + Vector3.up * f.eye, station.rotation);
                f.view.Head = f.head.transform; f.view.transform.SetParent(null, false); SceneManager.MoveGameObjectToScene(f.view.gameObject, f.caller); f.view.gameObject.SetActive(true); f.head.SetActive(true);
                ApplyPolicy(cell.technical ? ResearchUIPolicy.DevMovable : ResearchUIPolicy.FixedDemo);
                f.view.Refresh();
                if (cell.details) { SetField(f.view, "taskDetailsOpen", true); f.view.Refresh(); }
                Canvas.ForceUpdateCanvases(); foreach (var text in f.view.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate(true, true);
                f.view.Refresh(); Canvas.ForceUpdateCanvases(); f.view.mainPanel.Recenter(false, false); Physics.SyncTransforms();
                Require(c.Logger == f.logger && f.logger.State == HandsLabLogState.Active && !f.root.activeInHierarchy && string.IsNullOrEmpty(c.InspectionId), "The view fixture acquired inspection or active-controller ownership.");
                Require(f.camera.cullingMask == f.donor.Head.GetComponent<Camera>().cullingMask, "The capture culling mask differs from the authored camera.");
            }
            finally { SceneManager.SetActiveScene(f.caller); }
        }
        static IEnumerator Capture(Fixture f, Cell cell)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                Ready(); Require(f.renderedFrame == Time.frameCount, "No SRP receipt for this Game View frame.");
                string path = Path.Combine(f.directory, cell.Label + "_eye" + f.eye.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "_" + f.layout + ".png");
                Require(!File.Exists(path), "Capture overwrite rejected.");
                Canvas.ForceUpdateCanvases(); var measure = Measure(f, cell, path); ScreenCapture.CaptureScreenshot(path); f.paths.Add(path); f.observations.Add(measure);
                File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonConvert.SerializeObject(measure, Formatting.Indented));
            }
            catch (Exception e) { AddError(e); }
            finally { f.capturing = false; }
        }
        static object Measure(Fixture f, Cell cell, string path)
        {
            var texts = f.view.mainPanel.GetComponentsInChildren<TMP_Text>(false).Where(t => !string.IsNullOrWhiteSpace(t.text)).Select(t =>
            {
                t.ForceMeshUpdate(true, true); var glyphs = t.textInfo.characterInfo.Take(t.textInfo.characterCount).Where(g => g.isVisible).ToArray();
                return new { name = Hierarchy(t.transform), t.text, font = t.font ? t.font.name : null, t.fontSize, t.enableAutoSizing, t.lineSpacing, t.paragraphSpacing,
                    t.isTextOverflowing, t.preferredHeight, rect = RectData(t.rectTransform.rect), corners = Corners(t.rectTransform), glyph_count = glyphs.Length,
                    glyphs_inside_own_rect = glyphs.All(g => new[] { g.bottomLeft, g.topLeft, g.topRight, g.bottomRight }.All(p => Inside(t.rectTransform.rect, p))),
                    minimum_glyph_point_size = glyphs.Length == 0 ? 0 : glyphs.Min(g => g.pointSize), t.textInfo.lineCount };
            }).ToArray();
            var panels = new[] { f.view.mainPanel, f.view.referencePanel, f.view.RecoveryCanvas ? f.view.RecoveryCanvas.GetComponent<ResearchPanelMotion>() : null }.Where(p => p).Select(p =>
            {
                bool clear = p.PlacementClear(p.transform.position, p.transform.rotation); string rejected = p.LastRejection;
                var rect = (RectTransform)p.transform;
                return new { p.panelId, active = p.gameObject.activeInHierarchy, p.PlacementBlocked, p.RecenterPending, p.IsReadableFromHead, clear, rejected,
                    position = V(p.transform.position), rotation = Q(p.transform.rotation), scale = V(p.transform.lossyScale), rect = RectData(rect.rect), corners = Corners(rect),
                    canvas_enabled = p.GetComponent<Canvas>().enabled, viewport_corners = WorldCorners(rect).Select(v => V(f.camera.WorldToViewportPoint(v))).ToArray() };
            }).ToArray();
            var buttons = f.view.GetComponentsInChildren<ResearchUIAction>(false).Select(a =>
            { var b = a.GetComponent<Button>(); var r = (RectTransform)a.transform; return new { name = Hierarchy(a.transform), a.command,
                interactable = b && b.IsInteractable(), corners = Corners(r), viewport_corners = WorldCorners(r).Select(v => V(f.camera.WorldToViewportPoint(v))).ToArray(),
                rect = RectData(r.rect), active = a.gameObject.activeInHierarchy, graphic_raycast = b && b.targetGraphic && b.targetGraphic.raycastTarget }; }).ToArray();
            return new { scope = Scope, cell = cell.Label, cell.kind, cell.task, language = cell.language.ToString(), seeded_study_permission_text = !cell.technical,
                seeded_timing_stage = f.controller.InspectionFeedbackVisible ? "Visible" : f.controller.InspectionChecking ? "Checking" : "None", f.eye, f.layout,
                frame = Time.frameCount, srp_rendered_frame = f.renderedFrame, camera_id = f.camera.GetInstanceID(), camera_position = V(f.camera.transform.position),
                camera_rotation = Q(f.camera.transform.rotation), f.camera.fieldOfView, f.camera.cullingMask, f.camera.clearFlags, pixels = new[] { f.camera.pixelWidth, f.camera.pixelHeight },
                path, capture_method = "Actual Game View screenshot requested at WaitForEndOfFrame after same-frame owned camera SRP receipt; PNG encoded frame is not independently observed.",
                layout_source = "Seeded panel anchor policy only; physical task geometry is never moved by this fixture.",
                observed_physical_task_a_layout = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(m => m.layout).ToArray(),
                environment_hidden_by_fixture = false, environment_active_renderers = EnvironmentRenderers().Count(r => r.enabled && r.gameObject.activeInHierarchy),
                flow_state = f.controller.Flow.State.ToString(), f.controller.Flow.Paused, f.controller.ParticipantMayResume,
                donor_floor_policy_readout_for_view_only = f.controller.FloorPolicy.Ready, assembly_mvid = typeof(ResearchFlowView).Assembly.ManifestModule.ModuleVersionId.ToString(),
                panels, texts, buttons, optical_readability = "NOT_TESTED", native_controller = "NOT_TESTED", workflow_transition = "NOT_TESTED" };
        }
        static void Tick()
        {
            try
            {
                if (Status == "RUNNING")
                {
                    Ready(); Require(EditorApplication.timeSinceStartup < current.deadline && current.error == null, "Fixture timeout or capture error.");
                    Require(current.cameras.All(p => p.Key && !p.Key.enabled), "An original camera became enabled.");
                    foreach (var canvas in current.canvases.Keys) if (canvas) canvas.enabled = false;
                    Require(EnvironmentStates().SequenceEqual(current.environment), "Authored environment renderer activation changed during acquisition.");
                }
                if (current.work.MoveNext()) return;
                if (Status == "RUNNING") { Finish(null); return; }
                Status = current.error == null ? "CAPTURED_VIEW_FIXTURE_PENDING_VISUAL_REVIEW" : "FAIL"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result);
            }
            catch (Exception e)
            {
                if (Status == "CLEANING") { AddError(e); Status = "FAIL_CLEANUP"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result); }
                else Finish(e);
            }
        }
        static void Finish(Exception error) { if (error != null) AddError(error); Status = "CLEANING"; current.work = Cleanup(current); }
        static IEnumerator Cleanup(Fixture f)
        {
            Try(() => DestroyCell(f)); Try(() => ApplyPolicy(f.policy));
            Try(() => { if (f.eventSystem) f.eventSystem.pixelDragThreshold = f.dragThreshold; });
            foreach (var pair in f.cursorColors) Try(() => { if (pair.Key) pair.Key.HoverColor = pair.Value; });
            foreach (var pair in f.cameras) Try(() => { if (pair.Key) pair.Key.enabled = pair.Value; });
            foreach (var pair in f.canvases) Try(() => { if (pair.Key) pair.Key.enabled = pair.Value; });
            AsyncOperation unloading = null; Try(() => { if (f.owned.IsValid() && f.owned.isLoaded) unloading = SceneManager.UnloadSceneAsync(f.owned); });
            double until = EditorApplication.timeSinceStartup + 8;
            while (unloading != null && !unloading.isDone) { Require(EditorApplication.timeSinceStartup < until, "Owned scene unload timed out."); yield return null; }
            Require(SceneManager.GetActiveScene() == f.caller && SceneStates().SequenceEqual(f.scenes) && EnvironmentStates().SequenceEqual(f.environment)
                && f.cameras.All(p => p.Key && p.Key.enabled == p.Value) && f.canvases.All(p => p.Key && p.Key.enabled == p.Value) && ResearchUIPolicy.Active == f.policy,
                "Original scenes, cameras, participant canvases, environment or UI policy were not restored.");
            Require(f.events.All(path => File.ReadLines(path).All(line => !new[] { "INSPECT_FEEDBACK_VISIBLE", "TASK_COMPLETED", "TASK_ENDED", "CHECKPOINT_COMPLETED" }.Contains((string)JObject.Parse(line)["event"]))), "A view-only fixture generated completion events.");
            f.observations.Add(new { check = "cleanup_observed", pass = true, original_scene_states = SceneStates(), environment_unchanged = true, original_cameras_restored = true, participant_canvases_restored = true, policy_restored = true });
        }
        static void DestroyCell(Fixture f)
        {
            if (f.logger != null) { f.logger.Dispose(); f.logger = null; if (f.controller) Set(f.controller, "Logger", null); }
            if (f.view) { f.view.StopAllCoroutines(); UnityEngine.Object.DestroyImmediate(f.view.gameObject); }
            if (f.head) UnityEngine.Object.DestroyImmediate(f.head); if (f.root) UnityEngine.Object.DestroyImmediate(f.root);
            f.view = null; f.controller = null; f.camera = null; f.head = f.root = null; f.renderedFrame = -1;
        }
        static void Rendered(ScriptableRenderContext context, Camera camera) { if (current != null && camera == current.camera) { current.renderedFrame = Time.frameCount; current.renderCount++; } }
        static Renderer[] EnvironmentRenderers() => UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.gameObject.scene.path == "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity" && !r.GetComponentInParent<ResearchFlowView>() && !r.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>()).OrderBy(r => r.GetInstanceID()).ToArray();
        static string[] EnvironmentStates() => EnvironmentRenderers().Select(r => r.GetInstanceID() + ":" + r.enabled + ":" + r.gameObject.activeSelf + ":" + r.gameObject.activeInHierarchy).ToArray();
        static string[] SceneStates() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static void Ready() => Require(Application.isPlaying && !Application.isBatchMode && !EditorApplication.isPaused && Application.isFocused && EditorWindow.focusedWindow
            && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView" && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Focused unpaused rendered Game View is required; no focus repair.");
        static Vector3[] WorldCorners(RectTransform r) { var corners = new Vector3[4]; r.GetWorldCorners(corners); return corners; }
        static float[][] Corners(RectTransform r) => WorldCorners(r).Select(V).ToArray();
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        static object RectData(Rect r) => new { r.x, r.y, r.width, r.height };
        static bool Inside(Rect r, Vector3 p) => p.x >= r.xMin - 1 && p.x <= r.xMax + 1 && p.y >= r.yMin - 1 && p.y <= r.yMax + 1;
        static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
        static object Field(object o, string name) => o.GetType().GetField(name, Hidden).GetValue(o);
        static void SetField(object o, string name, object value) => o.GetType().GetField(name, Hidden).SetValue(o, value);
        static void Set(object o, string name, object value) => o.GetType().GetProperty(name).GetSetMethod(true).Invoke(o, new[] { value });
        static void ApplyPolicy(string policy) => typeof(ResearchUIPolicy).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { policy });
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Try(Action action) { try { action(); } catch (Exception e) { AddError(e); } }
        static void AddError(Exception e) { current.error = (current.error == null ? "" : current.error + "\n") + e; }
        static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode && Status == "RUNNING") Finish(new InvalidOperationException("Play Mode exited during acquisition.")); }
        static void Reload() { if (current == null) return; Finish(new InvalidOperationException("Assembly reload interrupted acquisition.")); Try(() => current.work.MoveNext()); Status = "ABORTED_RELOAD_CLEANUP_PENDING"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result); }
        static void Detach() { EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Reload; EditorApplication.playModeStateChanged -= PlayChanged; RenderPipelineManager.endCameraRendering -= Rendered; }
    }
}
#endif
