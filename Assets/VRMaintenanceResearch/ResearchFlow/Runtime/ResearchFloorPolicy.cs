using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Maintains the existing requested Floor policy without writing any height or pose.</summary>
    public sealed class ResearchFloorPolicy : MonoBehaviour
    {
        public event Action Changed;
        public bool Ready => editorTechnicalWithoutXR || recovery.Ready;
        public string BlockReason => editorTechnicalWithoutXR ? "EditorTechnicalWithoutXR" : recovery.Status;
        public int RequestAttempts => recovery.Attempts;
        public static bool EditorFixtureAllowed
        {
            get
            {
                if (!Application.isEditor) return false;
                var subsystems = new List<XRInputSubsystem>();
                SubsystemManager.GetSubsystems(subsystems);
                return !subsystems.Exists(s => s.running);
            }
        }
        readonly ResearchFloorRecovery recovery = new ResearchFloorRecovery();
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        XROrigin origin;
        ResearchSessionFlowController flow;
        XRInputSubsystem input;
        double nextPoll;
        bool editorTechnicalWithoutXR, publishedReady;
        string publishedReason;

        public void Configure(XROrigin target)
        {
            origin = target;
            flow = GetComponent<ResearchSessionFlowController>();
            Restart();
        }
        void OnEnable() => Restart();
        void OnDisable()
        {
            if (input != null) input.trackingOriginUpdated -= OriginUpdated;
            input = null;
            editorTechnicalWithoutXR = false;
            recovery.Restart();
            Publish();
        }
        void OnApplicationFocus(bool focused) { if (focused) Restart(); }
        void OnApplicationPause(bool paused) { if (!paused) Restart(); }
        void Restart()
        {
            recovery.Restart();
            editorTechnicalWithoutXR = false;
            nextPoll = 0;
            Publish();
        }
        void OriginUpdated(XRInputSubsystem subsystem)
        {
            nextPoll = 0;
            if (subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor) return;
            recovery.Invalidate();
            Publish();
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextPoll) return;
            nextPoll = now + .25;
            inputs.Clear();
            SubsystemManager.GetSubsystems(inputs);
            XRInputSubsystem running = null;
            foreach (var candidate in inputs) if (candidate.running) { running = candidate; break; }
            if (input != running)
            {
                if (input != null) input.trackingOriginUpdated -= OriginUpdated;
                input = running;
                if (input != null) input.trackingOriginUpdated += OriginUpdated;
                recovery.Restart();
            }
            // Existing Editor technical fixtures have no native subsystem. Never bypass standalone or a study record.
            editorTechnicalWithoutXR = Application.isEditor && input == null && flow && flow.IsTechnical;
            bool requestedFloor = origin && origin.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Floor;
            var supported = input == null ? TrackingOriginModeFlags.Unknown : input.GetSupportedTrackingOriginModes();
            var mode = input == null ? TrackingOriginModeFlags.Unknown : input.GetTrackingOriginMode();
            bool managedFloor = origin && origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor &&
                origin.CameraFloorOffsetObject && Mathf.Abs(origin.CameraFloorOffsetObject.transform.localPosition.y) < .0001f;
            bool request = recovery.Observe(now, requestedFloor && input != null && input.running,
                supported != TrackingOriginModeFlags.Unknown, (supported & TrackingOriginModeFlags.Floor) != 0,
                mode == TrackingOriginModeFlags.Floor, managedFloor);
            Publish();
            if (!request) return;
            // Unity 6000.3 XRInputSubsystem native API; XROrigin handles the ensuing origin update and offset removal.
            bool accepted = input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            flow?.Log("REFERENCE_SPACE_REQUEST", "xr_origin", accepted ? "AcceptedAwaitingReadback" : "Rejected",
                after: new { attempt = recovery.Attempts, previous_mode = mode.ToString(), supported = supported.ToString() }, source: "System");
        }

        void Publish()
        {
            if (publishedReady == Ready && publishedReason == BlockReason) return;
            publishedReady = Ready;
            publishedReason = BlockReason;
            flow?.Log("REFERENCE_SPACE_STATE", "xr_origin", BlockReason, after: new { ready = Ready, attempts = recovery.Attempts }, source: "System");
            Changed?.Invoke();
        }
    }
}
