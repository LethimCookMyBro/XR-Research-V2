#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static partial class ResearchS1FlowDriver
    {
        public static readonly List<object> FaultRows = new List<object>();
        const BindingFlags PrivateReader = BindingFlags.Instance | BindingFlags.NonPublic;
        static string exportFaultPath, exportFaultMarker;

        static void FaultContext()
        {
            Require(controller && controller.Record != null && controller.Record.test_data &&
                !string.IsNullOrEmpty(controller.Record.participant_id) && controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) &&
                controller.Logger != null && controller.Record.run_id == controller.Logger.RunId && controller.Flow.IsTaskActive &&
                Application.isFocused && ResearchFlowVisualInput.Running, "Fault injection requires the current isolated active DEV SDK session.");
        }

        static Action clearSdkTrace;
        static void ClearSdkTrace()
        {
            clearSdkTrace?.Invoke();
            clearSdkTrace = null;
        }

        static void TraceSdkControl(ResearchUIAction action)
        {
            ClearSdkTrace();
            var trigger = action.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            bool owned = !trigger;
            if (owned) trigger = action.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entries = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            clearSdkTrace = () =>
            {
                if (!trigger) return;
                foreach (var entry in entries) trigger.triggers.Remove(entry);
                if (owned) UnityEngine.Object.Destroy(trigger);
            };
            foreach (var kind in new[] { UnityEngine.EventSystems.EventTriggerType.PointerDown, UnityEngine.EventSystems.EventTriggerType.PointerUp, UnityEngine.EventSystems.EventTriggerType.PointerClick })
            {
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = kind };
                entry.callback.AddListener(data =>
                {
                    var pointer = (UnityEngine.EventSystems.PointerEventData)data;
                    FaultRows.Add(new { kind = kind.ToString(), command = action.command, frame = Time.frameCount,
                        realtime = Time.realtimeSinceStartupAsDouble, pointer_id = pointer.pointerId,
                        click_count = pointer.clickCount, input_scope = "Editor ISDK pointer event" });
                });
                entries.Add(entry); trigger.triggers.Add(entry);
            }
        }

        static void FaultResult(string id, string scope)
        {
            FaultRows.Add(new { case_id = id, result = "PASS_EDITOR", scope, frame = Time.frameCount,
                session = controller.Record.session_id, run = controller.Record.run_id, logger_sequence = controller.Logger.LastSequence });
            Step("fault verified " + id);
        }

        public static string InspectBoundary() => Start(InspectRoutine(true), 40);
        public static string ReaderFailures(string missingPdfRegistry) => Start(ReaderFailuresRoutine(missingPdfRegistry), 180);
        public static string ReaderEndPause() => Start(ReaderEndPauseRoutine(), 100);
        public static string ExportRetry(string ownedEvidenceRoot) => Start(ExportRetryRoutine(Path.GetFullPath(ownedEvidenceRoot)), 50);
        public static string LoggerFailureStop() => Start(LoggerFailureStopRoutine(), 50);
        public static string RegistryFailureStop() => Start(LoggerFailureStopRoutine(false), 50);
        public static string ReaderForFocus() => Start(ReaderForFocusRoutine(), 35);
        public static string ReaderAfterFocus() => Start(ReaderAfterFocusRoutine(), 45);

        static IEnumerator OpenReaderForFault()
        {
            if (!controller.ReferenceOpen) yield return RayClick("reference");
            var panel = controller.view.referencePanel;
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            yield return Settle();
            var rect = (RectTransform)panel.transform;
            ResearchFlowVisualInput.SetHeadFacing(rect.TransformPoint(rect.rect.center));
            yield return Wait(1.2);
            if (panel.PlacementBlocked)
            {
                var head = controller.origin.Camera.transform;
                ResearchFlowVisualInput.SetHeadFacing(head.position + Quaternion.AngleAxis(90, Vector3.up) * head.forward);
                yield return Wait(1.2);
                if (panel.PlacementBlocked) { yield return RayClick("panel_recovery"); yield return Settle(); }
            }
            yield return AwaitViewer(() => panel.IsReadableFromHead, "fault Reader readable facing");
        }

        static object ReaderPrivate(ResearchReferenceViewer viewer, string name) => typeof(ResearchReferenceViewer).GetField(name, PrivateReader).GetValue(viewer);
        static string ReaderError(ResearchReferenceViewer viewer) => (string)Newtonsoft.Json.Linq.JObject.FromObject(viewer.Metadata)["error"];

        static void InjectVideoReadFault(ResearchReferenceViewer viewer, string path, string hash)
        {
            var original = (ResearchReferenceViewer.Package)ReaderPrivate(viewer, "package");
            var clone = JsonUtility.FromJson<ResearchReferenceViewer.Package>(JsonUtility.ToJson(original));
            clone.video.streaming_path = path; clone.video.sha256 = hash;
            typeof(ResearchReferenceViewer).GetMethod("CancelVideo", PrivateReader).Invoke(viewer, null);
            typeof(ResearchReferenceViewer).GetMethod("ClearContent", PrivateReader).Invoke(viewer, null);
            typeof(ResearchReferenceViewer).GetField("package", PrivateReader).SetValue(viewer, clone);
            typeof(ResearchReferenceViewer).GetMethod("Render", PrivateReader).Invoke(viewer, null);
        }

        static IEnumerator VideoReady()
        {
            if (controller.ReferenceSection != "video") yield return RayClick("reference_tab.video");
            yield return AwaitViewer(() => controller.view.ReferenceViewer.IsReady && controller.view.ReferenceViewer.NativeVideoPlayer &&
                controller.view.ReferenceViewer.NativeVideoPlayer.frame >= 0, "decoded video ready", 18);
        }

        static IEnumerator ReaderFailuresRoutine(string missingPdfRegistry)
        {
            FaultContext(); ReaderLog.Clear();
            Require(missingPdfRegistry.StartsWith("ResearchReaderDemo/", StringComparison.Ordinal) && !missingPdfRegistry.Contains(".."), "Owned missing-PDF fixture registry required.");
            yield return OpenReaderForFault();
            yield return RayClick("reference_tab.user_manual");
            var viewer = controller.view.ReferenceViewer;
            yield return AwaitViewer(() => viewer.IsReady, "manual ready before loader fault");
            string key = (string)ReaderPrivate(viewer, "key"), fallback = (string)ReaderPrivate(viewer, "fallback");
            viewer.UseRegistry(missingPdfRegistry); viewer.Bind(key, "pdf", true, fallback);
            yield return AwaitViewer(() => viewer.Status == "Error", "actual missing original PDF loader error");
            Require(ReaderError(viewer) == "Original PDF missing or SHA256 mismatch", "Injected absent PDF source reached the expected loader validation error: " + ReaderError(viewer));
            Require(!viewer.IsReady, "Missing source PDF receives no reading readiness."); ViewerState("missing_pdf_source_error");
            yield return RayClick("reference_close"); yield return Settle();
            Require(!controller.ReferenceOpen, "SDK Close escapes missing PDF loader failure.");
            yield return OpenReaderForFault(); yield return RayClick("reference_tab.user_manual");
            yield return AwaitViewer(() => viewer.IsReady, "untouched original manual reopens");
            FaultResult("missing_pdf_source_close_reopen", "Owned registry points at absent source TextAsset; actual loader and SDK Close/reopen; original registry/media unchanged.");

            yield return VideoReady();
            string corrupt = "Assets/VRMaintenanceResearch/ResearchFlow/Editor/ReaderDemoFixtures/source.pdf";
            string hash; using (var sha = SHA256.Create()) hash = string.Concat(sha.ComputeHash(File.ReadAllBytes(corrupt)).Select(b => b.ToString("x2")));
            InjectVideoReadFault(viewer, "ResearchReaderDemo/source.pdf", hash);
            yield return AwaitViewer(() => viewer.Status == "Error", "actual corrupt-container decoder error", 18);
            Require(ReaderError(viewer).StartsWith("Video error:", StringComparison.Ordinal), "Injected wrong container reached actual decoder error: " + ReaderError(viewer));
            Require(!viewer.IsReady, "Corrupt decoder does not report ready."); ViewerState("corrupt_video_error");
            yield return RayClick("reference_media.retry"); yield return VideoReady();
            FaultResult("corrupt_video_sdk_retry", "Hash-valid wrong container triggers real Windows decoder error; SDK Retry reloads original registry and decoded frames.");

            InjectVideoReadFault(viewer, "ResearchReaderDemo/absent_" + Guid.NewGuid().ToString("N") + ".mp4", hash);
            yield return AwaitViewer(() => viewer.Status == "Error", "actual missing MP4 read failure", 12);
            Require(ReaderError(viewer).StartsWith("Video read failed:", StringComparison.Ordinal), "Injected absent MP4 reached actual file read error: " + ReaderError(viewer));
            Require(!viewer.IsReady && !viewer.NativeVideoPlayer, "Missing MP4 cannot own a prepared decoder."); ViewerState("missing_mp4_error");
            yield return RayClick("reference_media.retry"); yield return VideoReady();
            FaultResult("missing_mp4_sdk_retry", "Fault URI has no physical file; real UnityWebRequest read failure and SDK Retry; no approved/draft media changed.");

            foreach (bool close in new[] { false, true })
            {
                yield return RayClick("reference_media.play");
                yield return AwaitViewer(() => viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.time > .5, "video playing before seek fault");
                var oldPlayer = viewer.NativeVideoPlayer;
                var oldAudio = (AudioSource)ReaderPrivate(viewer, "videoAudio");
                var oldTexture = (RenderTexture)ReaderPrivate(viewer, "videoTexture");
                // Drop the real decoder frame receipt, not the viewer state or a fabricated completion.
                oldPlayer.sendFrameReadyEvents = false;
                yield return RayClick("reference_media.seek_forward");
                Require(viewer.Status == "Seeking", "A pending seek without its target-frame receipt was observed.");
                double target = (double)ReaderPrivate(viewer, "seekTarget");
                ViewerState(close ? "pending_seek_before_close" : "pending_seek_before_switch");
                yield return RayClick(close ? "reference_close" : "reference_tab.illustrated");
                yield return Settle();
                Require(!oldPlayer && !oldAudio && !oldTexture, "Cancelled seek retires old player, audio and RenderTexture.");
                if (close) yield return OpenReaderForFault();
                yield return VideoReady();
                Require(!viewer.NativeVideoPlayer.isPlaying && Math.Abs(viewer.NativeVideoPlayer.time - target) <= .2, "Reopen restores pending bookmark without autoplay.");
                FaultResult(close ? "pending_seek_sdk_close_reopen" : "pending_seek_sdk_switch_restore", "Actual SDK seek with injected lost frameReady receipt; observed Seeking, real SDK cancellation and native decoder/resource cleanup.");
            }

            yield return ReaderEndPauseRoutine();
        }

        static IEnumerator ReaderEndPauseRoutine()
        {
            FaultContext();
            yield return OpenReaderForFault();
            yield return VideoReady();
            var viewer = controller.view.ReferenceViewer;
            var timeline = (RectTransform)viewer.Timeline.handleRect.parent;
            double endTarget = viewer.NativeVideoPlayer.length - 1.5;
            Require(endTarget > 0 && !double.IsInfinity(endTarget), "Finite prepared clip duration required for EOF test.");
            float nearEnd = (float)(endTarget / viewer.NativeVideoPlayer.length);
            yield return RayPress(timeline, "video near-end timeline handle area", nearEnd);
            yield return AwaitViewer(() => viewer.IsReady, "timeline seek acknowledged", 12);
            double acceptedTarget = (double)ReaderPrivate(viewer, "seekTarget");
            Require(acceptedTarget > viewer.NativeVideoPlayer.length - 3 && acceptedTarget < viewer.NativeVideoPlayer.length - .5,
                "SDK slider selected a safe EOF tail, not the final decoded frame.");
            Require(viewer.NativeVideoPlayer.frameRate > 0 && Math.Abs(viewer.NativeVideoPlayer.time - acceptedTarget) <= 2 / viewer.NativeVideoPlayer.frameRate,
                "Decoded seek receipt matches the actual SDK-accepted target within two frames.");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.Status == "Ended", "actual decoder end", 8);
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer.isPlaying && viewer.NativeVideoPlayer.time < 5, "SDK replay from ended video", 12);
            FaultResult("ended_sdk_replay", "SDK timeline seek, actual endReached, explicit Play replay; not full-content human review.");

            yield return RayClick("pause"); yield return Settle();
            Require(controller.Flow.Paused && !controller.ReferenceOpen && !viewer.NativeVideoPlayer, "Participant Pause closes Reader and native decoder.");
            yield return RayClick("session_resume"); yield return Settle();
            Require(!controller.Flow.Paused, "Authorized participant Continue resumes same task.");
            yield return OpenReaderForFault(); yield return VideoReady();
            Require(!viewer.NativeVideoPlayer.isPlaying, "Reader after Pause never autoplays.");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => viewer.NativeVideoPlayer.isPlaying, "explicit Play after Pause");
            FaultResult("reader_sdk_pause_resume", "Actual SDK Pause/Continue/reopen/explicit Play, no lifecycle callback invocation.");
            yield return RayClick("reference_close"); yield return Settle();
        }

        static IEnumerator ExportRetryRoutine(string ownedRoot)
        {
            FaultContext();
            var data = (string)typeof(ResearchSessionFlowController).GetProperty("DataRoot", PrivateReader).GetValue(controller);
            Require(Path.GetFullPath(data).StartsWith(ownedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Export obstruction restricted to owned evidence registry.");
            string path = Path.Combine(data, "Exports");
            Require(!Directory.Exists(path) && !File.Exists(path), "No existing exports may be moved or overwritten.");
            exportFaultPath = path; exportFaultMarker = "S1_OWNED_EXPORT_FAULT_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(path, exportFaultMarker);
            try
            {
                yield return ConsoleClick(Console(), "export");
                yield return AwaitViewerless(() => !string.IsNullOrEmpty(controller.ExportDeliveryError), "actual export I/O failure", 10);
                Require(string.IsNullOrEmpty(controller.Error) && controller.Flow.IsTaskActive, "Recoverable export failure does not forge a fatal task error.");
                FaultRows.Add(new { case_id = "export_obstruction_observed", error = controller.ExportDeliveryError });
                ClearOwnedExportFault();
                yield return ConsoleClick(Console(), "export");
                yield return AwaitViewerless(() => !string.IsNullOrEmpty(controller.LastExportPath) && File.Exists(Path.Combine(controller.LastExportPath, "manifest.json")), "retry creates actual snapshot", 15);
                Require(string.IsNullOrEmpty(controller.ExportDeliveryError), "Successful retry clears delivery error.");
                FaultResult("export_real_io_failure_retry", "Actual SDK participant session; authorized IMGUI researcher Export/retry, not participant SDK/native Export. Snapshot remains partial while active.");
            }
            finally { ClearOwnedExportFault(); }
        }

        public static void ClearOwnedExportFault()
        {
            if (string.IsNullOrEmpty(exportFaultPath)) return;
            Require(!Directory.Exists(exportFaultPath) && (!File.Exists(exportFaultPath) || File.ReadAllText(exportFaultPath) == exportFaultMarker), "Unknown export-path drift preserved.");
            if (File.Exists(exportFaultPath)) File.Delete(exportFaultPath);
            exportFaultPath = exportFaultMarker = null;
        }

        static IEnumerator LoggerFailureStopRoutine(bool disposeWriter = true)
        {
            FaultContext();
            if (disposeWriter)
            {
                var writer = (StreamWriter)controller.Logger.GetType().GetField("eventsWriter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller.Logger);
                writer.Dispose();
            }
            yield return RayClick("inspect");
            yield return AwaitViewerless(() => !string.IsNullOrEmpty(controller.Error), "actual storage failure becomes sticky error", 8);
            Require(controller.Error.StartsWith(disposeWriter ? "Logger write failed:" : "Session persistence failed:", StringComparison.Ordinal), "Expected injected failure cause: " + controller.Error);
            Require(controller.Flow.IsTaskActive && controller.Flow.InspectPassed != true && !controller.InspectionFeedbackVisible, "Storage fault produces no successful Inspect feedback credit or task transition.");
            yield return RayClick("stop"); yield return Wait(.5);
            Require(controller.Flow.Paused && !string.IsNullOrEmpty(controller.Error), "SDK Stop reaches researcher wait without clearing fatal storage error.");
            FaultResult(disposeWriter ? "logger_fatal_sdk_stop" : "registry_lock_sdk_stop", "Owned disposed writer or externally exclusively locked registry; genuine SDK Inspect/Stop; no fabricated resume or data repair. Dataset must remain incomplete.");
        }

        static IEnumerator ReaderForFocusRoutine()
        {
            FaultContext(); yield return OpenReaderForFault(); yield return VideoReady();
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => controller.view.ReferenceViewer.NativeVideoPlayer.isPlaying, "playing before real OS focus loss");
        }

        static IEnumerator ReaderAfterFocusRoutine()
        {
            FaultContext(); Require(!controller.ReferenceOpen, "Application pause closed Reader before authorized resume.");
            var events = new List<Newtonsoft.Json.Linq.JObject>();
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                while (!reader.EndOfStream) events.Add(Newtonsoft.Json.Linq.JObject.Parse(reader.ReadLine()));
            var pause = events.LastOrDefault(e => (string)e["event"] == "SESSION_PAUSED" && (string)e["reason"] == "application_focus_lost" && (string)e["run_id"] == controller.Record.run_id);
            Require(pause != null && events.Any(e => (string)e["event"] == "SESSION_RESUMED" && (long)e["seq"] > (long)pause["seq"] && (string)e["after"]?["pause_kind"] == "application"), "Same-run real application-focus pause and authorized resume events required.");
            yield return OpenReaderForFault(); yield return VideoReady();
            Require(!controller.view.ReferenceViewer.NativeVideoPlayer.isPlaying, "Focus restoration does not autoplay.");
            yield return RayClick("reference_media.play");
            yield return AwaitViewer(() => controller.view.ReferenceViewer.NativeVideoPlayer.isPlaying, "explicit Play after real focus restore");
            FaultResult("reader_real_focus_restore", "External test must prove real OS focus lost/application pause and authorized Console resume before this SDK reopen/Play.");
            yield return RayClick("reference_close");
        }

        public static string ConsoleActionOnce(string id)
        {
            Require(controller && controller.Record != null && controller.Record.test_data && controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) && Application.isFocused, "Current focused DEV researcher context required.");
            Require(id == "resume" || id == "export", "Only the existing researcher Resume/Export controls are eligible.");
            var console = Console();
            Require(console.ControlRects.ContainsKey(id), "Drawn researcher control required: " + id);
            var rect = console.ControlRects[id]; var origin = console.rootVisualElement.worldBound.position;
            var point = origin + rect.center;
            console.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0, clickCount = 1 });
            console.SendEvent(new Event { type = EventType.MouseUp, mousePosition = point, button = 0, clickCount = 1 });
            console.Repaint(); FocusGame();
            return Report();
        }
    }
}
#endif
