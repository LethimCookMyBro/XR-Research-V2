#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchInspectionRenderChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        sealed class Fixture
        {
            internal string directory, error, eventsPath, session, run;
            internal double deadline;
            internal Scene caller, owned;
            internal string[] scenes;
            internal readonly Dictionary<Camera, bool> cameras = new Dictionary<Camera, bool>();
            internal readonly List<object> observations = new List<object>(), captures = new List<object>();
            internal readonly List<string> capturePaths = new List<string>();
            internal GameObject root, head;
            internal ResearchSessionFlowController controller;
            internal ResearchFlowView view;
            internal Camera camera;
            internal HandsLabLogger logger;
            internal ResearchTaskTiming clock;
            internal IEnumerator work;
            internal int evaluations, inspectionCaptures;
            internal bool inspecting;
        }
        static Fixture current;
        public static string Status { get; private set; } = "IDLE";
        public static string Result => JsonConvert.SerializeObject(new { status = Status, output = current?.directory, error = current?.error,
            observations = current?.observations, captures = current?.captures, raw_events = current?.eventsPath,
            scope = "Synthetic TEST DTO and inactive-controller component fixture; real Stopwatch clock, production view/SRP/coroutine and actual Game View captures. No Training, physical mechanics, SDK pointing, native or wearer acceptance.",
            hardware_verified = false, physical_mechanics_verified = false, study_approved = false }, Formatting.Indented);

        public static string Begin(string evidenceDirectory)
        {
            Require(Status != "RUNNING" && Status != "CLEANING", "A fixture still owns resources.");
            RuntimeReady();
            Require(ResearchFloorPolicy.EditorFixtureAllowed, "Editor fixture requires no running native XR subsystem.");
            Require(UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsSortMode.None).All(c => c.Record == null), "An existing session is open.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
            string path = Path.GetFullPath(evidenceDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
            Require(Path.IsPathRooted(evidenceDirectory) && !path.StartsWith("\\\\", StringComparison.Ordinal) && !Directory.Exists(path) && !File.Exists(path)
                && !path.Equals(project, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Use a fresh absolute local external directory.");
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)); parent != null; parent = parent.Parent)
                Require(parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) == 0, "Output parent must exist without reparse points.");
            Directory.CreateDirectory(path);
            current = new Fixture { directory = path, caller = SceneManager.GetActiveScene(), scenes = SceneStates(), deadline = EditorApplication.timeSinceStartup + 35 };
            Status = "RUNNING"; EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += AbortedReload; EditorApplication.playModeStateChanged += PlayChanged;
            try { Setup(current, donor); current.work = Sequence(current); }
            catch (Exception error) { Finish(error); }
            return Result;
        }
        static void Setup(Fixture f, ResearchFlowView donor)
        {
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.cameraType == CameraType.Game && c.targetDisplay == 0 && !c.targetTexture))
            { f.cameras.Add(c, c.enabled); c.enabled = false; }
            f.owned = SceneManager.CreateScene("Owned_InspectionRender_" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(f.owned);
            try
            {
                f.root = new GameObject("InspectionRender_TEST_Controller"); f.root.SetActive(false);
                var c = f.controller = f.root.AddComponent<ResearchSessionFlowController>(); c.registryRoot = Path.Combine(f.directory, "registry_TEST");
                var registry = new ResearchParticipantRegistry(c.registryRoot);
                var record = registry.StartOrResume(registry.CreateNext(ParticipantGroup.Test).participant_id, ParticipantLanguage.Thai, ExperimentInputMode.Controller, TaskOrder.BA);
                var flow = new ResearchSessionFlow(TaskOrder.BA, ResearchFlowState.Task1Ready, 5);
                Set(c, "Registry", registry); Set(c, "Flow", flow); Set(c, "Record", registry.SaveProgress(record, flow));
                f.logger = new HandsLabLogger(Path.Combine(c.registryRoot, "Logs"), "SimulatedController", "Rendered Inspect component fixture", executionEnvironment: "Editor",
                    hardwareVerified: false, phase: "ResearchSession", taskId: "B", contentVersion: "research.flow.1", technicalTest: true, testData: true, runId: record.run_id);
                f.session = record.session_id; f.run = record.run_id;
                Set(c, "Logger", f.logger); f.eventsPath = f.logger.EventsPath; f.clock = (ResearchTaskTiming)Field(c, "taskTiming");
                var port = f.root.AddComponent<ResearchTaskPort>(); port.taskId = "B"; port.demoMode = true; port.contentApproved = false;
                port.BindSnapshot(() => { if (f.inspecting) f.inspectionCaptures++; return new S1TaskBState { systemIntegrityValid = true, capturedAtSeconds = f.logger.Timestamp,
                    baseId = "TEST.base", frontGuardId = "TEST.front", bladeId = "TEST.blade", spinnerId = "TEST.spinner", rearGuardId = "TEST.rear", rearLockId = "TEST.lock",
                    powerIsolated = true, baseStable = true, frontGuardPlacement = S1Placement.Seated, frontGuardAligned = true, frontGuardClipped = true,
                    bladePlacement = S1Placement.Seated, bladeCompatible = true, bladeKeyAligned = true, spinner = S1ThreadState.Tight,
                    rearGuardPlacement = S1Placement.Seated, rearGuardAligned = true, rearLock = S1ThreadState.Tight }; },
                    state => { f.evaluations++; return ResearchS1TaskEvaluator.Evaluate((S1TaskBState)state).Passed; }, allowed => { });
                c.taskPorts = new[] { port };
                f.view = ResearchFlowView.Build(f.root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont); f.view.controller = c; c.view = f.view;
                f.head = new GameObject("InspectionRender_TEST_Head"); f.head.SetActive(false); f.camera = f.head.AddComponent<Camera>();
                f.head.transform.SetPositionAndRotation(new Vector3(1000, 1.65f, 1000), Quaternion.Euler(0, 180, 0));
                f.camera.targetDisplay = 0; f.camera.targetTexture = null; f.camera.fieldOfView = 90;
                f.camera.clearFlags = CameraClearFlags.SolidColor; f.camera.backgroundColor = new Color(.12f, .15f, .18f); f.camera.cullingMask = ~0;
                f.view.Head = f.head.transform; f.view.transform.SetParent(null, false); f.view.gameObject.SetActive(true); f.head.SetActive(true);
                f.view.Refresh(); f.view.mainPanel.Recenter(false, false);
                Require(c.Record.run_id == f.logger.RunId && !port.IsReadyForStudy && port.HasBoundMechanics, "Isolated bound TEST identity failed.");
                Observe(f, "context_ready");
            }
            finally { SceneManager.SetActiveScene(f.caller); }
        }
        static IEnumerator Sequence(Fixture f)
        {
            var c = f.controller; double until = f.logger.Timestamp + 8;
            while (f.view.mainPanel.PlacementBlocked || !f.view.mainPanel.IsReadableFromHead || Time.frameCount < 2)
            { Require(f.logger.Timestamp < until, "Panel did not become readable."); yield return null; }
            Require(c.Command("task_start", "SimulatedController") && c.TaskStartConfirmation && !f.clock.Started, "Start request must not start the clock.");
            Require(!c.Command("task_start", "SimulatedController"), "Duplicate Start request was accepted.");
            Require(c.Command("task_start_confirm", "SimulatedController") && f.clock.Started && c.Flow.IsTaskActive, "Bound task confirmation failed.");
            Require(!c.Command("task_start_confirm", "SimulatedController"), "Duplicate Start confirmation was accepted.");
            var started = Events(f).Single(r => (string)r["event"] == "TASK_STARTED");
            Require(started["after"]?["initial_state"]?.Type == JTokenType.Object, "Initial bound state is missing.");
            Capture(f, "active"); until = f.logger.Timestamp + .4;
            while (f.logger.Timestamp < until) yield return null;
            f.camera.enabled = false; f.inspecting = true;
            bool accepted;
            try { accepted = c.Command("inspect", "SimulatedController"); } finally { f.inspecting = false; }
            Require(accepted && c.InspectionChecking && f.evaluations == 1, "Inspect did not evaluate once.");
            string inspection = c.InspectionId; var requested = Events(f).Single(r => (string)r["event"] == "INSPECT");
            double requestedActive = (double)requested["after"]["active_elapsed_s"];
            Require(!c.Command("inspect", "SimulatedController") && !c.Command("inspect_continue", "SimulatedController"), "Pending Inspect accepted another action.");
            until = f.logger.Timestamp + 8;
            while (f.clock.Stage == ResearchInspectionStage.Checking)
            {
                Require(f.logger.Timestamp < until && !c.InspectionFeedbackVisible && !f.clock.Ended && EndCount(f) == 0, "Checking completed before a render.");
                yield return null;
            }
            Require(f.clock.Stage == ResearchInspectionStage.Ready && c.TaskActiveElapsed - requestedActive >= 3 && c.Flow.InspectPassed == true, "Checking did not last three real active seconds.");
            until = f.logger.Timestamp + .5;
            while (f.logger.Timestamp < until) { Require(!c.InspectionFeedbackVisible && !f.clock.Ended && EndCount(f) == 0, "Disabled camera acknowledged feedback."); yield return null; }
            Observe(f, "camera_disabled_ready_negative"); Capture(f, "camera_disabled_ready");
            until = f.logger.Timestamp + .3; while (f.logger.Timestamp < until) yield return null;
            f.camera.enabled = true; until = f.logger.Timestamp + 8;
            while (!c.InspectionFeedbackVisible) { Require(f.logger.Timestamp < until, "Actual Game camera never produced a receipt."); yield return null; }
            Require(c.InspectionId == inspection && f.clock.EndReason == "passed" && c.Flow.State == ResearchFlowState.Task1Active, "Pass skipped explicit Continue.");
            var rows = Events(f); var visible = rows.Single(r => (string)r["event"] == "INSPECT_FEEDBACK_VISIBLE");
            var completed = rows.Single(r => (string)r["event"] == "TASK_COMPLETED"); var ended = rows.Single(r => (string)r["event"] == "TASK_ENDED");
            var evaluated = rows.Single(r => (string)r["event"] == "INSPECT_PASS"); var snapshot = requested["before"];
            Require(snapshot?.Type == JTokenType.Object && new[] { requested["task_state"], evaluated["before"], evaluated["task_state"], visible["task_state"],
                completed["after"], completed["task_state"], ended["before"], ended["task_state"] }.All(value => JToken.DeepEquals(snapshot, value)), "Inspection events did not preserve one immutable evaluated snapshot.");
            Require((string)visible["action_id"] == inspection && (string)visible["after"]?["visibility_basis"] == "rendered_readable_panel"
                && (double)visible["after"]["active_elapsed_s"] - requestedActive >= 3 && (long)requested["seq"] < (long)visible["seq"]
                && (long)visible["seq"] < (long)completed["seq"] && (long)completed["seq"] < (long)ended["seq"], "Rendered event chronology failed.");
            Observe(f, "genuine_render_visible"); Capture(f, "pass_visible"); int frame = Time.frameCount; until = f.logger.Timestamp + .5;
            while (Time.frameCount < frame + 5 || f.logger.Timestamp < until)
            { Require(EndCount(f) == 3 && c.Flow.State == ResearchFlowState.Task1Active, "Completion duplicated or checkpoint advanced automatically."); yield return null; }
            Require(c.Command("inspect_continue", "SimulatedController") && c.Flow.State == ResearchFlowState.PostTask1Checkpoint, "Explicit Continue did not enter checkpoint.");
            Require(!c.Command("inspect_continue", "SimulatedController") && EndCount(f) == 3, "Repeated Continue changed completion.");
            Observe(f, "explicit_continue_checkpoint"); Capture(f, "checkpoint"); until = f.logger.Timestamp + 5;
            while (f.capturePaths.Count < 4 || f.capturePaths.Any(p => !File.Exists(p) || new FileInfo(p).Length < 100))
            { Require(f.logger.Timestamp < until, "Actual screenshot files were not written."); yield return null; }
            foreach (var path in f.capturePaths) Require(File.ReadAllBytes(path).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Capture is not a PNG.");
            Require(f.evaluations == 1 && Events(f).Count(r => (string)r["event"] == "INSPECT") == 1 && c.InspectionId == inspection && string.IsNullOrEmpty(c.Error), "Inspection request/identity/evaluation duplicated.");
        }
        static void Tick()
        {
            try
            {
                if (Status == "RUNNING")
                {
                    RuntimeReady(); Require(EditorApplication.timeSinceStartup < current.deadline && current.error == null, "Fixture timed out or asynchronous capture failed."); Require(current.cameras.All(p => !p.Key || !p.Key.enabled), "An unowned Game camera became enabled.");
                    Require(current.controller && current.logger.State == HandsLabLogState.Active && string.IsNullOrEmpty(current.controller.Error), "Owned controller/logger failed.");
                    Require(current.controller.Record?.session_id == current.session && current.controller.Record.run_id == current.run && current.logger.RunId == current.run, "Owned TEST session/run changed.");
                    Call(current.controller, "AdvanceTaskTiming"); Call(current.controller, "ApplyExports");
                }
                if (current.work.MoveNext()) return;
                if (Status == "RUNNING") { Finish(null); return; }
                Status = current.error == null ? "PASS_SCOPED_RENDERED_INSPECT_COMPONENT" : "FAIL"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result);
            }
            catch (Exception error) { if (Status == "CLEANING") { AddError(error); Status = "FAIL_CLEANUP"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result); } else Finish(error); }
        }
        static void Finish(Exception error) { if (error != null) AddError(error); Status = "CLEANING"; current.work = Cleanup(current); }
        static IEnumerator Cleanup(Fixture f)
        {
            Try(() => { if (f.view) { f.view.StopAllCoroutines(); f.view.enabled = false; } });
            Try(() => { if (f.controller) Require(f.controller.WaitForExports(15000) && string.IsNullOrEmpty(f.controller.ExportDeliveryError), "Export worker did not finish cleanly."); });
            Try(() => { f.logger?.Dispose(); if (f.controller) Set(f.controller, "Logger", null); });
            Try(() => { if (f.view) UnityEngine.Object.DestroyImmediate(f.view.gameObject); });
            Try(() => { if (f.head) UnityEngine.Object.DestroyImmediate(f.head); if (f.root) UnityEngine.Object.DestroyImmediate(f.root); });
            foreach (var pair in f.cameras) Try(() => { if (pair.Key) pair.Key.enabled = pair.Value; });
            AsyncOperation unloading = null;
            Try(() => { if (f.owned.IsValid() && f.owned.isLoaded) unloading = SceneManager.UnloadSceneAsync(f.owned); });
            double until = EditorApplication.timeSinceStartup + 8;
            while (unloading != null && !unloading.isDone) { Require(EditorApplication.timeSinceStartup < until, "Owned scene unload timed out."); yield return null; }
            Require(SceneManager.GetActiveScene() == f.caller && SceneStates().SequenceEqual(f.scenes) && f.cameras.All(p => p.Key && p.Key.enabled == p.Value), "Caller scenes or camera states changed.");
            f.observations.Add(new { check = "cleanup_observed", pass = true, logger_state = f.logger?.State.ToString(), scenes = SceneStates() });
        }
        static void Capture(Fixture f, string label) => f.view.StartCoroutine(CaptureFrame(f, label));
        static IEnumerator CaptureFrame(Fixture f, string label)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                RuntimeReady(); string path = Path.Combine(f.directory, label + ".png"); Require(!File.Exists(path), "Capture overwrite rejected.");
                ScreenCapture.CaptureScreenshot(path); f.capturePaths.Add(path);
                f.captures.Add(new { path, requested_frame = Time.frameCount, logger_timestamp = f.logger.Timestamp, inspection_id = f.controller.InspectionId,
                    flow_state = f.controller.Flow.State.ToString(), camera_enabled = f.camera.enabled, method = "Actual GameView ScreenCapture.CaptureScreenshot requested after WaitForEndOfFrame; encoded frame number not independently observed" });
            }
            catch (Exception error) { AddError(error); }
        }
        static void Observe(Fixture f, string check) => f.observations.Add(new { check, frame = Time.frameCount, logger_timestamp = f.logger.Timestamp,
            active_elapsed = f.controller.TaskActiveElapsed, stage = f.clock.Stage.ToString(), flow_state = f.controller.Flow.State.ToString(),
            feedback_visible = f.controller.InspectionFeedbackVisible, camera_enabled = f.camera.enabled, camera_id = f.camera.GetInstanceID(),
            width = f.camera.pixelWidth, height = f.camera.pixelHeight, readable = f.view.mainPanel.IsReadableFromHead, placement_blocked = f.view.mainPanel.PlacementBlocked,
            captures_during_inspect_dispatch = f.inspectionCaptures, evaluations = f.evaluations, session_id = f.session, run_id = f.run,
            assembly_mvid = typeof(ResearchFlowView).Assembly.ManifestModule.ModuleVersionId.ToString() });
        static JObject[] Events(Fixture f) { using (var stream = new FileStream(f.eventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var reader = new StreamReader(stream)) { var rows = new List<JObject>(); for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line)); return rows.ToArray(); } }
        static int EndCount(Fixture f) => Events(f).Count(r => new[] { "INSPECT_FEEDBACK_VISIBLE", "TASK_COMPLETED", "TASK_ENDED" }.Contains((string)r["event"]));
        static string[] SceneStates() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();
        static void RuntimeReady() => Require(Application.isPlaying && !Application.isBatchMode && !EditorApplication.isPaused && Application.isFocused
            && EditorWindow.focusedWindow && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView" && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Focused unpaused rendered Game View required; no focus repair.");
        static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });
        static void Call(object owner, string name) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, null);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Try(Action action) { try { action(); } catch (Exception error) { AddError(error); } }
        static void AddError(Exception error) { current.error = (current.error == null ? "" : current.error + "\n") + error; }
        static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode && Status == "RUNNING") Finish(new InvalidOperationException("Play Mode exited during acquisition.")); }
        static void AbortedReload() { if (current == null) return; Finish(new InvalidOperationException("Assembly reload interrupted acquisition.")); Try(() => current.work.MoveNext()); Status = "ABORTED_RELOAD_CLEANUP_PENDING"; Detach(); File.WriteAllText(Path.Combine(current.directory, "result.json"), Result); }
        static void Detach() { EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= AbortedReload; EditorApplication.playModeStateChanged -= PlayChanged; }
    }
}
#endif
