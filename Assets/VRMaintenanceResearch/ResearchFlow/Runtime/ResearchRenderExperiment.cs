using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Technical A/B/A windows only. Host VrApi logcat (Stale/Tear/App GPU) is the performance
    // measurement; these markers correlate by device UTC/time.
    public sealed class ResearchRenderExperiment : MonoBehaviour
    {
        public const double WarmupSeconds = 5, CaptureSeconds = 30;
        public const int BaselineMsaa = 2; // Android URP Medium asset; 4x (Meta's recommendation) is only measured as condition B
        // Quest 3 scale 1.00 = 1680x1760/eye (Meta render-scale docs). Wear-session ladder: A 1.00, B 1.10, C 1.15, restore 1.00.
        public static readonly float[] ResolutionLadder = { 1f, 1.10f, 1.15f, 1f };
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        ResearchSessionFlowController controller;
        UniversalRenderPipelineAsset pipeline;
        OpenXRSettings settings;
        StreamWriter output;
        string mode, path, participant, outcome = "idle", reason;
        ResearchFlowState sessionState;
        int phase, completedWindows, eyeWidth, eyeHeight, baselineWidth, baselineHeight, comparisonWidth, comparisonHeight, baselineFoveation;
        double phaseStart, captureStart, nextSample, previousUpdate;
        bool restoreRequired, capturing;
        float selectedScale = 1f, maxFrameSeconds;
        float baselineScale = 1f;
        int baselineMsaa = BaselineMsaa;
        int framesSinceSample, gcAtStart, restoreCheckFrames;
        bool? restoreVerified;
        object lastReadback;
        bool unattendedTechnical;
        int[] baselineTargets; // width, height, MSAA per XR render pass at Begin
        int[] phaseTargets, comparisonTargets;
        string baselineFoveationFlags;

        public bool Running { get; private set; }
        public bool? RestoreVerified => restoreVerified;
        public bool Completed => ValidMode(mode) && outcome == "completed_windows" && completedWindows == PhaseCount(mode) && restoreVerified == true;
        public object Status => new
        {
            status = outcome, Running, mode, phase, completed_windows = completedWindows,
            window = capturing ? "capture" : "warmup", reason, path,
            warmup_seconds = WarmupSeconds, capture_seconds = CaptureSeconds,
            baseline_scale = baselineScale, baseline_msaa = baselineMsaa,
            last_readback = lastReadback, restore_verified = restoreVerified, restore_required = restoreRequired, hardware_verified = false
        };

        public static bool ValidMode(string value) => value == "resolution" || value == "predicted_time" || value == "msaa";
        public static bool IsBaseline(float scale, bool predicted, int msaa = BaselineMsaa) =>
            Mathf.Abs(scale - 1f) < .0001f && !predicted && msaa == BaselineMsaa;
        public static bool CaptureReady(double elapsed) => elapsed >= WarmupSeconds;
        public static bool WindowComplete(double capturedSeconds) => capturedSeconds >= CaptureSeconds;
        public static bool OperationalReady(bool floorReady, string error) => floorReady && string.IsNullOrEmpty(error);
        public static bool SupportedScale(float scale) => scale == 1f || scale == 1.10f || scale == 1.15f;
        public static bool SourceSettingsSupported(float scale, bool predicted, int msaa) =>
            SupportedScale(scale) && !predicted && (msaa == 2 || msaa == 4);
        // Actual eye size must follow the requested scale (3% allows runtime alignment); 1.10 and 1.15 are 4.5% apart.
        public static bool DimensionsMatch(int baseline, int actual, float scale) => Mathf.Abs(actual - baseline * scale) <= baseline * scale * .03f;
        public static bool Restored(float urpScale, float targetScale, int urpMsaa, int passMsaa, bool predicted, int width, int height, int baselineWidth, int baselineHeight,
            float baselineScale = 1f, int baselineMsaa = BaselineMsaa) =>
            SourceSettingsSupported(baselineScale, predicted, baselineMsaa) && Mathf.Abs(urpScale - baselineScale) < .001f &&
            Mathf.Abs(targetScale - baselineScale) < .001f && urpMsaa == baselineMsaa && passMsaa == baselineMsaa && width == baselineWidth && height == baselineHeight;
        // Every pass (not only pass 0) must be back at its Begin width/height/MSAA; null = no readback = not restored.
        public static bool PassesRestored(int[] targets, int[] baseline) =>
            targets != null && baseline != null && targets.Length > 0 && targets.Length == baseline.Length && targets.SequenceEqual(baseline);
        public static bool PassDimensionsMatch(int[] targets, int[] baseline)
        {
            if (targets == null || baseline == null || targets.Length == 0 || targets.Length % 3 != 0 || targets.Length != baseline.Length) return false;
            for (int i = 0; i < targets.Length; i += 3)
                if (targets[i] != baseline[i] || targets[i + 1] != baseline[i + 1]) return false;
            return true;
        }
        public static bool DisplayRestored(float viewportScale, float refreshHz, int foveation, int baselineFoveation, string flags, string baselineFlags) =>
            Mathf.Abs(viewportScale - 1f) < .001f && Mathf.Abs(refreshHz - 72) <= .5f && foveation == baselineFoveation && flags == baselineFlags;
        // The restore latch stays set until a native readback confirms the baseline; failed or unknown restores block.
        public static bool MayBegin(bool running, bool restoreRequired, int restoreCheckFrames) => !running && !restoreRequired && restoreCheckFrames == 0;
        public static int PhaseCount(string value) => value == "resolution" ? ResolutionLadder.Length : 3;
        public static float PhaseScale(string value, int index, float selected = 1f) => value == "resolution"
            ? ResolutionLadder[Mathf.Clamp(index, 0, ResolutionLadder.Length - 1)] : value == "msaa" ? selected : 1f;
        public static bool PhasePredicted(string value, int index) => value == "predicted_time" && index == 1;
        public static int PhaseMsaa(string value, int index) => value == "msaa" && index == 1 ? 4 : BaselineMsaa;
        public static bool NativeReady(bool focused, bool valid, bool tracked, bool presentKnown,
            bool present, InputTrackingState tracking, int width, int height, int passes) =>
            focused && valid && tracked && presentKnown && present && width > 0 && height > 0 && passes > 0 &&
            (tracking & (InputTrackingState.Position | InputTrackingState.Rotation)) ==
            (InputTrackingState.Position | InputTrackingState.Rotation);

        public bool Begin(ResearchSessionFlowController owner, string requestedMode, out string error, float selectedResolution = 0, bool isolatedDesk = false)
        {
            error = null;
            if (!MayBegin(Running, restoreRequired, restoreCheckFrames))
            {
                error = Running || restoreCheckFrames > 0 ? "A render experiment is already pending."
                    : "The previous baseline restore was not verified by native readback; restart the technical application.";
                return false;
            }
            if (Application.isEditor || !Application.identifier.EndsWith(".technical", StringComparison.Ordinal))
            { error = "Render experiments require the standalone technical build."; return false; }
            var scenario = owner.GetComponent<ResearchTechnicalScenario>();
            if (isolatedDesk && (!scenario || !scenario.Owns(owner) || !scenario.SimulatedPresence))
            { error = "Desk rendering requires this run's active isolated TEST scenario and explicit simulated presence."; return false; }
            unattendedTechnical = isolatedDesk;
            if (!ValidMode(requestedMode))
            { error = "mode must be resolution, predicted_time or msaa; experiments cannot be combined."; return false; }
            if (requestedMode == "msaa" && !SupportedScale(selectedResolution))
            { error = "MSAA requires an explicitly selected_scale (1.00, 1.10 or 1.15) after the resolution comparison."; return false; }
            selectedScale = requestedMode == "msaa" ? selectedResolution : 1f;
            controller = owner;
            if (!SessionReady()) { error = "An active, unpaused DEV/TEST/PILOT test-data session is required."; return false; }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // The heavy diagnostic capture drops frames by itself (DEV006) and would confound A/B/A.
            var diagnostic = owner.GetComponent<ResearchFlowPhysicalProbe>();
            if (diagnostic && diagnostic.CaptureAlive)
            { error = "Relaunch without the technical diagnostic flag; its capture load confounds render windows."; return false; }
#endif
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            settings = OpenXRSettings.Instance;
            if (!pipeline || !settings || pipeline.supportsHDR || !SourceSettingsSupported(pipeline.renderScale, settings.useOpenXRPredictedTime, pipeline.msaaSampleCount))
            { error = "Source must be a supported render scale, MSAA 2x or 4x, HDR OFF and predicted time OFF."; return false; }
            if (!ReadNative(out var display, out error)) return false;
            if (Mathf.Abs(display.scaleOfAllRenderTargets - pipeline.renderScale) > .001f)
            { error = "Actual XR render-target scale must match the configured source before starting."; return false; }
            if (!display.TryGetDisplayRefreshRate(out float refresh) || Mathf.Abs(refresh - 72) > .5f ||
                Mathf.Abs(display.scaleOfAllViewports - 1f) > .001f)
            { error = "This experiment requires the 72 Hz baseline with viewport scale 1.0."; return false; }
            var targets = PassTargets(display);
            for (int i = 2; i < targets.Length; i += 3)
                if (targets[i] != pipeline.msaaSampleCount) { error = "Every XR render pass must match source MSAA before starting."; return false; }
            mode = requestedMode;
            baselineScale = pipeline.renderScale;
            baselineMsaa = pipeline.msaaSampleCount;
            baselineWidth = XRSettings.eyeTextureWidth;
            baselineHeight = XRSettings.eyeTextureHeight;
            baselineTargets = targets;
            baselineFoveation = Mathf.RoundToInt(display.foveatedRenderingLevel * 100);
            baselineFoveationFlags = display.foveatedRenderingFlags.ToString();
            participant = owner.Record.participant_id;
            sessionState = owner.Flow.State;
            phase = completedWindows = 0;
            comparisonWidth = comparisonHeight = 0;
            phaseTargets = comparisonTargets = null;
            reason = null;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "RenderExperiments");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + mode + ".jsonl");
                output = new StreamWriter(path) { AutoFlush = true };
                restoreRequired = Running = true;
                owner.RenderComparison = this;
                outcome = "pending";
                StartPhase();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Finish("aborted", "Start failed: " + error);
                return false;
            }
        }

        bool SessionReady() => controller && OperationalReady(unattendedTechnical || controller.FloorReady, controller.Error) && controller.Record != null && controller.Record.test_data &&
            ResearchParticipantRegistry.IsTechnical(controller.Record.group) && controller.IsTechnical &&
            controller.Record.session_state == RegistryState.InProgress && controller.LoggerReady &&
            controller.Flow != null && !controller.Flow.Paused && !controller.Transitioning &&
            (!unattendedTechnical || (controller.GetComponent<ResearchTechnicalScenario>() && controller.GetComponent<ResearchTechnicalScenario>().Owns(controller)));

        bool ReadNative(out XRDisplaySubsystem display, out string error)
        {
            display = null;
            error = null;
            SubsystemManager.GetSubsystems(displays);
            foreach (var candidate in displays)
                if (candidate.running)
                {
                    if (display != null) { error = "Multiple running XR displays; benchmark is ambiguous."; return false; }
                    display = candidate;
                }
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
            bool known = hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present);
            hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState tracking);
            int passes = display?.GetRenderPassCount() ?? 0;
            bool allocatedDesk = unattendedTechnical && Application.isFocused && hmd.isValid && known && present &&
                XRSettings.eyeTextureWidth > 0 && XRSettings.eyeTextureHeight > 0 && passes > 0;
            if (!XRSettings.enabled || !(allocatedDesk || NativeReady(Application.isFocused, hmd.isValid, tracked, known, present,
                tracking, XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, passes)))
            { error = "Native focused/tracked/present HMD and allocated active eye rendering are required."; return false; }
            for (int i = 0; i < passes; i++)
            {
                display.GetRenderPass(i, out var pass);
                if (pass.renderTargetDesc.width <= 0 || pass.renderTargetDesc.height <= 0)
                { error = "XR render pass has no allocated eye target."; return false; }
            }
            return true;
        }

        void StartPhase()
        {
            pipeline.renderScale = PhaseScale(mode, phase, selectedScale);
            settings.useOpenXRPredictedTime = PhasePredicted(mode, phase);
            pipeline.msaaSampleCount = PhaseMsaa(mode, phase);
            phaseStart = Time.realtimeSinceStartupAsDouble;
            previousUpdate = phaseStart;
            gcAtStart = GC.CollectionCount(0);
            nextSample = phaseStart;
            capturing = false;
            Write("phase_begin", null);
        }

        void Update()
        {
            if (!Running) { if (restoreCheckFrames > 0 && --restoreCheckFrames == 0) VerifyRestore(); return; }
            try
            {
                if (!SessionReady() || controller.Record.participant_id != participant || controller.Flow.State != sessionState)
                { Finish("aborted", "Session state changed; matched windows are invalid."); return; }
                if (GraphicsSettings.currentRenderPipeline != pipeline)
                { Finish("aborted", "Render pipeline changed."); return; }
                if (!ReadNative(out var display, out var error))
                { Finish("aborted", error); return; }
                if (!display.TryGetDisplayRefreshRate(out float refresh) || Mathf.Abs(refresh - 72) > .5f ||
                    Mathf.Abs(display.scaleOfAllViewports - 1f) > .001f)
                { Finish("aborted", "Refresh rate or viewport scale changed from the controlled baseline."); return; }
                if (Mathf.RoundToInt(display.foveatedRenderingLevel * 100) != baselineFoveation || display.foveatedRenderingFlags.ToString() != baselineFoveationFlags)
                { Finish("aborted", "Foveation level changed; windows no longer share the same foveation state."); return; }
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - previousUpdate > 1)
                { Finish("aborted", "Sampling gap exceeded one second; continuous active rendering was not observed."); return; }
                previousUpdate = now;
                framesSinceSample++; maxFrameSeconds = Mathf.Max(maxFrameSeconds, Time.unscaledDeltaTime);
                double elapsed = now - phaseStart;
                if (CaptureReady(elapsed))
                {
                    if (Mathf.Abs(pipeline.renderScale - PhaseScale(mode, phase, selectedScale)) > .001f ||
                        Mathf.Abs(display.scaleOfAllRenderTargets - PhaseScale(mode, phase, selectedScale)) > .001f ||
                        settings.useOpenXRPredictedTime != PhasePredicted(mode, phase) ||
                        pipeline.msaaSampleCount != PhaseMsaa(mode, phase) || ActualMsaa(display) != PhaseMsaa(mode, phase))
                    { Finish("aborted", "Requested render settings did not settle or changed during capture."); return; }
                    if (!capturing)
                    {
                        capturing = true;
                        captureStart = now;
                        eyeWidth = XRSettings.eyeTextureWidth;
                        eyeHeight = XRSettings.eyeTextureHeight;
                        float expected = PhaseScale(mode, phase, selectedScale) / baselineScale;
                        if (!DimensionsMatch(baselineWidth, eyeWidth, expected) || !DimensionsMatch(baselineHeight, eyeHeight, expected))
                        { Finish("aborted", "Actual eye dimensions do not match the requested scale."); return; }
                        phaseTargets = PassTargets(display);
                        if (phaseTargets.Length != baselineTargets.Length)
                        { Finish("aborted", "XR render pass count changed."); return; }
                        for (int i = 0; i < phaseTargets.Length; i += 3)
                            if (!DimensionsMatch(baselineTargets[i], phaseTargets[i], expected) || !DimensionsMatch(baselineTargets[i + 1], phaseTargets[i + 1], expected)
                                || phaseTargets[i + 2] != PhaseMsaa(mode, phase))
                            { Finish("aborted", "Every XR render pass must match requested dimensions and MSAA."); return; }
                        if (phase == 0) { comparisonWidth = eyeWidth; comparisonHeight = eyeHeight; comparisonTargets = phaseTargets; }
                        else if (mode != "resolution" || phase == PhaseCount(mode) - 1)
                        {
                            if (eyeWidth != comparisonWidth || eyeHeight != comparisonHeight || !PassDimensionsMatch(phaseTargets, comparisonTargets))
                            { Finish("aborted", "Matched comparison windows changed eye or render-pass dimensions."); return; }
                        }
                        Write("capture_begin", Readback(display));
                    }
                    if (eyeWidth != XRSettings.eyeTextureWidth || eyeHeight != XRSettings.eyeTextureHeight)
                    { Finish("aborted", "Actual eye dimensions changed within a capture window."); return; }
                    if (!PassSettingsUnchanged(display))
                    { Finish("aborted", "XR render-pass dimensions or MSAA changed within a capture window."); return; }
                }
                if (now >= nextSample)
                {
                    nextSample = now + 1;
                    Write("sample", Readback(display));
                }
                if (!capturing || !WindowComplete(now - captureStart)) return;
                completedWindows++;
                Write("capture_complete", Readback(display));
                if (phase == PhaseCount(mode) - 1) Finish("completed_windows", "Measurements require analysis; no physical pass is inferred.");
                else { phase++; StartPhase(); }
            }
            catch (Exception ex) { Finish("aborted", ex.Message); }
        }

        static int[] PassTargets(XRDisplaySubsystem display)
        {
            var targets = new int[display.GetRenderPassCount() * 3];
            for (int i = 0; i < targets.Length / 3; i++)
            {
                display.GetRenderPass(i, out var pass);
                targets[3 * i] = pass.renderTargetDesc.width; targets[3 * i + 1] = pass.renderTargetDesc.height; targets[3 * i + 2] = pass.renderTargetDesc.msaaSamples;
            }
            return targets;
        }

        static int ActualMsaa(XRDisplaySubsystem display)
        {
            display.GetRenderPass(0, out var pass);
            return pass.renderTargetDesc.msaaSamples;
        }

        bool PassSettingsUnchanged(XRDisplaySubsystem display)
        {
            if (phaseTargets == null || display.GetRenderPassCount() * 3 != phaseTargets.Length) return false;
            for (int i = 0; i < phaseTargets.Length; i += 3)
            {
                display.GetRenderPass(i / 3, out var pass);
                if (pass.renderTargetDesc.width != phaseTargets[i] || pass.renderTargetDesc.height != phaseTargets[i + 1]
                    || pass.renderTargetDesc.msaaSamples != phaseTargets[i + 2]) return false;
            }
            return true;
        }

        // On-demand configuration evidence; an allocated target never implies wearer acceptance.
        public static object CurrentSettings()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var settings = OpenXRSettings.Instance;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            var running = displays.Where(d => d.running).ToArray();
            var display = running.Length == 1 ? running[0] : null;
            float refresh = 0;
            bool refreshKnown = display != null && display.TryGetDisplayRefreshRate(out refresh);
            return new
            {
                urp_scale = pipeline ? (float?)pipeline.renderScale : null, urp_msaa = pipeline ? (int?)pipeline.msaaSampleCount : null,
                hdr = pipeline ? (bool?)pipeline.supportsHDR : null, predicted_time = settings ? (bool?)settings.useOpenXRPredictedTime : null,
                xr_enabled = XRSettings.enabled, running_displays = running.Length,
                actual_target_scale = display?.scaleOfAllRenderTargets, viewport_scale = display?.scaleOfAllViewports,
                xr_render_viewport_scale = display != null && XRSettings.enabled ? (float?)XRSettings.renderViewportScale : null,
                xr_applied_viewport_scale = display != null && XRSettings.enabled ? (float?)XRSettings.appliedRenderViewportScale : null,
                viewport_sample_timing = display != null && XRSettings.enabled ? "gameplay_previous_frame" : "unavailable",
                eye_width = XRSettings.eyeTextureWidth, eye_height = XRSettings.eyeTextureHeight,
                pass_targets = display != null ? PassTargets(display) : null, refresh_hz = refreshKnown ? (float?)refresh : null,
                foveation_level = display?.foveatedRenderingLevel, foveation_flags = display?.foveatedRenderingFlags.ToString(),
                hardware_verified = false
            };
        }

        object Readback(XRDisplaySubsystem display)
        {
            display.GetRenderPass(0, out var pass);
            display.TryGetDisplayRefreshRate(out float refresh);
            lastReadback = new
            {
                utc = DateTime.UtcNow.ToString("O"), device_realtime = Time.realtimeSinceStartupAsDouble,
                urp_scale = pipeline.renderScale, predicted_time = settings.useOpenXRPredictedTime, urp_msaa = pipeline.msaaSampleCount,
                actual_target_scale = display.scaleOfAllRenderTargets, viewport_scale = display.scaleOfAllViewports,
                xr_render_viewport_scale = XRSettings.renderViewportScale, xr_applied_viewport_scale = XRSettings.appliedRenderViewportScale,
                // Unity reports the previous rendered frame when queried during gameplay Update.
                viewport_sample_timing = "gameplay_previous_frame",
                eye_width = XRSettings.eyeTextureWidth, eye_height = XRSettings.eyeTextureHeight,
                target_width = pass.renderTargetDesc.width, target_height = pass.renderTargetDesc.height,
                pass_targets = PassTargets(display),
                msaa = pass.renderTargetDesc.msaaSamples, refresh_hz = refresh,
                foveation_level = display.foveatedRenderingLevel, foveation_flags = display.foveatedRenderingFlags.ToString(),
                app_gpu_ms = UnityEngine.XR.Provider.XRStats.TryGetStat(display, "perfmetrics.appgputime", out float gpu) ? (float?)gpu : null,
                dropped_frames = display.TryGetDroppedFrameCount(out int dropped) ? (int?)dropped : null,
                // Unity-side cadence since the previous sample; GC count shows whether a collection hit this phase.
                frames = framesSinceSample, max_frame_ms = maxFrameSeconds * 1000, gc_collections_in_phase = GC.CollectionCount(0) - gcAtStart,
                head = Head(),
                camera_view = controller?.origin?.Camera ? new { position = new[] { controller.origin.Camera.transform.position.x, controller.origin.Camera.transform.position.y, controller.origin.Camera.transform.position.z },
                    rotation = new[] { controller.origin.Camera.transform.rotation.x, controller.origin.Camera.transform.rotation.y, controller.origin.Camera.transform.rotation.z, controller.origin.Camera.transform.rotation.w },
                    source = "rendered camera transform; not native sensor pose" } : null,
                performance_source = "Host VrApi logcat stream; correlate device UTC/realtime"
            };
            framesSinceSample = 0; maxFrameSeconds = 0;
            return lastReadback;
        }

        void Write(string eventName, object readback)
        {
            output?.WriteLine(JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow.ToString("O"), device_realtime = Time.realtimeSinceStartupAsDouble,
                frame = Time.frameCount, build_guid = Application.buildGUID, @event = eventName,
                mode, phase, selected_scale = selectedScale,
                condition = mode == "resolution" ? (phase == PhaseCount(mode) - 1 ? "A_repeat" : ((char)('A' + phase)).ToString()) : phase == 1 ? "B" : "A",
                window = capturing ? "capture" : "warmup",
                phase_elapsed = Time.realtimeSinceStartupAsDouble - phaseStart,
                capture_elapsed = capturing ? Time.realtimeSinceStartupAsDouble - captureStart : 0,
                status = outcome, completed_windows = completedWindows, reason,
                participant_id = participant, task_state = sessionState.ToString(), readback,
                simulated_presence = unattendedTechnical, scope = unattendedTechnical ? "native_allocated_desk_rendering_no_wearer" : "native_tracked_rendering_no_wearer_acceptance",
                hardware_verified = false
            }));
        }

        public void Cancel() { if (Running) Finish("aborted", "Researcher cancelled."); }

        void Finish(string result, string detail)
        {
            Running = false;
            outcome = result;
            reason = detail;
            try
            {
                if (restoreRequired)
                {
                    // Always attempt every restore, even when one native setter fails.
                    try { if (pipeline) pipeline.renderScale = baselineScale; }
                    finally
                    {
                        try { if (settings) settings.useOpenXRPredictedTime = false; }
                        finally { if (pipeline) pipeline.msaaSampleCount = baselineMsaa; }
                    }
                    // restoreRequired stays latched: only VerifyRestore's native readback may clear it.
                    restoreVerified = null; restoreCheckFrames = 3; // native targets reallocate on a later frame; read back then
                }
                Write("experiment_end", new { baseline_scale = pipeline ? (float?)pipeline.renderScale : null,
                    predicted_time = settings ? (bool?)settings.useOpenXRPredictedTime : null,
                    baseline_msaa = pipeline ? (int?)pipeline.msaaSampleCount : null });
            }
            catch (Exception ex)
            {
                outcome = "restore_or_log_failed";
                reason += " " + ex.Message;
                Debug.LogError("RENDER_EXPERIMENT_FAILED " + reason);
            }
            finally { if (restoreCheckFrames == 0) CloseLog(); }
        }

        void CloseLog()
        {
            var writer = output;
            output = null;
            try { writer?.Dispose(); }
            catch (Exception ex)
            {
                outcome = "log_close_failed";
                reason += " " + ex.Message;
                Debug.LogError("RENDER_EXPERIMENT_FAILED " + reason);
            }
        }

        // The windows only count if the native eye buffer is back at the baseline, not just the requested settings.
        void VerifyRestore()
        {
            try
            {
                XRDisplaySubsystem display = null;
                int runningDisplays = 0;
                SubsystemManager.GetSubsystems(displays);
                foreach (var candidate in displays) if (candidate.running) { display = candidate; runningDisplays++; }
                int passMsaa = display != null && display.GetRenderPassCount() > 0 ? ActualMsaa(display) : -1;
                int[] targets = display != null ? PassTargets(display) : null;
                float refresh = 0; bool refreshKnown = display != null && display.TryGetDisplayRefreshRate(out refresh);
                int foveation = display != null ? Mathf.RoundToInt(display.foveatedRenderingLevel * 100) : -1;
                string foveationFlags = display?.foveatedRenderingFlags.ToString();
                restoreVerified = pipeline && settings && runningDisplays == 1 && Restored(pipeline.renderScale, display.scaleOfAllRenderTargets, pipeline.msaaSampleCount, passMsaa,
                    settings.useOpenXRPredictedTime, XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, baselineWidth, baselineHeight, baselineScale, baselineMsaa)
                    && PassesRestored(targets, baselineTargets) && refreshKnown
                    && DisplayRestored(display.scaleOfAllViewports, refresh, foveation, baselineFoveation, foveationFlags, baselineFoveationFlags);
                if (restoreVerified == true) restoreRequired = false;
                Write("restore_readback", new { restored = restoreVerified, restore_required = restoreRequired, urp_scale = pipeline ? (float?)pipeline.renderScale : null, actual_target_scale = display?.scaleOfAllRenderTargets,
                    urp_msaa = pipeline ? (int?)pipeline.msaaSampleCount : null, msaa = passMsaa, predicted_time = settings ? (bool?)settings.useOpenXRPredictedTime : null,
                    eye_width = XRSettings.eyeTextureWidth, eye_height = XRSettings.eyeTextureHeight, baseline_eye = new[] { baselineWidth, baselineHeight },
                    pass_targets = targets, baseline_pass_targets = baselineTargets, viewport_scale = display?.scaleOfAllViewports, refresh_hz = refreshKnown ? (float?)refresh : null,
                    xr_render_viewport_scale = display != null && XRSettings.enabled ? (float?)XRSettings.renderViewportScale : null,
                    xr_applied_viewport_scale = display != null && XRSettings.enabled ? (float?)XRSettings.appliedRenderViewportScale : null,
                    viewport_sample_timing = display != null && XRSettings.enabled ? "gameplay_previous_frame" : "unavailable",
                    foveation_level = foveation, baseline_foveation = baselineFoveation, foveation_flags = foveationFlags, baseline_foveation_flags = baselineFoveationFlags });
                if (restoreVerified != true) { outcome = "restore_not_verified"; Debug.LogError($"RENDER_EXPERIMENT_FAILED baseline readback does not match {baselineScale:F2} / MSAA {baselineMsaa} / predicted off"); }
            }
            catch (Exception ex) { restoreVerified = false; outcome = "restore_not_verified"; reason += " " + ex.Message; Debug.LogError("RENDER_EXPERIMENT_FAILED " + ex.Message); }
            finally { CloseLog(); }
        }

        static object Head()
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!hmd.isValid || !hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return null;
            if (!hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p) || !hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion q)) return null;
            var e = q.eulerAngles; // primitives only: Newtonsoft recurses through Vector3/Quaternion
            return new { position = new[] { (float)Math.Round(p.x, 3), (float)Math.Round(p.y, 3), (float)Math.Round(p.z, 3) }, yaw = (float)Math.Round(e.y, 1), pitch = (float)Math.Round(e.x, 1) };
        }

        void OnDisable() { if (Running || restoreRequired) Finish("aborted", "Experiment disabled."); if (restoreCheckFrames > 0) { restoreCheckFrames = 0; CloseLog(); } }
        void OnApplicationFocus(bool focused) { if (!focused && Running) Finish("aborted", "Application focus lost."); }
        void OnApplicationQuit() { if (Running || restoreRequired) Finish("aborted", "Application exiting."); if (restoreCheckFrames > 0) { restoreCheckFrames = 0; CloseLog(); } }
    }
}
