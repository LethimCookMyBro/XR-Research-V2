#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Technical-build visual/pacing diagnostics. view_probe renders named S1 surfaces with an offscreen left-eye camera on the
    // device GPU (same shaders, textures, eye pixel grid, MSAA) per render config and times the real XR path for each;
    // frame_timing only reads. Neither is wearer evidence: the compositor, lenses and a moving head are outside both.
    public sealed class ResearchViewDiagnostic : MonoBehaviour
    {
        public const float MinScale = .8f, MaxScale = 1.3f;
        const int TimingCap = 120 * 60;
        static readonly (string name, int station, string target)[] Presets = {
            ("sign", 0, "Training_Room_Sign"), ("window", 0, "Training_Window_Frame"), ("door", 0, "Training_Door_Frame"),
            ("floor", 0, null), ("trainer", 0, "ResearchFlow_TrainingStation"), ("ui", 0, "#canvas"),
            ("task_a", 1, "ResearchFlow_TaskA_Port"), ("task_b", 2, "ResearchFlow_TaskB_Port"),
            ("board", 0, "Instruction_Board_0") };

        ResearchSessionFlowController owner;
        string directory, error;
        readonly float[] cpu = new float[TimingCap], main = new float[TimingCap], render = new float[TimingCap], gpu = new float[TimingCap], interval = new float[TimingCap];
        readonly int[] collections = new int[TimingCap];
        readonly FrameTiming[] timing = new FrameTiming[1];
        int samples, gcAtStart;
        bool sampling;
        ProfilerRecorder gcAlloc;
        long allocated;
        public string State { get; private set; } = "idle";
        public bool Running => State == "running";
        public object Status => new { state = State, error, directory, hardware_verified = false, wearer_evidence = false };

        public static string PlanRefusal(JObject plan, out string[] presets, out (float scale, int msaa)[] configs, out int frames, out float yawStep, out float hold)
        {
            presets = null; configs = null; frames = 0; yawStep = 0; hold = 0;
            try
            {
                presets = plan["presets"].Select(p => (string)p).ToArray();
                configs = plan["configs"].Select(c => ((float)c[0], (int)c[1])).ToArray();
                frames = (int?)plan["frames"] ?? 6; yawStep = (float?)plan["yaw_step"] ?? .02f; hold = (float?)plan["hold"] ?? 8;
            }
            catch (Exception ex) when (ex is NullReferenceException || ex is InvalidCastException || ex is FormatException || ex is ArgumentException)
            { return "plan needs presets[], configs[[scale,msaa]], frames, yaw_step, hold."; }
            if (presets.Length < 1 || presets.Length > Presets.Length || presets.Distinct().Count() != presets.Length || presets.Any(p => Presets.All(x => x.name != p)))
                return "presets must be distinct names from: " + string.Join(",", Presets.Select(p => p.name));
            if (configs.Length < 1 || configs.Length > 8 || configs.Any(c => c.scale < MinScale || c.scale > MaxScale || (c.msaa != 1 && c.msaa != 2 && c.msaa != 4)))
                return "configs: 1-8 of [scale " + MinScale + "-" + MaxScale + ", msaa 1|2|4].";
            if (frames < 1 || frames > 12 || yawStep < 0 || yawStep > .2f || hold < 0 || hold > 20) return "frames 1-12, yaw_step 0-0.2 deg, hold 0-20 s.";
            return null;
        }

        bool Idle => owner && owner.Flow == null && owner.Record == null && owner.TechnicalSelfSetup && !owner.Transitioning
            && !(owner.RenderComparison && owner.RenderComparison.Running) && !(owner.GetComponent<ResearchTechnicalScenario>()?.Running ?? false);

        public bool BeginProbe(ResearchSessionFlowController controller, JObject plan, out string refusal)
        {
            owner = controller;
            if (Running) { refusal = "A diagnostic is already running."; return false; }
            if (!ResearchSessionFlowController.TechnicalBuild || !Idle) { refusal = "view_probe needs the idle .technical app with no participant/session."; return false; }
            if (!owner.origin || !owner.origin.Camera || owner.stations == null || owner.stations.Length < 3) { refusal = "Rig or stations unavailable."; return false; }
            // The XR view never moves, but render settings change while it runs; keep it off a wearer's session.
            if (owner.guards.Any(g => g && g.TrackingValid)) { refusal = "A controller is tracked; view_probe only runs with the headset and controllers set down."; return false; }
            refusal = PlanRefusal(plan, out var presets, out var configs, out int frames, out float yawStep, out float hold);
            if (refusal != null) return false;
            Open("view_probe");
            StartCoroutine(Probe(presets, configs, frames, yawStep, hold));
            return true;
        }

        // Wear-round A/B: live URP scale/MSAA until the next render_set or app restart; the persisted asset is never written.
        public static object SetRender(float scale, int msaa, out string refusal)
        {
            refusal = null;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) { refusal = "URP asset unavailable."; return null; }
            if (scale < MinScale || scale > MaxScale || (msaa != 1 && msaa != 2 && msaa != 4)) { refusal = "scale " + MinScale + "-" + MaxScale + ", msaa 1|2|4."; return null; }
            pipeline.renderScale = scale; pipeline.msaaSampleCount = msaa;
            return new { requested = new { scale, msaa }, note = "readback is from the previous frame; call state after ~1 s for the applied eye size/MSAA" };
        }

        public bool BeginTiming(ResearchSessionFlowController controller, int seconds, out string refusal)
        {
            owner = controller; refusal = null;
            if (Running) { refusal = "A diagnostic is already running."; return false; }
            if (seconds < 5 || seconds > 60) { refusal = "seconds 5-60."; return false; }
            if (owner.RenderComparison && owner.RenderComparison.Running) { refusal = "A render benchmark is running."; return false; }
            Open("frame_timing");
            StartCoroutine(TimingOnly(seconds));
            return true;
        }

        void Open(string kind)
        {
            directory = Path.Combine(Application.persistentDataPath, "ViewDiagnostics", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + kind);
            Directory.CreateDirectory(directory);
            State = "running"; error = null;
        }

        IEnumerator TimingOnly(int seconds)
        {
            yield return Sample(seconds, "workload");
            Write("result.json", new { kind = "frame_timing", seconds, flow_state = owner.Flow?.State.ToString() ?? "ResearcherSetup", participant = owner.Record?.participant_id,
                render = ResearchRenderExperiment.CurrentSettings(), timing = lastTiming, hardware_verified = false });
            State = "completed";
        }

        object lastTiming;
        IEnumerator Sample(float seconds, string label)
        {
            FrameTimingManager.CaptureFrameTimings();
            samples = 0; gcAtStart = GC.CollectionCount(0); allocated = 0;
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            sampling = true;
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
            sampling = false; gcAlloc.Dispose();
            float refresh = Refresh(), budget = 1000f / refresh;
            object Stats(float[] values)
            {
                var v = values.Take(samples).Where(x => x > 0).OrderBy(x => x).ToArray();
                if (v.Length == 0) return new { available = false };
                float P(double q) => v[Math.Min(v.Length - 1, (int)Math.Floor(q * (v.Length - 1)))];
                return new { available = true, n = v.Length, mean = v.Average(), p50 = P(.5), p95 = P(.95), p99 = P(.99), max = v[v.Length - 1], over_budget = v.Count(x => x > budget) };
            }
            File.WriteAllLines(Path.Combine(directory, "timing_" + label + ".csv"), new[] { "interval_ms,cpu_ms,main_ms,render_ms,gpu_ms,gc_collections" }
                .Concat(Enumerable.Range(0, samples).Select(i => interval[i].ToString("F3") + "," + cpu[i].ToString("F3") + "," + main[i].ToString("F3") + "," + render[i].ToString("F3") + "," + gpu[i].ToString("F3") + "," + collections[i])));
            lastTiming = new { label, seconds, ended_utc = DateTime.UtcNow.ToString("O"), frames = samples, refresh_hz = refresh, budget_ms = budget, frame_timing_stats = FrameTimingManager.IsFeatureEnabled(),
                interval_ms = Stats(interval), cpu_ms = Stats(cpu), main_thread_ms = Stats(main), render_thread_ms = Stats(render), gpu_ms = Stats(gpu),
                interval_missed = interval.Take(samples).Count(x => x > budget * 1.5f), gc_collections = GC.CollectionCount(0) - gcAtStart, gc_allocated_bytes = allocated,
                incremental_gc = UnityEngine.Scripting.GarbageCollector.isIncremental };
        }

        static float Refresh()
        {
            var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running && d.TryGetDisplayRefreshRate(out float hz) && hz > 0) return hz;
            return 72f;
        }

        void Update()
        {
            if (!sampling || samples >= TimingCap) return;
            FrameTimingManager.CaptureFrameTimings();
            bool known = FrameTimingManager.GetLatestTimings(1, timing) == 1;
            interval[samples] = Time.unscaledDeltaTime * 1000f;
            cpu[samples] = known ? (float)timing[0].cpuFrameTime : 0; main[samples] = known ? (float)timing[0].cpuMainThreadFrameTime : 0;
            render[samples] = known ? (float)timing[0].cpuRenderThreadFrameTime : 0; gpu[samples] = known ? (float)timing[0].gpuFrameTime : 0;
            if (gcAlloc.Valid) allocated += gcAlloc.LastValue;
            collections[samples] = GC.CollectionCount(0) - gcAtStart;
            samples++;
        }

        IEnumerator Probe(string[] names, (float scale, int msaa)[] configs, int frames, float yawStep, float hold)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float scale = pipeline ? pipeline.renderScale : 1; int msaa = pipeline ? pipeline.msaaSampleCount : 1;
            var log = new List<object>();
            Exception failure = null;
            var routines = new Stack<IEnumerator>();
            routines.Push(ProbeBody(pipeline, names, configs, frames, yawStep, hold, log));
            // Own stack so an exception in a nested step still reaches the restore below.
            while (routines.Count != 0)
            {
                object current;
                try
                {
                    var routine = routines.Peek();
                    if (!routine.MoveNext()) { routines.Pop(); continue; }
                    current = routine.Current;
                    if (current is IEnumerator nested) { routines.Push(nested); continue; }
                }
                catch (Exception ex) { failure = ex; sampling = false; break; }
                yield return current;
            }
            if (probeCamera) Destroy(probeCamera.gameObject);
            if (pipeline) { pipeline.renderScale = scale; pipeline.msaaSampleCount = msaa; }
            yield return new WaitForSecondsRealtime(1.5f);
            var restored = ResearchRenderExperiment.CurrentSettings();
            bool ok = pipeline && Mathf.Abs(pipeline.renderScale - scale) < .001f && pipeline.msaaSampleCount == msaa;
            error = failure?.Message ?? (ok ? null : "Restore readback mismatch.");
            Write("result.json", new { kind = "view_probe", method = "offscreen left-eye camera on device GPU: eye FOV/aspect, eye-buffer pixels x scale, RT MSAA; XR path timed in hold windows",
                presets = names, configs = configs.Select(c => new[] { c.scale, c.msaa }), frames, yaw_step_deg = yawStep, hold_s = hold,
                status = failure == null && ok ? "completed" : "failed", error, restored_render = restored, restore_verified = ok, baseline = new { scale, msaa },
                rig_moved = false, captures = log, hardware_verified = false, wearer_evidence = false });
            State = error == null ? "completed" : "failed";
        }

        Camera probeCamera;
        IEnumerator ProbeBody(UniversalRenderPipelineAsset pipeline, string[] names, (float scale, int msaa)[] configs, int frames, float yawStep, float hold, List<object> log)
        {
            if (!pipeline) throw new InvalidOperationException("URP asset unavailable.");
            var xr = owner.origin.Camera;
            if (!xr.stereoEnabled || XRSettings.eyeTextureWidth <= 0) throw new InvalidOperationException("Running XR eye rendering required.");
            var projection = xr.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            float fovY = 2 * Mathf.Atan(1 / projection.m11) * Mathf.Rad2Deg;
            int baseWidth = Mathf.RoundToInt(XRSettings.eyeTextureWidth / pipeline.renderScale), baseHeight = Mathf.RoundToInt(XRSettings.eyeTextureHeight / pipeline.renderScale);
            probeCamera = new GameObject("ResearchViewProbeCamera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            probeCamera.CopyFrom(xr);
            probeCamera.stereoTargetEye = StereoTargetEyeMask.None; probeCamera.enabled = false;
            probeCamera.fieldOfView = fovY; probeCamera.aspect = (float)baseWidth / baseHeight;
            var xrData = xr.GetUniversalAdditionalCameraData(); var data = probeCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = xrData.renderPostProcessing; data.antialiasing = xrData.antialiasing; data.renderShadows = xrData.renderShadows;
            foreach (var config in configs)
            {
                pipeline.renderScale = config.scale; pipeline.msaaSampleCount = config.msaa;
                yield return new WaitForSecondsRealtime(2f);
                var applied = ResearchRenderExperiment.CurrentSettings();
                string label = "s" + config.scale.ToString("F2") + "_m" + config.msaa;
                lastTiming = null;
                if (hold > 0) yield return Sample(hold, label);
                int width = Mathf.RoundToInt(baseWidth * config.scale), height = Mathf.RoundToInt(baseHeight * config.scale);
                foreach (var name in names)
                {
                    var (eye, target) = Viewpoint(name);
                    for (int i = 0; i < frames; i++)
                    {
                        string file = label + "_" + name + "_" + i.ToString("D2") + ".png";
                        var rotation = Quaternion.AngleAxis(i * yawStep, Vector3.up) * Quaternion.LookRotation(target - eye, Vector3.up);
                        Render(probeCamera, eye, rotation, width, height, config.msaa, Path.Combine(directory, file));
                        log.Add(new { file, preset = name, config = label, yaw_offset_deg = i * yawStep, width, height, msaa = config.msaa, fov_y_deg = fovY,
                            eye = new[] { eye.x, eye.y, eye.z }, target = new[] { target.x, target.y, target.z }, distance_m = Vector3.Distance(eye, target),
                            xr_applied = i == 0 ? applied : null, xr_timing = i == 0 && name == names[0] ? lastTiming : null });
                        yield return null;
                    }
                }
            }
        }

        static void Render(Camera camera, Vector3 eye, Quaternion rotation, int width, int height, int msaa, string path)
        {
            var target = new RenderTexture(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
                { msaaSamples = msaa, sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            // ReadPixels on a Vulkan MSAA target returns zeros; resolve into a single-sample copy first.
            var resolved = new RenderTexture(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
                { msaaSamples = 1, sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear });
            var previous = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                camera.transform.SetPositionAndRotation(eye, rotation);
                camera.targetTexture = target; camera.Render();
                Graphics.Blit(target, resolved);
                RenderTexture.active = resolved; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                target.Release(); resolved.Release(); Destroy(target); Destroy(resolved); Destroy(image);
            }
        }

        // Scene presets stand 1.6 m above their station; "ui" looks at the participant panel from the live head distance.
        (Vector3 eye, Vector3 target) Viewpoint(string preset)
        {
            var spec = Presets.First(p => p.name == preset);
            var station = owner.stations[spec.station];
            if (spec.target == "#canvas")
            {
                // A world-space canvas reads from its -forward side; 0.9 m is the participant panel's working distance.
                var panel = ParticipantCanvas(station);
                return (panel.position - panel.forward * .9f, panel.position);
            }
            return (station.position + Vector3.up * 1.6f, Target(spec.target, station));
        }

        Transform ParticipantCanvas(Transform station)
        {
            var panel = owner.view ? owner.view.mainPanel : null;
            if (!panel || !panel.gameObject.activeInHierarchy) throw new InvalidOperationException("Participant panel is not visible.");
            return panel.transform;
        }

        Vector3 Target(string name, Transform station)
        {
            if (name == null) return new Vector3(station.position.x, station.position.y, station.position.z) + station.forward * 2.5f;
            if (name == "#canvas") return ParticipantCanvas(station).position;
            var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.name == name && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Expected one active '" + name + "', found " + found.Length);
            var renderers = found[0].GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return found[0].position;
            var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.center;
        }

        void Write(string file, object value)
        {
            try { File.WriteAllText(Path.Combine(directory, file), JsonConvert.SerializeObject(value, Formatting.Indented)); }
            catch (Exception ex) { error = "Write failed: " + ex.Message; }
        }
    }
}
#endif
