#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Technical diagnostic capture. Opt-in technical APK only; no participant debug UI.
    public sealed class ResearchFlowPhysicalProbe : MonoBehaviour
    {
        ResearchSessionFlowController flow;
        StreamWriter output;
        public bool CaptureAlive => isActiveAndEnabled && output != null;
        float nextSample;
        float nextRenderingSample;
        object rendering;
        readonly string captureId = Guid.NewGuid().ToString("N");
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        readonly FrameTiming[] frameTimings = new FrameTiming[1];
        static readonly System.Reflection.FieldInfo trackerActive = typeof(ActiveStateTracker).GetField("_active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Collider[] workSurfaces;
        Transform environment;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();

        // Normal research mode = no flag = no capture. The heavy 4 Hz/10 Hz synchronous capture
        // measurably drops frames (DEV006: Stale 5.1/s ON vs 0.03/s OFF), so diagnostic mode is
        // explicit and lasts one launch: Install consumes the flag, a forgotten flag cannot persist.
        public const string DiagnosticFlag = "technical-diagnostic.enabled";
        public static bool ShouldInstall(bool editor, string identifier, bool flagPresent) =>
            !editor && identifier == "th.ac.tmu.vr.researchflow.quest3.technical" && flagPresent;
        // Pilot sessions are participant use; only DEV/TEST (or no session yet) may be captured.
        public static bool AllowedFor(ParticipantGroup? group) => group == null || group == ParticipantGroup.Dev || group == ParticipantGroup.Test;
        HandsLabGrabProbe grabProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            string flag = Path.Combine(Application.persistentDataPath, DiagnosticFlag);
            if (!ShouldInstall(Application.isEditor, Application.identifier, File.Exists(flag))) return;
            var owner = FindFirstObjectByType<ResearchSessionFlowController>();
            if (!owner) return;
            File.Delete(flag);
            owner.gameObject.AddComponent<ResearchFlowPhysicalProbe>();
        }

        void Start()
        {
            flow = GetComponent<ResearchSessionFlowController>();
            environment = gameObject.scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment_V2")?.transform;
            workSurfaces = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.name == "Training_Practice_A" || c.name == "Computer_Main" || c.name == "Fan_Main").ToArray();
            string directory = Path.Combine(Application.persistentDataPath, "PhysicalBaseline", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(directory);
            output = new StreamWriter(Path.Combine(directory, "height.jsonl")) { AutoFlush = true };
            (grabProbe = gameObject.AddComponent<HandsLabGrabProbe>()).Initialize(flow.guards, flow.trainingObjects, directory,
                () => flow.Transitioning || flow.guards.Any(g => g.SystemCancelling));
            Debug.Log("TECHNICAL_DIAGNOSTIC_CAPTURE " + directory);
            // Capture only: never create a participant, advance the flow, or hide its UI.
        }

        void LateUpdate()
        {
            if (flow && !AllowedFor(flow.Record?.group))
            {
                Debug.Log("TECHNICAL_DIAGNOSTIC_STOPPED group=" + flow.Record.group);
                Destroy(grabProbe); output?.Dispose(); output = null; enabled = false; return;
            }
            if (output == null || !flow || !flow.origin || Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + .25f;
            try
            {
                // ponytail: cap this temporary capture at 32 MiB; restart for a fresh capture.
                if (output.BaseStream.Length >= 32 * 1024 * 1024) { output.Dispose(); output = null; return; }
                var origin = flow.origin;
                var head = origin.Camera.transform;
                if (Time.unscaledTime >= nextRenderingSample)
                {
                    nextRenderingSample = Time.unscaledTime + 1f;
                    // A failed optional provider query must not stop the floor/controller capture.
                    try { rendering = RenderingState(origin.Camera); }
                    catch (Exception ex) { rendering = new { capture_id = captureId, available = false, error = ex.Message }; }
                }
                SubsystemManager.GetSubsystems(inputs);
                var hits = Physics.RaycastAll(head.position, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore)
                    .OrderBy(h => h.distance).ToArray();
                var floor = hits.Where(h => h.collider.name.EndsWith("_Floor", StringComparison.Ordinal) || h.collider.name == "Floor").ToArray();
                float? floorY = floor.Length > 0 ? floor[0].point.y : (float?)null;
                var floorRenderer = floor.Length > 0 ? floor[0].collider.GetComponent<Renderer>() : null;
                var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool headTracked);
                bool hasHeadPose = hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPose);
                bool hasHeadRotation = hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation);
                bool hasHeadTrackingState = hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState headTrackingState);
                bool hasUserPresence = hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool userPresent);
                output.WriteLine(JsonConvert.SerializeObject(new
                {
                    capture_id = captureId, utc = DateTime.UtcNow.ToString("O"), time = Time.realtimeSinceStartupAsDouble,
                    focused = Application.isFocused, hardware_verified = false,
                    rendering,
                    tracking_origin_mode = inputs.Select(s => new { s.running, mode = s.GetTrackingOriginMode().ToString(), supported = s.GetSupportedTrackingOriginModes().ToString() }).ToArray(),
                    requested_mode = origin.RequestedTrackingOriginMode.ToString(), origin_current_mode = origin.CurrentTrackingOriginMode.ToString(),
                    xr_origin_position = V(origin.transform.position), xr_origin_scale = V(origin.transform.lossyScale),
                    xr_origin_rotation = Q(origin.transform.rotation),
                    camera_local_position = V(head.localPosition), camera_world_position = V(head.position),
                    camera_local_rotation = Q(head.localRotation), camera_world_rotation = Q(head.rotation), camera_lossy_scale = V(head.lossyScale),
                    camera_floor_offset_local_position = V(origin.CameraFloorOffsetObject.transform.localPosition),
                    camera_floor_offset_world_position = V(origin.CameraFloorOffsetObject.transform.position),
                    camera_floor_offset_local_rotation = Q(origin.CameraFloorOffsetObject.transform.localRotation),
                    camera_floor_offset_world_rotation = Q(origin.CameraFloorOffsetObject.transform.rotation),
                    camera_floor_offset_local_scale = V(origin.CameraFloorOffsetObject.transform.localScale),
                    camera_floor_offset_lossy_scale = V(origin.CameraFloorOffsetObject.transform.lossyScale),
                    environment_root = environment ? new { position = V(environment.position), rotation = Q(environment.rotation), local_scale = V(environment.localScale), lossy_scale = V(environment.lossyScale) } : null,
                    nearest_downward_hit = hits.Select(h => new { collider = h.collider.name, point = V(h.point), h.distance }).FirstOrDefault(),
                    hmd_valid = hmd.isValid, hmd_tracked = headTracked, hmd_device_position = hasHeadPose ? V(headPose) : null,
                    hmd_position_available = hasHeadPose, hmd_tracking_state_available = hasHeadTrackingState,
                    hmd_tracking_state = hasHeadTrackingState ? headTrackingState.ToString() : null,
                    hmd_user_presence_available = hasUserPresence, hmd_user_present = hasUserPresence ? (bool?)userPresent : null,
                    hmd_rotation_available = hasHeadRotation, hmd_device_rotation = hasHeadRotation ? Q(headRotation) : null,
                    floor_hit_y = floorY, floor_collider = floor.Length > 0 ? floor[0].collider.name : null,
                    named_floor_renderer = floorRenderer ? new { floorRenderer.name, floorRenderer.enabled, active = floorRenderer.gameObject.activeInHierarchy,
                        bounds_min = V(floorRenderer.bounds.min), bounds_max = V(floorRenderer.bounds.max), lossy_scale = V(floorRenderer.transform.lossyScale) } : null,
                    measured_eye_height = floorY.HasValue ? head.position.y - floorY : null,
                    controllers = flow.guards.Select(g => ControllerPose(g)).ToArray(),
                    flow_state = flow.Flow?.State.ToString(), paused = flow.Flow?.Paused, can_interact = flow.Flow?.CanInteract,
                    flow.DeviceReady, flow.Transitioning, flow.Error,
                    panels = flow.view ? new[] { flow.view.mainPanel, flow.view.referencePanel }.Where(p => p).Select(PanelState).ToArray() : null,
                    work_surfaces = workSurfaces.Select(c => new { c.name, top_y = c.bounds.max.y, active = c.gameObject.activeInHierarchy }).ToArray(),
                    objects = flow.trainingObjects.Select(o => new { o.stableId, position = V(o.transform.position), o.IsHeld,
                        grabbable_enabled = o.grabbable.enabled, active = o.gameObject.activeInHierarchy,
                        is_kinematic = o.body.isKinematic, use_gravity = o.body.useGravity }).ToArray()
                }));
            }
            catch (Exception ex)
            {
                Debug.LogError("PHYSICAL_BASELINE_FAILED " + ex);
                output?.Dispose(); output = null; enabled = false;
            }
        }

        static object PanelState(ResearchPanelMotion panel) => new
        {
            panel.panelId, active = panel.gameObject.activeInHierarchy, parent = panel.transform.parent ? panel.transform.parent.name : null,
            position = V(panel.transform.position), rotation = Q(panel.transform.rotation),
            recenter_pending = panel.RecenterPending, placement_blocked = panel.PlacementBlocked, last_attempt_head_pose_ready = panel.LastHeadPoseReady,
            last_attempt_presence_known = panel.LastPresenceKnown, last_attempt_user_present = panel.LastPresenceKnown ? (bool?)panel.LastUserPresent : null,
            last_placement_frame = panel.LastPlacementFrame,
            last_placement_head_position = panel.LastPlacementFrame >= 0 ? V(panel.LastPlacementHeadPosition) : null
        };

        object ControllerPose(HandsLabInputGuard guard)
        {
            var device = InputDevices.GetDeviceAtXRNode(guard.side == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand);
            bool hasPose = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
            bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
            string profile = null, profileError = null;
            if (!Application.isEditor && XRSettings.enabled)
                try { profile = PhysicalProbeOpenXRReadback.Profile(guard.side); }
                catch (Exception ex) { profileError = ex.Message; }
            return new { side = guard.side.ToString(), device_valid = device.isValid, tracked,
                device_name = device.name, characteristics = device.characteristics.ToString(), input_source = guard.Mode.ToString(),
                position_available = hasPose, rotation_available = hasRotation,
                tracking_space_rotation = hasRotation ? Q(rotation) : null,
                interaction_profile = profile, interaction_profile_error = profileError,
                tracking_space_position = hasPose ? V(position) : null,
                controller_world_position = hasPose ? V(flow.origin.CameraFloorOffsetObject.transform.TransformPoint(position)) : null,
                mode = guard.Mode.ToString(), guard.TrackingValid, guard.WaitingForNeutral, guard.InteractionsAllowed, guard.LastReason,
                guard.Suspended, guard.SuspensionReason,
                interactionRoot_active = guard.interactionsRoot.activeInHierarchy,
                active_state_trackers = guard.interactionsRoot.GetComponentsInChildren<ActiveStateTracker>(true)
                    .Select(t => new { t.name, t.enabled, active_in_hierarchy = t.gameObject.activeInHierarchy, applied_active = trackerActive?.GetValue(t) }).ToArray() };
        }

        object RenderingState(Camera camera)
        {
            if (!camera) return new { capture_id = captureId, available = false, error = "camera_unavailable", displays = Array.Empty<object>() };
            SubsystemManager.GetSubsystems(displays);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            var settings = OpenXRSettings.Instance;
            var descriptor = XRSettings.eyeTextureDesc;
            double timingQueryRealtime = Time.realtimeSinceStartupAsDouble;
            int timingQueryFrame = Time.frameCount;
            bool timingEnabled = FrameTimingManager.IsFeatureEnabled();
            uint timingCount = timingEnabled ? FrameTimingManager.GetLatestTimings(1, frameTimings) : 0;
            if (timingEnabled) FrameTimingManager.CaptureFrameTimings();
            ulong? frequency = null;
            string frequencyStatus = timingEnabled ? "unavailable" : "feature_disabled", frequencyError = null;
            if (timingEnabled)
                try
                {
                    frequency = FrameTimingManager.GetCpuTimerFrequency();
                    frequencyStatus = frequency == 0 ? "unsupported_zero" : "available";
                }
                catch (Exception ex) { frequencyStatus = "error"; frequencyError = ex.Message; }
            return new
            {
                time = Time.realtimeSinceStartupAsDouble, frame = Time.frameCount, Application.buildGUID, Application.unityVersion,
                capture_id = captureId,
                camera_route = new { camera.name, instance_id = camera.GetInstanceID(), scene_path = camera.gameObject.scene.path,
                    camera.enabled, active = camera.gameObject.activeInHierarchy, camera_type = camera.cameraType.ToString(),
                    clear_flags = camera.clearFlags.ToString(), background_rgba = new[] { camera.backgroundColor.r, camera.backgroundColor.g, camera.backgroundColor.b, camera.backgroundColor.a },
                    camera.stereoEnabled, stereo_target_eye = camera.stereoTargetEye.ToString(), camera.targetDisplay, camera.depth, camera.cullingMask,
                    target_texture_instance_id = camera.targetTexture ? (int?)camera.targetTexture.GetInstanceID() : null,
                    target_texture_name = camera.targetTexture ? camera.targetTexture.name : null,
                    world_position = V(camera.transform.position), world_rotation = Q(camera.transform.rotation) },
                xr_display_available = displays.Any(d => d != null && d.running),
                quality_level = QualitySettings.GetQualityLevel(), quality_name = QualitySettings.names[QualitySettings.GetQualityLevel()],
                xr_enabled = XRSettings.enabled, xr_eye_texture_resolution_scale = XRSettings.eyeTextureResolutionScale,
                xr_render_viewport_scale = XRSettings.renderViewportScale, xr_applied_viewport_scale = XRSettings.appliedRenderViewportScale,
                eye_texture_width = XRSettings.eyeTextureWidth, eye_texture_height = XRSettings.eyeTextureHeight,
                eye_texture_msaa = descriptor.msaaSamples, eye_texture_format = descriptor.graphicsFormat.ToString(),
                eye_texture_dynamic_scale = descriptor.useDynamicScale,
                scalable_buffer_width = ScalableBufferManager.widthScaleFactor, scalable_buffer_height = ScalableBufferManager.heightScaleFactor,
                urp_asset = urp ? urp.name : null, urp_render_scale = urp ? (float?)urp.renderScale : null,
                msaa = urp ? (int?)urp.msaaSampleCount : null, hdr = urp ? (bool?)urp.supportsHDR : null,
                camera.allowHDR, camera.allowMSAA, camera.allowDynamicResolution, camera.nearClipPlane, camera.farClipPlane,
                post_processing = cameraData ? (bool?)cameraData.renderPostProcessing : null, post_aa = cameraData ? cameraData.antialiasing.ToString() : null,
                graphics_api = SystemInfo.graphicsDeviceType.ToString(), SystemInfo.graphicsDeviceVersion,
                stereo_rendering_mode = XRSettings.stereoRenderingMode.ToString(),
                openxr_render_mode = settings ? settings.renderMode.ToString() : null,
                openxr_user_presence_extension_enabled = !Application.isEditor && XRSettings.enabled ? (bool?)OpenXRRuntime.IsExtensionEnabled("XR_EXT_user_presence") : null,
                latency_optimization = settings ? settings.latencyOptimization.ToString() : null,
                predicted_time = settings ? (bool?)settings.useOpenXRPredictedTime : null,
                symmetric_projection_setting = settings ? (bool?)settings.symmetricProjection : null,
                multiview_regions_setting = settings ? settings.multiviewRenderRegionsOptimizationMode.ToString() : null,
                foveation_api = settings ? settings.foveatedRenderingApi.ToString() : null,
                subsampled_layout = FoveatedRenderingFeature.isSubsampledLayoutEnabled,
                automatic_dynamic_resolution_feature = settings ? (bool?)(settings.GetFeature<AutomaticDynamicResolutionFeature>()?.enabled ?? false) : null,
                enabled_openxr_features = settings ? settings.GetFeatures<OpenXRFeature>().Where(f => f.enabled).Select(f => f.GetType().Name).ToArray() : Array.Empty<string>(),
                // OpenXR's normalized foveation getter exposes no dynamic-FFR/effective-level readback.
                dynamic_foveation = "UNKNOWN", effective_ffr_level = "UNKNOWN",
                timing_enabled = timingEnabled, timing_samples = timingCount,
                frame_timing_metadata = new
                {
                    source = "UnityEngine.FrameTimingManager.GetLatestTimings",
                    query_realtime_since_startup_s = timingQueryRealtime, query_frame = timingQueryFrame,
                    sample_status = !timingEnabled ? "feature_disabled" : timingCount > 0 ? "sample_returned" : "no_sample_returned",
                    frame_start_timestamp_cpu_ticks = timingCount > 0 ? (ulong?)frameTimings[0].frameStartTimestamp : null,
                    cpu_timer_frequency_hz = frequency, cpu_timer_frequency_query_status = frequencyStatus,
                    cpu_timer_frequency_error = frequencyError, sample_age_s = (double?)null,
                    freshness_status = "unestablished_no_clock_alignment"
                },
                cpu_main_ms = timingCount > 0 ? (double?)frameTimings[0].cpuMainThreadFrameTime : null,
                cpu_render_ms = timingCount > 0 ? (double?)frameTimings[0].cpuRenderThreadFrameTime : null,
                frame_timing_gpu_ms = timingCount > 0 ? (double?)frameTimings[0].gpuFrameTime : null,
                displays = displays.Select(d => DisplayState(d, camera)).ToArray()
            };
        }

        static object DisplayState(XRDisplaySubsystem display, Camera camera)
        {
            if (display == null || !display.running || !camera)
                return new { running = display?.running ?? false, render_parameters_available = false,
                    error = display == null ? "display_unavailable" : !display.running ? "display_not_running" : "camera_unavailable",
                    render_passes = Array.Empty<object>() };
            double counterQueryRealtime = Time.realtimeSinceStartupAsDouble;
            int counterQueryFrame = Time.frameCount;
            bool gpuAvailable = display.TryGetAppGPUTimeLastFrame(out float gpuRaw);
            bool compositorAvailable = display.TryGetCompositorGPUTimeLastFrame(out float compositorRaw);
            // OpenXR 1.16.1 passes META millisecond counters through unchanged, including the
            // generic APIs documented as seconds. Keep those raw; use explicit perfmetrics tags.
            bool appGpuAvailable = UnityEngine.XR.Provider.XRStats.TryGetStat(display, "perfmetrics.appgputime", out float appGpuMs);
            bool appCpuAvailable = UnityEngine.XR.Provider.XRStats.TryGetStat(display, "perfmetrics.appcputime", out float appCpuMs);
            bool compositorGpuAvailable = UnityEngine.XR.Provider.XRStats.TryGetStat(display, "perfmetrics.compositorgputime", out float compositorGpuMs);
            bool refreshAvailable = display.TryGetDisplayRefreshRate(out float refresh);
            bool droppedAvailable = display.TryGetDroppedFrameCount(out int dropped);
            bool presentedAvailable = display.TryGetFramePresentCount(out int presented);
            var passes = new List<object>();
            int? passCount = null; string passError = null; bool parametersAvailable = false;
            try
            {
                int count = display.GetRenderPassCount(); passCount = count;
                for (int i = 0; i < count; i++)
                {
                    display.GetRenderPass(i, out var pass);
                    var target = pass.renderTargetDesc;
                    var parameters = new List<object>();
                    int? parameterCount = null; int availableCount = 0;
                    string parameterError = target.width <= 0 || target.height <= 0 ? "render_target_unallocated" : null;
                    if (parameterError == null)
                        try
                        {
                            int total = pass.GetRenderParameterCount(); parameterCount = total;
                            if (total == 0) parameterError = "no_render_parameters";
                            for (int index = 0; index < total; index++)
                                try
                                {
                                    pass.GetRenderParameter(camera, index, out var parameter);
                                    parameters.Add(new { parameter_index = index, available = true, error = (string)null,
                                        view = Enumerable.Range(0, 16).Select(n => parameter.view[n]).ToArray(),
                                        projection = Enumerable.Range(0, 16).Select(n => parameter.projection[n]).ToArray(),
                                        viewport = new[] { parameter.viewport.x, parameter.viewport.y, parameter.viewport.width, parameter.viewport.height },
                                        texture_array_slice = (int?)parameter.textureArraySlice });
                                    availableCount++; parametersAvailable = true;
                                }
                                catch (Exception ex)
                                {
                                    parameterError = ex.Message;
                                    parameters.Add(new { parameter_index = index, available = false, error = ex.Message,
                                        view = (float[])null, projection = (float[])null, viewport = (float[])null, texture_array_slice = (int?)null });
                                }
                        }
                        catch (Exception ex) { parameterError = ex.Message; }
                    passes.Add(new { pass_index = i, render_target_available = target.width > 0 && target.height > 0,
                        render_target_id_api_raw = pass.renderTarget.ToString(),
                        target.width, target.height, target.volumeDepth, target.msaaSamples, target.useDynamicScale, format = target.graphicsFormat.ToString(),
                        render_parameter_count = parameterCount, render_parameters_available = availableCount > 0,
                        render_parameters_error = parameterError, render_parameters = parameters });
                }
            }
            catch (Exception ex) { passError = ex.Message; }
            return new { display.running, display.scaleOfAllRenderTargets, display.scaleOfAllViewports,
                display.foveatedRenderingLevel, foveation_flags = display.foveatedRenderingFlags.ToString(),
                gpu_timing_source = "OpenXR META perfmetrics counters; milliseconds; pinned provider 1.16.1",
                app_gpu_api_raw = TimingValue(gpuAvailable, gpuRaw), compositor_gpu_api_raw = TimingValue(compositorAvailable, compositorRaw),
                generic_api_unit_warning = "Documented seconds; this META provider forwards milliseconds. Raw values are not converted.",
                app_gpu_ms = TimingValue(appGpuAvailable, appGpuMs), app_cpu_ms = TimingValue(appCpuAvailable, appCpuMs),
                compositor_gpu_ms = TimingValue(compositorGpuAvailable, compositorGpuMs),
                refresh_hz = refreshAvailable ? (float?)refresh : null,
                dropped_frames = droppedAvailable ? (int?)dropped : null, frame_present_count = presentedAvailable ? (int?)presented : null,
                counter_metadata = new
                {
                    schema = "researchflow.render-counter-metadata.1",
                    query_realtime_since_startup_s = counterQueryRealtime, query_frame = counterQueryFrame,
                    native_metadata_status = "not_exposed_by_these_managed_getters",
                    native_counter_flags = (string)null, native_counter_unit = (string)null, native_xr_result = (string)null,
                    provider_sample_timestamp = (string)null, provider_sample_age_s = (double?)null,
                    freshness_status = "unknown_provider_timestamp_not_exposed",
                    app_gpu_api_raw = TimingMetadata(gpuAvailable, gpuRaw, "XRDisplaySubsystem.TryGetAppGPUTimeLastFrame"),
                    compositor_gpu_api_raw = TimingMetadata(compositorAvailable, compositorRaw, "XRDisplaySubsystem.TryGetCompositorGPUTimeLastFrame"),
                    app_gpu_ms = TimingMetadata(appGpuAvailable, appGpuMs, "XRStats.TryGetStat:perfmetrics.appgputime"),
                    app_cpu_ms = TimingMetadata(appCpuAvailable, appCpuMs, "XRStats.TryGetStat:perfmetrics.appcputime"),
                    compositor_gpu_ms = TimingMetadata(compositorGpuAvailable, compositorGpuMs, "XRStats.TryGetStat:perfmetrics.compositorgputime"),
                    refresh_hz = new { getter_success = refreshAvailable, source = "XRDisplaySubsystem.TryGetDisplayRefreshRate",
                        raw_output_text = refreshAvailable ? refresh.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null,
                        missing_reason = refreshAvailable ? null : "getter_failed" },
                    dropped_frames = new { getter_success = droppedAvailable, source = "XRDisplaySubsystem.TryGetDroppedFrameCount",
                        raw_output_text = droppedAvailable ? dropped.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                        missing_reason = droppedAvailable ? null : "getter_failed" },
                    frame_present_count = new { getter_success = presentedAvailable, source = "XRDisplaySubsystem.TryGetFramePresentCount",
                        raw_output_text = presentedAvailable ? presented.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                        missing_reason = presentedAvailable ? null : "getter_failed" }
                },
                render_pass_count = passCount, render_parameters_available = parametersAvailable,
                render_parameters_error = passError ?? (passCount == 0 ? "no_render_passes" : !parametersAvailable ? "render_parameters_unavailable" : null),
                matrix_order = "Unity Matrix4x4[index] column-major: row + column*4",
                viewport_units = "XRRenderParameter.viewport API raw x,y,width,height; not converted",
                render_passes = passes };
        }

        static float? TimingValue(bool available, float value) => available && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f ? value : (float?)null;
        static object TimingMetadata(bool getterSuccess, float rawOutput, string source)
        {
            return new { getter_success = getterSuccess, source,
                raw_output_text = getterSuccess ? rawOutput.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null,
                missing_reason = !getterSuccess ? "getter_failed" : float.IsNaN(rawOutput) || float.IsInfinity(rawOutput) ? "non_finite" : rawOutput < 0 ? "negative" : null };
        }
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        void OnDestroy() { output?.Dispose(); output = null; }
    }

    // Read-only access to the installed OpenXR package's protected profile query; never enabled as a feature.
    sealed class PhysicalProbeOpenXRReadback : OpenXRFeature
    {
        public static string Profile(Handedness side)
        {
            ulong profile = GetCurrentInteractionProfile(side == Handedness.Left ? "/user/hand/left" : "/user/hand/right");
            return profile == 0 ? null : PathToString(profile);
        }
    }
}
#endif
