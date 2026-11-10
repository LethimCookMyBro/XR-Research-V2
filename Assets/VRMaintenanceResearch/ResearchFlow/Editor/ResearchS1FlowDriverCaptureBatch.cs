#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static partial class ResearchS1FlowDriver
    {
        static string batchRoot, batchExpectedTraining;
        static int batchFactIndex;
        static readonly Dictionary<string, object> batchPriorRegistries = new Dictionary<string, object>();
        static readonly Dictionary<string, string> batchResources = new Dictionary<string, string>();
        static readonly Dictionary<string, string> batchVideos = new Dictionary<string, string>();
        public static bool BatchStrictFocus => batchRoot != null;
        public static bool BatchTrainingOnly { get; private set; }
        public static bool BatchVideoPlaybackCheck { get; private set; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        public static bool BatchFocused
        {
            get
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
                return process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id
                    && EditorWindow.focusedWindow != null
                    && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";
            }
        }

        public static string BatchConfigure(string output, string expectedTraining, bool trainingOnly = false)
        {
            Require(EditorApplication.isPlaying && BatchFocused && !EditorApplication.isPaused, "Batch needs focused unpaused Play Mode.");
            controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            Require(controller && controller.Record == null && !ResearchS1TaskASdkRunner.Running && !ResearchS1TaskBSdkRunner.Running,
                "Configure only idle setup with no session or SDK runner.");
            Require(!string.IsNullOrEmpty(expectedTraining), "Bind an explicit training version.");
            string full = Path.GetFullPath(output), project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
            Require(Path.IsPathRooted(output) && !full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.Equals(project, StringComparison.OrdinalIgnoreCase) && !full.StartsWith("\\\\", StringComparison.Ordinal)
                && !Directory.Exists(full) && !File.Exists(full), "Use a fresh local external batch cell directory.");
            Directory.CreateDirectory(full);
            batchRoot = full; batchExpectedTraining = expectedTraining; batchFactIndex = 0;
            BatchTrainingOnly = trainingOnly;
            BatchVideoPlaybackCheck = false;
            if (expectedTraining == ResearchUIPolicy.TrainingNoticeV8)
            {
                ResearchParticipantRegistry.EditorCaptureTrainingVersion = expectedTraining;
                AssemblyReloadEvents.beforeAssemblyReload -= BatchRestoreFixture;
                AssemblyReloadEvents.beforeAssemblyReload += BatchRestoreFixture;
                EditorApplication.playModeStateChanged -= BatchFixturePlayState;
                EditorApplication.playModeStateChanged += BatchFixturePlayState;
            }
            SetCaptureDir(Path.Combine(full, "screenshots"));
            return BatchSnapshot();
        }

        // Explicit partial DEV regression: all preceding skills use real inputs, only video facts are captured.
        public static string BatchConfigureVideoPlaybackCheck(string output, string expectedTraining)
        {
            string snapshot = BatchConfigure(output, expectedTraining, trainingOnly: true);
            BatchVideoPlaybackCheck = true;
            return snapshot;
        }

        public static string BatchSnapshot()
        {
            var flow = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            return JsonConvert.SerializeObject(new {
                playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling, focused = BatchFocused,
                editor_paused = EditorApplication.isPaused, frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, unity = Application.unityVersion,
                participant = flow?.Record?.participant_id, session = flow?.Record?.session_id, run = flow?.Record?.run_id,
                test_data = flow?.Record?.test_data, training_version = flow?.Record?.training_version,
                capture_fixture = batchPriorRegistries.Count > 0, fixture_final_media_accepted = false,
                input_mode = flow?.Record?.input_mode.ToString(), simulated_input = ResearchFlowVisualInput.Running,
                state = flow?.Flow?.State.ToString(), task = flow?.Flow?.CurrentTask, paused = flow?.Flow?.Paused,
                task_a = EditorApplication.isPlaying && flow?.Record != null ? UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>()?.Capture() : null,
                task_b = EditorApplication.isPlaying && flow?.Record != null ? UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>()?.Capture() : null,
                reader = flow?.view?.ReferenceViewer?.Metadata, error = flow?.Error,
                status = Status, detail = Detail, hardware_verified = false
            });
        }

        public static string BatchCancel()
        {
            ResearchS1GameViewVideo.Stop();
            ResearchS1TaskASdkRunner.Stop(); ResearchS1TaskBSdkRunner.Stop();
            if (ResearchFlowVisualInput.Running)
                for (int side = 0; side < 2; side++)
                    ResearchFlowVisualInput.SetControllerWorldPose(side, ResearchFlowVisualInput.ControllerWorldPose(side));
            Stop("BATCH_INTERRUPTED", "Input released; session preserved; no autofocus or automatic resume.");
            return BatchSnapshot();
        }

        public static string BatchLoadTrainingFixture(string descriptorPath)
        {
            Require(BatchStrictFocus && BatchFocused && controller && controller.Record == null, "Install capture fixture only before an owned DEV session.");
            var descriptor = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(descriptorPath));
            string resource = (string)descriptor["resource"];
            Require(resource == "ResearchReaderS1/T7_th/package" || resource == "ResearchReaderS1/T7_ja/package", "Only neutral T7 capture fixture registries are allowed.");
            var package = JsonUtility.FromJson<ResearchReferenceViewer.Package>(descriptor["package"].ToString());
            Require(package.content_id.StartsWith("s1.capture-fixture.T7.", StringComparison.Ordinal)
                && package.version == "EDITOR_CAPTURE_FIXTURE_T7_NOT_FINAL" && ResearchReferenceViewer.ValidatePackage(package) == null,
                "Explicit neutral capture-fixture identity and valid authored package required.");
            string video = (string)descriptor["video_path"];
            Require(Path.IsPathRooted(video) && File.Exists(video) && ResearchReferenceViewer.HashMatches(File.ReadAllBytes(video), package.video.sha256), "Neutral fixture video hash differs.");
            var registryType = typeof(ResearchReferenceViewer).GetNestedType("Registry", BindingFlags.NonPublic);
            var cache = (IDictionary)typeof(ResearchReferenceViewer).GetField("registries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Require(!batchPriorRegistries.ContainsKey(resource), "Fixture already installed.");
            foreach (var item in ((Newtonsoft.Json.Linq.JObject)descriptor["resources"]).Properties())
            {
                string path = (string)item.Value;
                Require(Path.IsPathRooted(path) && File.Exists(path), "External fixture bytes missing: " + path);
                batchResources.Add(item.Name, path);
            }
            batchPriorRegistries.Add(resource, cache.Contains(resource) ? cache[resource] : null);
            var registry = Activator.CreateInstance(registryType);
            registryType.GetField("package").SetValue(registry, package);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                registryType.GetField("hash").SetValue(registry, BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(descriptorPath))).Replace("-", "").ToLowerInvariant());
            cache[resource] = registry;
            batchVideos[package.video.streaming_path] = video;
            ResearchReferenceViewer.EditorCaptureResource = key => FixtureSessionAllowed && batchResources.TryGetValue(key, out var path) ? File.ReadAllBytes(path) : null;
            ResearchReferenceViewer.EditorCaptureVideoPath = key => FixtureSessionAllowed && batchVideos.TryGetValue(key, out var path) ? path : null;
            ResearchParticipantRegistry.EditorCaptureTrainingVersion = ResearchUIPolicy.TrainingTrainerV7;
            AssemblyReloadEvents.beforeAssemblyReload -= BatchRestoreFixture;
            AssemblyReloadEvents.beforeAssemblyReload += BatchRestoreFixture;
            EditorApplication.playModeStateChanged -= BatchFixturePlayState;
            EditorApplication.playModeStateChanged += BatchFixturePlayState;
            File.Copy(descriptorPath, Path.Combine(batchRoot, Path.GetFileName(descriptorPath)), false);
            return BatchSnapshot();
        }

        static bool FixtureSessionAllowed => Application.isEditor && BatchStrictFocus && controller
            && (controller.Record == null || controller.Record.test_data && controller.Record.group == ParticipantGroup.Dev);
        static void BatchFixturePlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) BatchRestoreFixture(); }
        static void BatchRestoreFixture()
        {
            var cache = (IDictionary)typeof(ResearchReferenceViewer).GetField("registries", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var entry in batchPriorRegistries) { if (entry.Value == null) cache.Remove(entry.Key); else cache[entry.Key] = entry.Value; }
            batchPriorRegistries.Clear(); batchResources.Clear(); batchVideos.Clear();
            ResearchReferenceViewer.EditorCaptureResource = null; ResearchReferenceViewer.EditorCaptureVideoPath = null;
            ResearchParticipantRegistry.EditorCaptureTrainingVersion = null;
            AssemblyReloadEvents.beforeAssemblyReload -= BatchRestoreFixture;
            EditorApplication.playModeStateChanged -= BatchFixturePlayState;
        }

        public static string BatchLookAtWorkpiece()
        {
            Require(BatchStrictFocus && BatchFocused && controller && controller.Flow.IsTaskActive, "Active focused batch task required.");
            var target = UnityEngine.Object.FindObjectsByType<ResearchExamineTarget>(FindObjectsSortMode.None)
                .Single(t => t.stableTarget == controller.Flow.CurrentTask + ".module");
            var anchor = new SerializedObject(target).FindProperty("focusAnchor").objectReferenceValue as Transform;
            Require(anchor, "Workpiece anchor unavailable.");
            ResearchFlowVisualInput.SetHeadFacing(anchor.position);
            return BatchSnapshot();
        }

        // Media stills: one real ray click on an offered control (empty command = look at Main, "-" = keep the current view), then one Game View frame.
        public static string ClickAndCapture(string command, string label) => Start(ClickAndCaptureRoutine(command, label), 30);
        static IEnumerator ClickAndCaptureRoutine(string command, string label)
        {
            if (command == "-") { }
            else if (string.IsNullOrEmpty(command))
            {
                var main = (RectTransform)controller.view.mainPanel.transform;
                ResearchFlowVisualInput.SetHeadFacing(main.TransformPoint(main.rect.center));
            }
            else yield return RayClick(command);
            // Inspect shows its result after the 3 s check; capture while that result is on the panel.
            yield return Settle(); yield return Wait(command == "inspect" ? 3.6 : .6);
            Capture(label); yield return Wait(.3);
        }

        public static string BatchExaminePart(string stableTarget) => Start(BatchExaminePartRoutine(stableTarget), 60);
        static IEnumerator BatchExaminePartRoutine(string stableTarget)
        {
            Require(controller.Flow.IsTaskActive, "Part Examine requires an active task.");
            bool fan = controller.Flow.CurrentTask == "B";
            var target = UnityEngine.Object.FindObjectsByType<ResearchExamineTarget>(FindObjectsSortMode.None)
                .Single(t => t.stableTarget == controller.Flow.CurrentTask + ".module");
            // Task B (pointed Examine): aim at a fan part; the chip names its S1 area.
            var anchor = fan ? UnityEngine.Object.FindObjectsByType<ResearchS1FanPart>(FindObjectsSortMode.None).Single(p => p.stableId == stableTarget).transform
                : UnityEngine.Object.FindObjectsByType<ResearchS1TaskAInput>(FindObjectsSortMode.None).Single(p => p.stableId == stableTarget).transform;
            string expected = fan ? ResearchS1FanObservations.TargetForPart(stableTarget) : stableTarget;
            // S1: the rear guard edge is examined from outside; from the front the front guard hides it, so step to the fan's side.
            Vector3? returnTo = null;
            if (stableTarget == "B.rear_guard")
            {
                returnTo = controller.origin.Camera.transform.position;
                var fanRoot = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>().transform;
                ResearchFlowVisualInput.WalkHeadTo(anchor.position + fanRoot.right * .75f);
                yield return Wait(.5);
            }
            Require(target.Available && anchor, "Actual selected part is unavailable.");
            var colliders = anchor.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
            Require(colliders.Length > 0, "Selected physical part has no visible collider.");
            var aim = colliders.OrderBy(c => Vector3.Distance(c.bounds.center, controller.origin.Camera.transform.position)).First().bounds.center;
            ResearchFlowVisualInput.SetHeadFacing(aim);
            var ray = Ray(1); HandNearBody(1);
            var selected = typeof(ResearchExamineTarget).GetField("selectedPart", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 40 && (!target.ChipVisible || (string)selected.GetValue(target) != expected); i++)
            {
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (aim - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return Wait(.05);
            }
            Require(target.ChipVisible && (string)selected.GetValue(target) == expected, "Selected part chip did not appear after actual ray pointing: " + stableTarget);
            yield return RayPress((RectTransform)target.transform, "selected physical part examine chip");
            if (returnTo.HasValue) { ResearchFlowVisualInput.WalkHeadTo(returnTo.Value); yield return Wait(.5); }
        }

        static IEnumerator BatchTrainingAuxiliary(string pendingFact)
        {
            if (!BatchStrictFocus || BatchVideoPlaybackCheck || (pendingFact != "close" && pendingFact != "unlock")) yield break;
            int completed = controller.TutorialCompletedFactCount;
            if (pendingFact == "close")
            {
                Require(controller.ReferenceOpen && !controller.ReferenceMinimized, "Neutral Reader auxiliary capture requires an expanded Reader.");
                yield return BatchTrainingBefore("minimize");
                yield return RayClick("reference_minimize"); yield return Settle();
                Require(controller.ReferenceMinimized, "Reader minimized through actual ray input.");
                yield return BatchTrainingAfter("minimize");
                yield return BatchTrainingBefore("restore");
                yield return RayClick("reference_restore"); yield return Settle();
                Require(controller.ReferenceOpen && !controller.ReferenceMinimized, "Reader restored through actual ray input.");
                yield return BatchTrainingAfter("restore");
                yield return BatchTrainingBefore("reposition");
                var panel = controller.view.referencePanel;
                Vector3 before = panel.transform.position;
                yield return DragReference(); yield return Settle();
                Require(panel.HasAcceptedPose && !panel.PlacementBlocked && Vector3.Distance(before, panel.transform.position) > .01f,
                    "Actual Trigger drag did not retain a changed accepted Reader pose.");
                yield return BatchTrainingAfter("reposition");
            }
            else
            {
                yield return BatchTrainingBefore("pause_resume");
                yield return PauseRoutine();
                yield return BatchTrainingAfter("pause_resume");
            }
            Require(controller.TutorialCompletedFactCount == completed && controller.TutorialFact == pendingFact,
                "Auxiliary capture changed mandatory Training progress.");
        }

        static IEnumerator BatchTrainingBefore(string fact)
        {
            if (!BatchStrictFocus || BatchVideoPlaybackCheck && fact != "video_play" && fact != "video_pause") yield break;
            Require(controller.Record.training_version == batchExpectedTraining, "Observed training version differs from pinned batch version.");
            string dir = Path.Combine(batchRoot, "training_" + (++batchFactIndex).ToString("000") + "_" + fact);
            ResearchS1GameViewVideo.Begin(dir, controller.Record.session_id, controller.Record.run_id, 30, 90);
            File.WriteAllText(Path.Combine(dir, "before.json"), BatchSnapshot());
            yield return Wait(2);
        }

        static IEnumerator BatchTrainingAfter(string fact)
        {
            if (!BatchStrictFocus || BatchVideoPlaybackCheck && fact != "video_play" && fact != "video_pause") yield break;
            yield return Wait(4);
            string dir = Path.Combine(batchRoot, "training_" + batchFactIndex.ToString("000") + "_" + fact);
            File.WriteAllText(Path.Combine(dir, "after.json"), BatchSnapshot());
            ResearchS1GameViewVideo.Stop();
            double until = activeTime + 15;
            while (!(bool)Newtonsoft.Json.Linq.JObject.Parse(ResearchS1GameViewVideo.Audit())["cleanup_observed"])
            { Require(activeTime < until, "Capture drain timeout."); yield return null; }
            var audit = Newtonsoft.Json.Linq.JObject.Parse(ResearchS1GameViewVideo.Audit());
            Require((string)audit["status"] == "ACTUAL_FRAMES_RECORDED_PENDING_VISUAL_REVIEW", "Training capture failed: " + audit);
        }

        static IEnumerator BatchTrainingMedia(string fact)
        {
            string section, command;
            switch (fact)
            {
                case "manual_contents": section = "user_manual"; command = "reference_media.contents"; break;
                case "manual_select": section = "user_manual"; command = "section"; break;
                case "manual_next": section = "user_manual"; command = "reference_next"; break;
                case "steps_next": section = "step_by_step"; command = "reference_next"; break;
                case "steps_back": section = "step_by_step"; command = "reference_previous"; break;
                case "steps_contents": section = "step_by_step"; command = "reference_media.contents"; break;
                case "steps_select": section = "step_by_step"; command = "section"; break;
                case "steps_all": section = "step_by_step"; command = "reference_media.view_all"; break;
                case "steps_choose": section = "step_by_step"; command = "reference_media.step.1"; break;
                case "image_zoom": section = "illustrated"; command = "reference_media.zoom_in"; break;
                case "image_pan": section = "illustrated"; command = "reference_media.pan_right"; break;
                case "video_play": section = "video"; command = "reference_media.play"; break;
                case "video_pause": section = "video"; command = "reference_media.pause"; break;
                case "video_seek": section = "video"; command = "reference_media.seek_forward"; break;
                case "video_chapter": section = "video"; command = "section"; break;
                default: throw new InvalidOperationException("No observed SDK automation for tutorial fact " + fact);
            }
            var viewer = controller.view.ReferenceViewer;
            if (controller.ReferenceSection != section) yield return RayClick("reference_tab." + section);
            yield return AwaitViewer(() => (viewer.IsReady && viewer.BoundSection == section) || viewer.Status == "Error",
                fact + " bound reader", section == "video" ? 60 : 12);
            Require(viewer.IsReady && viewer.BoundSection == section, "Reader failed before " + fact + ": " + viewer.Status);
            if ((fact == "manual_contents" || fact == "steps_contents") && viewer.ContentsOpen)
                yield return RayClick("reference_media.contents");
            if (viewer.ContentsOpen && (fact == "steps_next" || fact == "image_zoom"))
            {
                var package = (ResearchReferenceViewer.Package)typeof(ResearchReferenceViewer).GetField("package", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(viewer);
                string id = fact == "image_zoom" ? viewer.SectionIds.First() : package.contents.First(e => e.format == "steps" && e.index >= 1 && e.index < package.steps.Length
                    && package.steps[e.index - 1].topic_id == package.steps[e.index].topic_id).id;
                yield return BatchChooseSection(id);
            }
            if (command == "section")
            {
                string currentSection = viewer.SectionId;
                var package = (ResearchReferenceViewer.Package)typeof(ResearchReferenceViewer).GetField("package", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(viewer);
                var target = package.contents.FirstOrDefault(entry => viewer.SectionIds.Contains(entry.id) && entry.id != currentSection
                    && (section == "video" ? viewer.NativeVideoPlayer && Math.Abs(entry.start_seconds - viewer.NativeVideoPlayer.time) > .05
                        : entry.index != viewer.PageIndex));
                Require(target != null, "No different content/chapter position for " + fact);
                if (!viewer.ContentsOpen) yield return RayClick("reference_media.contents");
                yield return Settle();
                yield return BatchChooseSection(target.id);
                yield break;
            }
            if (fact == "steps_choose") yield return BatchRevealDocumentAction(command);
            float imagePanStart = 0;
            if (fact == "image_pan")
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    var imageState = Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata);
                    if ((float)imageState["image_viewport"]["width"] < .999f) break;
                    Require((float)imageState["image_zoom"] < 3, "Infographic cannot pan horizontally at maximum zoom: " + imageState);
                    yield return RayClick("reference_media.zoom_in");
                    yield return Settle();
                }
                var readyImage = Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata);
                Require((float)readyImage["image_viewport"]["width"] < .999f, "Actual infographic width still fits viewport; pan would be a no-op.");
                imagePanStart = (float)readyImage["image_viewport"]["x"];
                command = imagePanStart > .001f ? "reference_media.pan_left" : "reference_media.pan_right";
            }
            var nativeBefore = fact == "video_play" ? viewer.NativeVideoPlayer : null;
            string bindingBefore = viewer.BindingToken;
            yield return RayClick(command);
            yield return Settle();
            if (BatchVideoPlaybackCheck && fact == "video_play")
            {
                Require(nativeBefore && viewer.NativeVideoPlayer == nativeBefore && viewer.BindingToken == bindingBefore,
                    "Training cue transition replaced native decoder/binding after Play.");
                double startTime = nativeBefore.time;
                yield return Wait(2);
                Require(viewer.NativeVideoPlayer == nativeBefore && viewer.BindingToken == bindingBefore && nativeBefore.isPlaying
                    && nativeBefore.time > startTime + .5, "Native playback did not continue through next Training cue.");
                FaultRows.Add(new { kind = "native_video_cue_continuity", decoder = nativeBefore.GetInstanceID(), bindingBefore,
                    startTime, endTime = nativeBefore.time, frame = nativeBefore.frame, hardware_verified = false });
            }
            if (BatchVideoPlaybackCheck && fact == "video_pause")
            {
                Require(viewer.NativeVideoPlayer && !viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.isPaused,
                    "Actual native decoder did not pause after deliberate pause input.");
                double pausedTime = viewer.NativeVideoPlayer.time; yield return Wait(.5);
                Require(Math.Abs(viewer.NativeVideoPlayer.time - pausedTime) < .1, "Paused native video continued advancing.");
                FaultRows.Add(new { kind = "native_video_pause", pausedTime, observedTime = viewer.NativeVideoPlayer.time, hardware_verified = false });
            }
            if (fact == "image_pan")
            {
                float imagePanEnd = (float)Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata)["image_viewport"]["x"];
                Require(!Mathf.Approximately(imagePanStart, imagePanEnd), "Pan did not change the actual infographic viewport.");
                FaultRows.Add(new { kind = "reader_image_pan", imagePanStart, imagePanEnd, command, frame = Time.frameCount });
            }
        }

        static IEnumerator BatchRevealDocumentAction(string command)
        {
            var viewer = controller.view.ReferenceViewer;
            var scroll = (ScrollRect)typeof(ResearchReferenceViewer).GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(viewer);
            var focus = controller.view.referencePanel.GetComponent<ResearchSlateFocus>();
            var ray = Ray(1); var corners = new Vector3[4];
            HandNearBody(1);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var target = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == command);
                Require(target, "Missing document action " + command);
                var rect = target.GetComponent<RectTransform>(); rect.GetWorldCorners(corners);
                var local = corners.Select(scroll.viewport.InverseTransformPoint).ToArray();
                var bounds = scroll.viewport.rect;
                if (local.All(point => bounds.Contains(point))) yield break;
                Require(local.All(point => point.x >= bounds.xMin && point.x <= bounds.xMax), "Document action clipped horizontally: " + command);
                Require(viewer.CanScrollDocument && !viewer.ContentsOpen, "Document scrolling unavailable: " + command);
                var pose = Hand(1);
                pose.rotation = Quaternion.FromToRotation(ray.Forward, (scroll.viewport.TransformPoint(bounds.center) - ray.Origin).normalized) * pose.rotation;
                Drive(1, pose); yield return Wait(.04);
                if (!focus.Focused) continue;
                float axis = local.Max(point => point.y) >= bounds.yMax ? 1 : -1;
                float before = scroll.verticalNormalizedPosition;
                try { Drive(1, pose, stick: new Vector2(0, axis)); yield return Wait(.1); }
                finally { Drive(1, pose); }
                yield return Wait(.04);
                float after = scroll.verticalNormalizedPosition;
                FaultRows.Add(new { kind = "reader_document_scroll", command, before, after, axis, frame = Time.frameCount, logger_sequence = controller.Logger.LastSequence });
                Require(!Mathf.Approximately(before, after), "Guarded thumbstick did not move document: " + command);
            }
            throw new InvalidOperationException("Bounded document navigation did not expose " + command);
        }

        static IEnumerator BatchChooseSection(string id)
        {
            var viewer = controller.view.ReferenceViewer;
            var scroll = (ScrollRect)typeof(ResearchReferenceViewer).GetField("contentsScroll", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(viewer);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var target = controller.view.GetComponentsInChildren<ResearchUIAction>(false).FirstOrDefault(a => a.command == "reference_media.section." + id);
                Require(target, "Missing contents entry " + id);
                var targetRect = (RectTransform)target.transform; var local = scroll.viewport.InverseTransformPoint(targetRect.TransformPoint(targetRect.rect.center));
                if (scroll.viewport.rect.Contains(local))
                { yield return RayClick(target.command); yield return Settle(); yield break; }
                yield return RayClick(local.y > scroll.viewport.rect.yMax ? "reference_media.contents_up" : "reference_media.contents_down");
                yield return Settle();
            }
            throw new InvalidOperationException("Bounded contents navigation did not expose " + id);
        }
    }
}
#endif
