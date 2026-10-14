#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1GameViewVideo
    {
        sealed class Slot
        {
            internal RenderTexture Texture;
            internal AsyncGPUReadbackRequest Request;
            internal bool Pending;
            internal int Index;
            internal string Receipt;
        }

        sealed class Capture
        {
            internal ResearchSessionFlowController Controller;
            internal ResearchS1GameViewVideoWriter Writer;
            internal readonly Slot[] Slots = { new Slot(), new Slot() };
            internal Coroutine Routine;
            internal string Output, Session, Run, Participant, StopReason, Error, GraphicsBackend;
            internal int Width, Height, Fps, DueSamples, Submitted, Dropped, ReadbackErrors, LastFrame = -1;
            internal double Started, Next, Deadline, LastRendered;
            internal bool Stopping, CleanupObserved, GraphicsUVStartsAtTop;
        }

        static Capture current;
        public static bool Running => current != null && !current.Stopping;

        public static string Begin(string evidenceDirectory, string expectedSessionId, string expectedRunId,
            int framesPerSecond = 15, double maxSeconds = 120)
        {
            if (current != null && (!current.Stopping || !current.CleanupObserved))
                throw new InvalidOperationException("Previous recording still owns resources.");
            if (framesPerSecond < 1 || framesPerSecond > 30 || double.IsNaN(maxSeconds) || double.IsInfinity(maxSeconds) ||
                maxSeconds < 1 || maxSeconds > 1200) throw new ArgumentOutOfRangeException("Capture requires 1..30 fps and 1..1200 seconds.");
            var controller = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsSortMode.None).Single();
            // shortcut: UV origin selects PNG row order, verify paired native screenshots before using another graphics backend.
            var capture = new Capture { Controller = controller, Session = expectedSessionId, Run = expectedRunId,
                Fps = framesPerSecond, Width = Screen.width, Height = Screen.height,
                GraphicsUVStartsAtTop = SystemInfo.graphicsUVStartsAtTop, GraphicsBackend = SystemInfo.graphicsDeviceType.ToString() };
            CheckContext(capture);
            if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("GPU asynchronous readback is unavailable. No substitute capture.");
            if (Application.isBatchMode || EditorWindow.focusedWindow == null ||
                EditorWindow.focusedWindow.GetType().FullName != "UnityEditor.GameView")
                throw new InvalidOperationException("A focused real Game View is required. Recording will not change focus.");
            if (capture.Width <= 0 || capture.Height <= 0 || (long)capture.Width * capture.Height > 1920L * 1080)
                throw new InvalidOperationException("Keep the existing Game View at at most 2073600 pixels. Recording does not resize it.");
            capture.Output = FreshExternalDirectory(evidenceDirectory);
            capture.Participant = controller.Record.participant_id;
            capture.Started = capture.Next = capture.LastRendered = EditorApplication.timeSinceStartup;
            capture.Deadline = capture.Started + maxSeconds;
            current = capture;
            try
            {
                WriteJson(Path.Combine(capture.Output, "capture.json"), new {
                    schema = "research.s1.gameview-capture.1", status = "RECORDING", capture_method = "ScreenCapture.CaptureScreenshotIntoRenderTexture after WaitForEndOfFrame",
                    execution_environment = "Editor", input_context = "SimulatedController", test_data = true, hardware_verified = false,
                    participant_id = capture.Participant, session_id = capture.Session, run_id = capture.Run,
                    logger_run_id = controller.Logger.RunId, language = controller.Record.language.ToString(),
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, unity_version = Application.unityVersion,
                    graphics_backend = capture.GraphicsBackend, graphics_uv_starts_at_top = capture.GraphicsUVStartsAtTop,
                    png_rows_reversed = capture.GraphicsUVStartsAtTop,
                    width = capture.Width, height = capture.Height, requested_fps = capture.Fps, max_seconds = maxSeconds,
                    max_png_bytes = 2L * 1024 * 1024 * 1024, owned_gpu_slots = 2, queued_frame_limit = ResearchS1GameViewVideoWriter.QueueCapacity,
                    started_editor_realtime = capture.Started, utc = DateTime.UtcNow.ToString("O"),
                    audio = "not_recorded", color_profile = "Unity PNG encoder without gamma or profile metadata",
                    pixels = "Actual Game View RGBA32 readback. Platform UV-origin row normalization only; RGBA channel bytes preserved. No rerender, copied stills, interpolation or task mutation."
                });
                capture.Writer = new ResearchS1GameViewVideoWriter(capture.Output, (data, width, height) =>
                {
                    ResearchS1GameViewVideoWriter.ReverseRgbaRows(data, width, height, capture.GraphicsUVStartsAtTop);
                    return ImageConversion.EncodeArrayToPNG(data, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
                });
                foreach (var slot in capture.Slots)
                {
                    slot.Texture = new RenderTexture(capture.Width, capture.Height, 0, RenderTextureFormat.ARGB32)
                        { name = "S1 owned GameView capture", hideFlags = HideFlags.HideAndDontSave };
                    if (!slot.Texture.Create()) throw new InvalidOperationException("Owned capture render target could not be created.");
                }
                EditorApplication.update += Watch;
                EditorApplication.playModeStateChanged += PlayState;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                capture.Routine = controller.StartCoroutine(Frames(capture));
                return Audit();
            }
            catch (Exception failure)
            {
                capture.Error = failure.ToString();
                Finish(capture, "start_failed");
                throw;
            }
        }

        static void CheckContext(Capture capture)
        {
            var controller = capture.Controller;
            if (!Application.isEditor || !EditorApplication.isPlaying || EditorApplication.isPaused ||
                !ResearchFlowVisualInput.Running || !controller || !controller.isActiveAndEnabled || controller.Record == null ||
                !controller.Record.test_data || string.IsNullOrEmpty(controller.Record.participant_id) ||
                !controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) || controller.Flow == null ||
                controller.Logger == null || controller.Logger.State == HandsLabLogState.WriteError ||
                string.IsNullOrEmpty(capture.Session) || string.IsNullOrEmpty(capture.Run) ||
                capture.Session != controller.Record.session_id || capture.Run != controller.Record.run_id ||
                capture.Run != controller.Logger.RunId ||
                (capture.Participant != null && capture.Participant != controller.Record.participant_id))
                throw new InvalidOperationException("Capture requires an unpaused Editor DEV test_data session, running simulator and exact current session/run/logger identity.");
        }

        static string FreshExternalDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("Use an absolute fresh external output path.");
            string full = Path.GetFullPath(path);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.Equals(project, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture output must be local and outside the Unity project.");
            if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Capture output already exists. No overwrite.");
            var parent = new DirectoryInfo(Path.GetDirectoryName(full));
            if (!parent.Exists) throw new DirectoryNotFoundException("Use an existing external parent directory.");
            for (var item = parent; item != null; item = item.Parent)
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Capture output cannot traverse a reparse point.");
            Directory.CreateDirectory(full);
            return full;
        }

        static IEnumerator Frames(Capture capture)
        {
            var end = new WaitForEndOfFrame();
            while (!capture.Stopping)
            {
                yield return end;
                if (capture.Stopping) yield break;
                try { Submit(capture); }
                catch (Exception failure) { capture.Error = failure.ToString(); }
                if (capture.Error != null) yield break;
            }
        }

        static void Submit(Capture capture)
        {
            CheckContext(capture);
            double now = EditorApplication.timeSinceStartup;
            capture.LastRendered = now;
            if (Time.frameCount == capture.LastFrame || now < capture.Next) return;
            capture.LastFrame = Time.frameCount;
            capture.Next = now + 1d / capture.Fps;
            capture.DueSamples++;
            if (Screen.width != capture.Width || Screen.height != capture.Height)
                throw new InvalidOperationException("Game View dimensions changed during recording.");
            var slot = capture.Slots.FirstOrDefault(s => !s.Pending);
            if (slot == null || capture.Writer.Pending >= ResearchS1GameViewVideoWriter.QueueCapacity) { capture.Dropped++; return; }
            var controller = capture.Controller;
            slot.Index = ++capture.Submitted;
            slot.Receipt = JsonConvert.SerializeObject(new {
                index = slot.Index, unity_frame = Time.frameCount, unity_realtime = Time.realtimeSinceStartupAsDouble,
                editor_realtime = now, utc = DateTime.UtcNow.ToString("O"),
                participant_id = controller.Record.participant_id, session_id = capture.Session, run_id = capture.Run,
                execution_environment = "Editor", input_context = "SimulatedController", test_data = true, hardware_verified = false,
                logger_timestamp = controller.Logger.Timestamp, logger_sequence = controller.Logger.LastSequence,
                logger_state = controller.Logger.State.ToString(), flow_state = controller.Flow.State.ToString(),
                task = controller.Flow.CurrentTask, flow_paused = controller.Flow.Paused, pause_kind = controller.PauseKind,
                task_active_elapsed = controller.TaskActiveElapsed, task_wall_elapsed = controller.TaskWallElapsed,
                application_focused = Application.isFocused, editor_paused = EditorApplication.isPaused,
                observed_guards = controller.guards.Where(g => g).Select(g => new {
                    side = g.side.ToString(), mode = g.Mode.ToString(), tracking_valid = g.TrackingValid,
                    suspended = g.Suspended, waiting_for_neutral = g.WaitingForNeutral }).ToArray(),
                controller_error = controller.Error, task_end_reason = controller.TaskEndReason,
                width = capture.Width, height = capture.Height
            });
            var previous = RenderTexture.active;
            try { ScreenCapture.CaptureScreenshotIntoRenderTexture(slot.Texture); }
            finally { RenderTexture.active = previous; }
            slot.Pending = true;
            try { slot.Request = AsyncGPUReadback.Request(slot.Texture, 0, TextureFormat.RGBA32, request => Readback(capture, slot, request)); }
            catch { slot.Pending = false; throw; }
        }

        static void Readback(Capture capture, Slot slot, AsyncGPUReadbackRequest request)
        {
            if (!slot.Pending) return;
            try
            {
                if (request.hasError) throw new InvalidOperationException("Owned Game View GPU readback failed.");
                if (request.width != capture.Width || request.height != capture.Height)
                    throw new InvalidDataException("GPU frame dimensions do not match the rendered receipt.");
                var pixels = request.GetData<byte>().ToArray();
                if (!capture.Writer.TryAdd(new ResearchS1GameViewVideoWriter.Frame(slot.Index, capture.Width, capture.Height, pixels, slot.Receipt)))
                    capture.Dropped++;
            }
            catch (Exception failure) { capture.ReadbackErrors++; capture.Error = failure.ToString(); }
            finally { slot.Pending = false; }
        }

        static void Watch()
        {
            var capture = current;
            if (capture == null || capture.Stopping) return;
            try
            {
                CheckContext(capture);
                if (capture.Error != null || capture.Writer.Error != null) { Finish(capture, "recording_error"); return; }
                double now = EditorApplication.timeSinceStartup;
                if (now >= capture.Deadline) { Finish(capture, "duration_limit"); return; }
                if (now - capture.LastRendered > 3) throw new TimeoutException("No actual end-of-frame Game View samples for three seconds. No focus repair or substitute frames.");
            }
            catch (Exception failure) { capture.Error = failure.ToString(); Finish(capture, "context_or_render_lost"); }
        }

        public static string Stop() { if (current != null && !current.Stopping) Finish(current, "requested_stop"); return Audit(); }
        static void PlayState(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) StopFor("play_exit"); }
        static void BeforeReload() { StopFor("assembly_reload"); }
        static void StopFor(string reason) { if (current != null && !current.Stopping) Finish(current, reason); }

        static void Finish(Capture capture, string reason)
        {
            if (capture.Stopping) return;
            capture.Stopping = true; capture.StopReason = reason;
            EditorApplication.update -= Watch;
            EditorApplication.playModeStateChanged -= PlayState;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            try { if (capture.Controller && capture.Routine != null) capture.Controller.StopCoroutine(capture.Routine); }
            catch (Exception failure) { capture.Error = JoinError(capture.Error, failure); }
            foreach (var slot in capture.Slots)
            {
                try
                {
                    if (slot.Pending) { slot.Request.WaitForCompletion(); Readback(capture, slot, slot.Request); }
                    if (slot.Texture) { slot.Texture.Release(); UnityEngine.Object.DestroyImmediate(slot.Texture); slot.Texture = null; }
                }
                catch (Exception failure) { capture.Error = JoinError(capture.Error, failure); }
            }
            bool writerStopped = capture.Writer == null;
            try { if (capture.Writer != null) writerStopped = capture.Writer.Close(); }
            catch (Exception failure) { capture.Error = JoinError(capture.Error, failure); }
            if (capture.Writer != null && capture.Writer.Error != null) capture.Error = JoinError(capture.Error, new IOException(capture.Writer.Error));
            capture.CleanupObserved = writerStopped && capture.Slots.All(s => !s.Pending && !s.Texture);
            try { WriteJson(Path.Combine(capture.Output, "result.json"), Summary(capture)); }
            catch (Exception failure) { capture.Error = JoinError(capture.Error, failure); Debug.LogError("S1 video result could not be persisted: " + failure); }
        }

        static string JoinError(string prior, Exception failure) => string.IsNullOrEmpty(prior) ? failure.ToString() : prior + "\n" + failure;
        static object Summary(Capture capture) => new {
            schema = "research.s1.gameview-capture.1", status = !capture.Stopping ? "RECORDING" : capture.Error != null || !capture.CleanupObserved ? "CAPTURE_ERROR" :
                capture.Writer != null && capture.Writer.Written > 0 ? "ACTUAL_FRAMES_RECORDED_PENDING_VISUAL_REVIEW" : "NO_ACTUAL_FRAMES",
            running = !capture.Stopping, output = capture.Output, participant_id = capture.Participant,
            session_id = capture.Session, run_id = capture.Run, stop_reason = capture.StopReason, error = capture.Error,
            graphics_backend = capture.GraphicsBackend, graphics_uv_starts_at_top = capture.GraphicsUVStartsAtTop,
            png_rows_reversed = capture.GraphicsUVStartsAtTop,
            due_samples = capture.DueSamples, gpu_capture_attempts = capture.Submitted,
            written = capture.Writer?.Written ?? 0, dropped_due_samples = capture.Dropped,
            readback_errors = capture.ReadbackErrors, writer_error_count = capture.Writer?.Error == null ? 0 : 1,
            png_bytes = capture.Writer?.Bytes ?? 0,
            cleanup_observed = capture.CleanupObserved, writer_drained = capture.Writer == null || capture.Writer.Drained,
            owned_gpu_pending = capture.Slots.Count(s => s.Pending), owned_gpu_remaining = capture.Slots.Count(s => s.Texture),
            started_editor_realtime = capture.Started, observed_editor_realtime = EditorApplication.timeSinceStartup,
            last_rendered_editor_realtime = capture.LastRendered, last_sampled_unity_frame = capture.LastFrame,
            hardware_verified = false, whole_workflow_verified = false, visual_review = "pending", audio = "not_recorded"
        };
        public static string Audit() => JsonConvert.SerializeObject(current == null ? new { status = "IDLE" } : Summary(current), Formatting.Indented);
        static void WriteJson(string path, object value)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(JsonConvert.SerializeObject(value, Formatting.Indented));
        }
    }
}
#endif
