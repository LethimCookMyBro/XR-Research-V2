using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;
using InputDevice = UnityEngine.InputSystem.InputDevice;
using HandSide = Oculus.Interaction.Input.Handedness;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>ResearchFlow owns lifecycle; this component owns bounded observation/subscriptions only.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed class ResearchMovementSampler : MonoBehaviour
    {
        static readonly ProfilerMarker Marker = new ProfilerMarker("Research.Movement.ObserveAndWrite");
        ResearchSessionFlowController owner;
        HandsLabLogger logger;
        ResearchMovementCollection collection;
        Transform trackingSpace, leftVisual, rightVisual;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        readonly Dictionary<int, (double providerTime, double observedTime)> observations = new Dictionary<int, (double, double)>();
        XRInputSubsystem subscribedInput;
        Vector3 originPosition;
        Quaternion originRotation;
        bool originKnown, applicationPaused, focused = true;
        double nextSubsystemPoll;
        public long ScheduledFrames => collection?.Frames ?? 0;
        public double LastWriteMilliseconds { get; private set; }
        public double MaximumWriteMilliseconds { get; private set; }

        public static ResearchMovementSampler Install(ResearchSessionFlowController controller)
        {
            var sampler = controller.GetComponent<ResearchMovementSampler>() ?? controller.gameObject.AddComponent<ResearchMovementSampler>();
            sampler.owner = controller;
            var retained = UnityEngine.Object.FindObjectsByType<HandsLabSession>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(s => s.gameObject.scene == controller.gameObject.scene && controller.origin && s.head == controller.origin.Camera);
            sampler.trackingSpace = retained && retained.trackingSpace ? retained.trackingSpace :
                controller.origin && controller.origin.CameraFloorOffsetObject ? controller.origin.CameraFloorOffsetObject.transform : controller.origin ? controller.origin.transform : null;
            sampler.leftVisual = retained ? retained.leftVisual : null;
            sampler.rightVisual = retained ? retained.rightVisual : null;
            return sampler;
        }

        void OnEnable() { nextSubsystemPoll = 0; collection?.Boundary("sampler_enabled"); }
        void OnApplicationPause(bool paused)
        { applicationPaused = paused; Boundary(paused ? "application_pause" : "application_resume"); }
        void OnApplicationFocus(bool hasFocus)
        { focused = hasFocus; Boundary(hasFocus ? "application_focus_restored" : "application_focus_lost"); }
        public void Boundary(string reason) => collection?.Boundary(reason);
        void ReferenceSpaceChanged(XRInputSubsystem input) => Boundary("tracking_origin_updated_recenter");

        void Update()
        {
            if (!owner) owner = GetComponent<ResearchSessionFlowController>();
            if (!owner || !owner.isActiveAndEnabled) return;
            if (logger != owner.Logger)
            {
                collection?.FlushAndStop("session_changed");
                logger = owner.Logger; collection = logger == null ? null : new ResearchMovementCollection(logger);
                observations.Clear(); originKnown = false;
            }
            if (collection == null || logger.State != HandsLabLogState.Active || owner.Record == null || owner.Record.run_id != logger.RunId) return;
            var flow = owner.Flow;
            if (flow?.IsTaskActive == true) collection.NoteActiveTask(flow.CurrentTask);
            if (owner.origin)
            {
                var reference = owner.origin.transform;
                if (originKnown && (Vector3.Distance(originPosition, reference.position) > .0001f || Quaternion.Angle(originRotation, reference.rotation) > .01f))
                    Boundary("world_origin_relocated_teleport_snap_or_recenter");
                originPosition = reference.position; originRotation = reference.rotation; originKnown = true;
            }
            if (Time.realtimeSinceStartupAsDouble >= nextSubsystemPoll)
            {
                nextSubsystemPoll = Time.realtimeSinceStartupAsDouble + 1;
                inputs.Clear(); SubsystemManager.GetSubsystems(inputs);
                var running = inputs.FirstOrDefault(i => i.running);
                if (running != subscribedInput)
                {
                    if (subscribedInput != null) subscribedInput.trackingOriginUpdated -= ReferenceSpaceChanged;
                    subscribedInput = running;
                    if (subscribedInput != null) subscribedInput.trackingOriginUpdated += ReferenceSpaceChanged;
                    Boundary("xr_subsystem_changed");
                }
            }
            if (!collection.Due) return;
            double started = Time.realtimeSinceStartupAsDouble;
            using (Marker.Auto())
            {
                bool paused = applicationPaused || flow?.Paused == true || owner.Transitioning;
                string phase = flow?.IsTutorial == true ? "Tutorial" : flow != null && flow.CurrentTask.Length > 0 ? "Experiment" : "Session";
                string task = flow?.IsTutorial == true ? "training" : flow != null && flow.CurrentTask.Length > 0 ? flow.CurrentTask : "session";
                var frame = new HandsLabFrame { head = DevicePose(XRNode.Head), leftProvider = DevicePose(XRNode.LeftHand), rightProvider = DevicePose(XRNode.RightHand),
                    leftVisual = VisualPose(leftVisual, HandSide.Left), rightVisual = VisualPose(rightVisual, HandSide.Right),
                    origin = VisualPose(owner.origin ? owner.origin.transform : null, null), inputSource = "None" };
                collection.Tick(frame, phase, task, flow?.State.ToString() ?? "ResearcherSetup", flow?.IsTaskActive == true, paused, focused);
            }
            LastWriteMilliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;
            MaximumWriteMilliseconds = Math.Max(MaximumWriteMilliseconds, LastWriteMilliseconds);
        }

        PoseSample DevicePose(XRNode node)
        {
            var simulated = InputSystem.devices.FirstOrDefault(d => IsSimulated(d) && (node == XRNode.Head ? d is XRHMD :
                d is XRController && d.usages.Any(u => u.ToString() == (node == XRNode.LeftHand ? "LeftHand" : "RightHand"))));
            if (simulated != null)
                return owner.Record.test_data ? SimulatedPose(simulated) : new PoseSample { provider = "InputSystem." + simulated.layout,
                    inputSource = "SimulatedController", reason = "simulated_provider_rejected_in_study" };
            var device = InputDevices.GetDeviceAtXRNode(node);
            var pose = new PoseSample { provider = "UnityXR.InputDevices." + node, inputSource = node == XRNode.Head || !device.isValid ? "None" : "Controller",
                sampleTimestamp = logger.Timestamp, reason = "provider_tracking_unavailable" };
            if (!trackingSpace || !device.isValid || !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out InputTrackingState state) || (state & (InputTrackingState.Position | InputTrackingState.Rotation)) != (InputTrackingState.Position | InputTrackingState.Rotation) ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position) || !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation)) return pose;
            pose.position = V(trackingSpace.TransformPoint(position)); pose.rotation = Q(trackingSpace.rotation * rotation);
            pose.trackingValid = true; pose.reason = "provider_query_no_native_sample_timestamp";
            return pose;
        }

        static bool IsSimulated(InputDevice device) => device.layout.StartsWith("XRSimulated", StringComparison.Ordinal) || device.layout.StartsWith("HandsLabSimulated", StringComparison.Ordinal)
            || device.name.StartsWith("HandsLabSimulated", StringComparison.Ordinal);
        PoseSample SimulatedPose(InputDevice device)
        {
            var result = new PoseSample { provider = "InputSystem." + device.layout, inputSource = "SimulatedController", reason = "simulated_tracking_invalid" };
            if (!trackingSpace) { result.reason = "tracking_coordinate_space_unavailable"; return result; }
            Vector3 position; Quaternion rotation;
            var aggregate = device.TryGetChildControl<PoseControl>("devicePose");
            if (aggregate != null)
            {
                var pose = aggregate.ReadValue();
                if (!pose.isTracked || (pose.trackingState & (InputTrackingState.Position | InputTrackingState.Rotation)) != (InputTrackingState.Position | InputTrackingState.Rotation)) return result;
                position = pose.position; rotation = pose.rotation;
            }
            else
            {
                var tracked = device.TryGetChildControl<ButtonControl>("isTracked"); var state = device.TryGetChildControl<IntegerControl>("trackingState");
                var pos = device.TryGetChildControl<Vector3Control>("devicePosition"); var rot = device.TryGetChildControl<QuaternionControl>("deviceRotation");
                if (tracked == null || !tracked.isPressed || state == null || (state.ReadValue() & 3) != 3 || pos == null || rot == null) return result;
                position = pos.ReadValue(); rotation = rot.ReadValue();
            }
            if (!observations.TryGetValue(device.deviceId, out var observation) || observation.providerTime != device.lastUpdateTime)
            {
                // InputSystem lastUpdateTime uses realtimeSinceStartup; retain age instead of re-dating cached input.
                observation = (device.lastUpdateTime, logger.Timestamp - (Time.realtimeSinceStartupAsDouble - device.lastUpdateTime));
                observations[device.deviceId] = observation;
            }
            result.position = V(trackingSpace.TransformPoint(position)); result.rotation = Q(trackingSpace.rotation * rotation);
            result.sampleTimestamp = observation.observedTime; result.trackingValid = true; result.reason = "simulated_input_state_event_observed";
            return result;
        }

        PoseSample VisualPose(Transform value, HandSide? side)
        {
            var guard = side.HasValue ? owner.guards.FirstOrDefault(g => g && g.side == side.Value) : null;
            string source = !side.HasValue ? "None" : InputSystem.devices.Any(d => IsSimulated(d) && d is XRController && d.usages.Any(u => u.ToString() == (side == HandSide.Left ? "LeftHand" : "RightHand"))) ? "SimulatedController" : "Controller";
            if (!value || !value.gameObject.activeInHierarchy || (side.HasValue && (!guard || !guard.TrackingValid)))
                return new PoseSample { provider = side.HasValue ? "ResearchFlow.ConstrainedVisualTransform" : "XR Origin authored reference", inputSource = source, reason = "visual_not_current" };
            return new PoseSample { provider = side.HasValue ? "ResearchFlow.ConstrainedVisualTransform" : "XR Origin authored reference", inputSource = source,
                position = V(value.position), rotation = Q(value.rotation), trackingValid = true, sampleTimestamp = logger.Timestamp, reason = "visual_observation_not_provider" };
        }

        public bool FlushAndStop(string reason) => collection == null || collection.FlushAndStop(reason);
        void OnDisable()
        {
            FlushAndStop("sampler_disabled");
            if (subscribedInput != null) subscribedInput.trackingOriginUpdated -= ReferenceSpaceChanged;
            subscribedInput = null; collection = null; logger = null; observations.Clear();
        }
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static float[] Q(Quaternion value) => new[] { value.x, value.y, value.z, value.w };
    }
}
