using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchPanelMotion : MonoBehaviour
    {
        public ResearchFlowView view;
        public string panelId;
        public bool movable = true;
        // Status modals (setup, pause, tracking, error) try straight ahead first; clearance fallbacks unchanged.
        [System.NonSerialized] public bool centered;
        Renderer[] environment;
        readonly Collider[] overlapBuffer = new Collider[256];
        readonly Plane[] sightVolume = new Plane[6];
        readonly Vector3[] sightCorners = new Vector3[4];
        readonly Vector3[] otherCorners = new Vector3[4];
        readonly List<Button> buttons = new List<Button>();
        readonly List<ResearchDragHandle> handles = new List<ResearchDragHandle>();
        readonly List<Vector3> actionPoints = new List<Vector3>();
        long placementTicks;
        public int PlacementCalls { get; private set; }
        public int PlacementRejections { get; private set; }
        public double PlacementMilliseconds => placementTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        bool canvasEnabledBeforeBlock, raycasterEnabledBeforeBlock;
        float nextBlockedRetry;
        float nextClearanceCheck;
        bool clearanceKnown, clearanceValue;
        Pose clearanceHead, clearancePanel;
        Rect clearanceRect;
        Vector3 clearanceScale;
        bool deferredRetryUsed = true;
        bool faceHidden;
        bool placementAccepted;
        // Idle re-checks of an accepted pose: a held part or the other panel crossing the line of sight is ordinary occlusion, not a
        // placement fault. Treating it as one hid the Canvas/Raycaster and let Main and Reader conceal each other in turn (flicker).
        // New placements (recenter, drag release) still test peers and movable parts.
        bool idleCheck;
        bool IgnoreMovableParts => idleCheck || (panelId == "task_information" && placementAccepted && FixedWorkingSlate);
        bool FixedWorkingSlate => ResearchUIPolicy.StationAnchoredPanels && !centered && view && view.controller
            && view.controller.Flow != null && view.controller.Flow.IsTaskActive
            && ResearchStationAnchors.Supports(view.controller.CurrentStationIndex, panelId);
        float faceTransitionSince = float.NegativeInfinity;
        // Re-show only after the accepted pose stays clear this long; input stays off while waiting.
        const float RecoverDwellSeconds = .25f;
        float clearSince = float.NegativeInfinity;
        Pose? pendingRestore;
        Pose? deferredViewportHead;
        bool pendingOnlyIfNeeded, pendingRequireView = true;
        string pendingReason = "recenter";
        bool PlacementBusy => IsDragging || (view && view.PanelPlacementBusy);
        public bool PlacementBlocked { get; private set; }
        public bool WorldPlacementBlocked => PlacementBlocked && !faceHidden;
        public string PlacementFailureKind => FailureKind(LastRejection, faceHidden);
        public static string FailureKind(string reason, bool hiddenFace = false)
        {
            if (hiddenFace || reason == "rear_face" || reason == "edge_on") return "facing";
            if (reason.StartsWith("viewport:", System.StringComparison.Ordinal) || reason.StartsWith("action_viewport:", System.StringComparison.Ordinal)
                || reason.StartsWith("recovery_projection:", System.StringComparison.Ordinal)) return "viewport";
            if (reason.StartsWith("collider:", System.StringComparison.Ordinal) || reason.StartsWith("renderer:", System.StringComparison.Ordinal)
                || reason.StartsWith("occluded", System.StringComparison.Ordinal) || reason.StartsWith("other_panel", System.StringComparison.Ordinal)
                || reason == "overlap_buffer_full" || reason == "sight_buffer_full") return "geometry";
            return "placement";
        }
        public bool HasAcceptedPose => placementAccepted;
        bool FixedReader => panelId == "reference" && ResearchUIPolicy.Active == ResearchUIPolicy.FixedDemo
            && view && view.controller && view.controller.Flow?.IsTaskActive == true;
        public string LastRejection { get; private set; } = "";
        public bool RecenterPending { get; private set; }
        public bool LastHeadPoseReady { get; private set; }
        public bool LastPresenceKnown { get; private set; }
        public bool LastUserPresent { get; private set; }
        public int LastPlacementFrame { get; private set; } = -1;
        public Vector3 LastPlacementHeadPosition { get; private set; }
        public bool IsReadableFromHead => FitsView(transform.position, transform.rotation);
        public bool IsDragging
        {
            get
            {
                GetComponentsInChildren(false, handles);
                foreach (var handle in handles) if (handle.IsDragging) return true;
                return false;
            }
        }
        bool pendingParticipant, pendingAnimate;
        string pendingInput;
        int requestedFrame;
        // Placement tests use the resting canvas scale; enter/exit animations scale the root transiently.
        Vector3 RestScale => (transform.parent ? transform.parent.lossyScale : Vector3.one) * .001f;
        public object Pose() => new { position = new[] { transform.position.x, transform.position.y, transform.position.z }, yaw = transform.eulerAngles.y };
        public void Recenter(bool participant, bool animate = true, string input = "None", bool onlyIfNeeded = false, string reason = "recenter")
        {
            if (!view || !view.Head) return;
            if (reason != "clearance_retry") placementAccepted = false;
            pendingRestore = null;
            deferredViewportHead = null;
            pendingOnlyIfNeeded = onlyIfNeeded; pendingRequireView = true;
            pendingReason = reason;
            deferredRetryUsed = false;
            bool busy = PlacementBusy;
            if (FixedReader && !HasAcceptedPose) { LastRejection = "awaiting_authored_pose"; SetPlacementBlocked(true); }
            QueueRecenter(participant, animate, input);
            if (busy) view.controller?.Log("PANEL_PLACEMENT", panelId, "Deferred", source: "System", inputSource: input, reason: reason + ":interaction_busy");
            if (!onlyIfNeeded && !busy && (!Application.isPlaying || Application.isEditor))
                TryApplyPendingRecenter(true, true, true, Time.frameCount + 1);
        }
        void QueueRecenter(bool participant, bool animate, string input)
        {
            pendingParticipant = participant; pendingAnimate = animate; pendingInput = input;
            requestedFrame = Time.frameCount;
            LastHeadPoseReady = false;
            LastPresenceKnown = LastUserPresent = false;
            RecenterPending = true;
        }
        void OnDisable() { RecenterPending = false; pendingRestore = null; deferredViewportHead = null; clearanceKnown = false; placementAccepted = false; }
        void OnRectTransformDimensionsChange() { clearanceKnown = false; }
        void OnTransformChildrenChanged() { clearanceKnown = false; }
        bool IdlePlacementClear()
        {
            var rect = ((RectTransform)transform).rect;
            bool changed = !clearanceKnown || Vector3.Distance(clearanceHead.position, view.Head.position) >= .005f
                || Quaternion.Angle(clearanceHead.rotation, view.Head.rotation) >= .5f
                || clearancePanel.position != transform.position || clearancePanel.rotation != transform.rotation
                || clearanceRect != rect || clearanceScale != RestScale;
            if (!changed && Time.unscaledTime < nextClearanceCheck) return clearanceValue;
            clearanceHead = new Pose(view.Head.position, view.Head.rotation);
            clearancePanel = new Pose(transform.position, transform.rotation);
            clearanceRect = rect; clearanceScale = RestScale;
            nextClearanceCheck = Time.unscaledTime + .1f;
            idleCheck = true;
            try { clearanceValue = PlacementClear(transform.position, transform.rotation); }
            finally { idleCheck = false; }
            clearanceKnown = true;
            return clearanceValue;
        }
        void LateUpdate()
        {
            if (view && view.Head)
            {
                var sight = transform.position - view.Head.position;
                float facing = sight.sqrMagnitude > .0001f ? Vector3.Dot(transform.forward, sight.normalized) : -1;
                bool clear = IdlePlacementClear();
                if (!clear) { faceHidden = false; SetPlacementBlocked(true); faceTransitionSince = clearSince = float.NegativeInfinity; }
                else
                {
                    if (PlacementBlocked && !faceHidden && facing >= .58f)
                    {
                        if (float.IsNegativeInfinity(clearSince)) clearSince = Time.unscaledTime;
                        if (Time.unscaledTime - clearSince >= RecoverDwellSeconds) { SetPlacementBlocked(false); clearSince = float.NegativeInfinity; }
                    }
                    else clearSince = float.NegativeInfinity;
                    bool change = faceHidden ? facing >= .58f : facing < .52f;
                    if (!change) faceTransitionSince = float.NegativeInfinity;
                    else
                    {
                        if (float.IsNegativeInfinity(faceTransitionSince)) faceTransitionSince = Time.unscaledTime;
                        if (facing < .45f || Time.unscaledTime - faceTransitionSince >= .08f)
                        {
                            faceHidden = !faceHidden;
                            if (faceHidden) LastRejection = facing <= 0 ? "rear_face" : "edge_on";
                            SetPlacementBlocked(faceHidden);
                            faceTransitionSince = float.NegativeInfinity;
                        }
                    }
                }
            }
            // Explicit recovery may relocate a fixed slate; a temporary obstruction must not move its accepted pose.
            if (PlacementBlocked && !faceHidden && !RecenterPending && !FixedReader && !(FixedWorkingSlate && placementAccepted)
                && view && view.Head && Time.unscaledTime >= nextBlockedRetry)
            { nextBlockedRetry = Time.unscaledTime + 1f; Recenter(false, false, reason: "clearance_retry"); deferredRetryUsed = true; }
            if (!RecenterPending) return;
            if (!Application.isPlaying || Application.isEditor)
            { TryApplyPendingRecenter(true, true, true, pendingOnlyIfNeeded ? Time.frameCount : Time.frameCount + 1); return; }
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool ready = device.isValid
                && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked
                && device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
                && (state & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation)
                && device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
                && device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            bool present = false;
            bool presenceKnown = device.isValid && device.TryGetFeatureValue(CommonUsages.userPresence, out present);
            TryApplyPendingRecenter(ready, presenceKnown, present, Time.frameCount);
        }
        void TryApplyPendingRecenter(bool poseReady, bool presenceKnown, bool userPresent, int frame)
        {
            if (!RecenterPending) return;
            LastHeadPoseReady = poseReady;
            LastPresenceKnown = presenceKnown;
            LastUserPresent = userPresent;
            // Let the InputSystem TrackedPoseDriver consume a later frame's pose before placing UI.
            if (!poseReady || !presenceKnown || !userPresent || frame <= requestedFrame || !view || !view.Head || PlacementBusy) return;
            if (deferredViewportHead.HasValue && Vector3.Distance(deferredViewportHead.Value.position, view.Head.position) < .005f
                && Quaternion.Angle(deferredViewportHead.Value.rotation, view.Head.rotation) < .5f) return;
            deferredViewportHead = null;
            if (panelId == "reference" && view.mainPanel && view.mainPanel.gameObject.activeInHierarchy && view.mainPanel.RecenterPending) return;
            RecenterPending = false;
            if (pendingRestore.HasValue)
            {
                var pose = pendingRestore.Value; pendingRestore = null;
                bool clear = ReadableFace(pose.position, pose.rotation) && PlacementClear(pose.position, pose.rotation);
                if (clear || FixedWorkingSlate)
                {
                    var before = Pose();
                    bool moved = Vector3.Distance(transform.position, pose.position) > .0001f || Quaternion.Angle(transform.rotation, pose.rotation) > .01f;
                    transform.SetPositionAndRotation(pose.position, pose.rotation);
                    AcceptPose();
                    faceHidden = false; SetPlacementBlocked(!clear);
                    if (moved) view.controller?.Log("PANEL_PLACEMENT", panelId, "Restored", before, Pose(), source: "System", reason: "saved_task_pose");
                    return;
                }
            }
            if (pendingOnlyIfNeeded && ReadableFace(transform.position, transform.rotation)
                && (!pendingRequireView || FitsView(transform.position, transform.rotation)) && PlacementClear(transform.position, transform.rotation))
            { faceHidden = false; deferredRetryUsed = true; AcceptPose(); SetPlacementBlocked(false); return; }
            ApplyRecenter(pendingParticipant, pendingAnimate, pendingInput);
        }
        void ApplyRecenter(bool participant, bool animate, string input)
        {
            if (PlacementBusy) { QueueRecenter(participant, animate, input); return; }
            var forward = Vector3.ProjectOnPlane(view.Head.forward, Vector3.up); if (forward.sqrMagnitude < .01f) forward = Vector3.forward; forward.Normalize();
            // Leave the central manipulation area clear; reference opens on the other side.
            float side = panelId == "reference" ? -1 : 1;
            // The 1.04 m Reference needs a nearer-forward angle to keep its sidebar in view.
            // Keep clearance fallbacks for low eye positions and nearby equipment.
            var angles = panelId == "reference" ? new[] { 20f, 24f, 28f, 35f, 55f, 75f } : centered ? new[] { 0f, 28f, 32f, 35f, 55f, 75f }
                : FullMainPanel ? new[] { 20f, 24f, 28f, 32f, 35f, 55f, 75f, 0f } : new[] { 28f, 35f, 55f, 75f };
            Vector3 position = default;
            Quaternion rotation = default;
            bool found = false;
            // Fixed Readers accept authored geometry; content exposure still requires both eye viewports.
            string anchorId = null, anchorFallback = null;
            bool preferHeadRelative = participant && ResearchUIPolicy.AnchoredMovable;
            if (!preferHeadRelative)
                found = TryStationAnchor(out position, out rotation, out anchorId, out anchorFallback);
            // A low eye pose or tall assembly can block the slate. Try bounded
            // height/distance alternatives before turning it farther out of view.
            var distances = preferHeadRelative
                ? new[] { .85f, 1.0f, 1.15f, 1.3f, 1.5f }
                : panelId == "recovery" ? new[] { .9f, 1.05f, 1.2f, .65f, .75f } : centered ? new[] { .85f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.2f } : FullMainPanel ? new[] { 1.2f, 1.4f, 1.6f, 1.8f, 2f } : panelId == "reference" ? new[] { 1.2f, 1.4f, 1.6f, 1.8f } : new[] { 1.2f };
            var candidateAngles = preferHeadRelative ? new[] { 0f, 16f, 24f, 32f, 45f } : angles;
            // A seated 1.0 m eye at the Computer bench: every right-side pose of the Ready and Task cards hit the case or bench, so the
            // card hid and neither Start nor Inspect could be pressed. A 504-pose sweep found clear poses only 20 deg to the left,
            // 0.2-0.4 m up (owner-20261020/sweep, 21 Oct). The mirrored pass runs only when the first found nothing.
            bool mirrored = !preferHeadRelative && panelId != "reference" && panelId != "recovery";
            for (int pass = 0; pass < (mirrored ? 2 : 1) && !found && !FixedReader; pass++)
            foreach (float angle in pass == 1 ? new[] { 20f, 28f, 35f } : candidateAngles)
            {
                foreach (float distance in distances)
                {
                    // At a native .99 m eye pose, no original Tutorial candidate was both visible and clear near the worktop.
                    foreach (float height in pass == 1 ? new[] { .2f, .3f, .4f } : preferHeadRelative ? new[] { -.08f, 0f, .12f, .24f } : panelId == "recovery" ? new[] { -.25f, -.4f, .2f } : panelId == "reference" || FullMainPanel ? new[] { -.1f, .2f, .4f } : new[] { -.1f, .2f })
                    {
                        var direction = Quaternion.AngleAxis((pass == 1 ? -side : side) * angle, Vector3.up) * forward;
                        rotation = Quaternion.LookRotation(direction, Vector3.up);
                        position = view.Head.position + direction * distance + Vector3.up * height;
                        bool viewOk = preferHeadRelative && distance < 1.15f ? ReadableFromHeadForDrag(position, rotation) : FitsView(position, rotation);
                        if (viewOk && PlacementClear(position, rotation)) { found = true; break; }
                    }
                    if (found) break;
                }
                if (found) break;
            }
            if (!found && preferHeadRelative)
                found = TryStationAnchor(out position, out rotation, out anchorId, out anchorFallback);
            if (!found)
            {
                if (!PlacementBlocked) view.controller?.Log("PANEL_PLACEMENT", panelId, "Blocked", after: anchorFallback != null ? new { anchor_rejection = anchorFallback } : null,
                    source: "System", inputSource: input, reason: pendingReason + ":" + LastRejection);
                nextBlockedRetry = Mathf.Max(nextBlockedRetry, Time.unscaledTime + .5f);
                faceHidden = false; SetPlacementBlocked(true);
                if (pendingOnlyIfNeeded && LastRejection.StartsWith("viewport:", System.StringComparison.Ordinal))
                {
                    QueueRecenter(participant, animate, input);
                    deferredViewportHead = new Pose(view.Head.position, view.Head.rotation);
                }
                return;
            }
            var before = Pose();
            bool moved = Vector3.Distance(transform.position, position) > .0001f || Quaternion.Angle(transform.rotation, rotation) > .01f;
            LastPlacementFrame = Time.frameCount;
            LastPlacementHeadPosition = view.Head.position;
            // Recenter directly: interpolating can sweep the panel through equipment.
            transform.SetPositionAndRotation(position, rotation);
            AcceptPose();
            faceHidden = false; deferredRetryUsed = true; SetPlacementBlocked(false);
            if (anchorId != null)
                view.controller?.Log("PANEL_PLACEMENT", panelId, "Anchored", before, new { pose = Pose(), anchor = anchorId, moved },
                    source: "System", inputSource: input, reason: pendingReason);
            else if (moved || anchorFallback != null)
                view.controller?.Log("PANEL_PLACEMENT", panelId, anchorFallback != null ? "AnchorFallbackRecentered" : "Recentered", before,
                    anchorFallback != null ? (object)new { pose = Pose(), anchor_rejection = anchorFallback } : Pose(),
                    source: "System", inputSource: input, reason: pendingReason);
            if (participant) view.controller?.Log("PANEL_RECENTER", panelId, "Recentered", before, Pose(), inputSource: input);
            if (participant && panelId == "reference") view.controller?.Evidence("recenter");
        }

        bool TryStationAnchor(out Vector3 position, out Quaternion rotation, out string anchorId, out string fallback)
        {
            position = default; rotation = default; anchorId = null; fallback = null;
            var owner = view ? view.controller : null;
            // Status modals (centered) and the recovery strip stay head-relative; only working slates are anchored.
            if (!ResearchUIPolicy.StationAnchoredPanels || centered || !owner || !ResearchStationAnchors.Supports(owner.CurrentStationIndex, panelId)) return false;
            if (!owner.TryStationFrame(out var station, out int index, out string layout, out double eye)) return false;
            var stationPosition = station.position;
            for (int candidate = 0; candidate < ResearchStationAnchors.CandidateCount(index, panelId); candidate++)
            {
                if (!ResearchStationAnchors.TryGet(index, layout, panelId, stationPosition.x, stationPosition.y, stationPosition.z,
                    station.eulerAngles.y, eye, candidate, out var pose))
                { fallback = "anchor_unavailable:" + ResearchStationAnchors.StationName(index) + ":layout=" + (layout ?? "none"); return false; }
                var p = new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z);
                var r = Quaternion.Euler(0, (float)pose.YawDegrees, 0);
                bool clear = PlacementClear(p, r);
                if (clear && (!FixedReader || PeerClearForCandidate(p, r)) && (FixedReader || FitsView(p, r))) { position = p; rotation = r; anchorId = pose.Id; return true; }
                fallback = pose.Id + ":" + LastRejection;
            }
            return false;
        }

        void AcceptPose()
        {
            placementAccepted = true;
            if (FixedReader) view.SaveReaderTaskPose(transform.position, transform.rotation);
        }

        bool PeerClearForCandidate(Vector3 position, Quaternion rotation)
        {
            var peer = panelId == "reference" ? view.mainPanel : view.referencePanel;
            if (!peer || !peer.gameObject.activeInHierarchy || peer.PlacementBlocked || !peer.GetComponent<Canvas>().enabled) return true;
            if (!peer.PlacementClear(peer.transform.position, peer.transform.rotation)) { LastRejection = "other_panel_not_clear"; return false; }
            var rect = ((RectTransform)transform).rect;
            var bounds = new Bounds(position, Vector3.zero);
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
                bounds.Encapsulate(position + rotation * Vector3.Scale(new Vector3(x == 0 ? rect.xMin : rect.xMax, y == 0 ? rect.yMin : rect.yMax, 0), RestScale));
            bounds.Expand(.08f);
            var peerCentre = (peer.sightCorners[0] + peer.sightCorners[2]) * .5f;
            var peerBox = new Bounds(peerCentre, Vector3.zero);
            foreach (var corner in peer.sightCorners) peerBox.Encapsulate(corner);
            peerBox.Expand(.08f);
            var camera = view.Head.GetComponent<Camera>();
            bool clear = camera && camera.stereoEnabled
                ? peer.SightVolumeClear(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint(Vector3.zero), peerCentre, peer.transform.rotation, peerBox, bounds)
                    && peer.SightVolumeClear(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint(Vector3.zero), peerCentre, peer.transform.rotation, peerBox, bounds)
                : peer.SightVolumeClear(view.Head.position, peerCentre, peer.transform.rotation, peerBox, bounds);
            if (!clear) LastRejection = "other_panel_occluded_by_candidate";
            return clear;
        }

        bool ReadableFace(Vector3 position, Quaternion rotation)
        {
            if (!view || !view.Head) return false;
            var sight = position - view.Head.position;
            float facing = sight.sqrMagnitude > .0001f ? Vector3.Dot(rotation * Vector3.forward, sight.normalized) : -1;
            if (facing >= .55f) return true;
            LastRejection = facing <= 0 ? "rear_face" : "edge_on";
            return false;
        }

        public bool ReadableFromHeadForDrag(Vector3 position, Quaternion rotation)
        {
            if (!ReadableFace(position, rotation)) return false;
            if (!view || !view.Head) return false;
            // A slate pulled close may be wider than the view, but its central 75 % must stay inside both eyes' viewports.
            return FitsView(position, rotation, ResearchUIPolicy.AnchoredMovable
                && Vector3.Distance(position, view.Head.position) < 1.25f ? .75f : 1f);
        }

        bool FitsView(Vector3 position, Quaternion rotation, float extent = 1f)
        {
            if (!ReadableFace(position, rotation)) return false;
            var camera = view.Head.GetComponent<Camera>();
            if (!camera) return true;
            // Mono can accept a slate clipped in one native eye. Validate both actual stereo transforms.
            return camera.stereoEnabled
                ? FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Left, extent)
                    && FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Right, extent)
                : FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Mono, extent);
        }

        bool FitsViewEye(Vector3 position, Quaternion rotation, Camera camera, Camera.MonoOrStereoscopicEye eye, float extent)
        {
            var full = ((RectTransform)transform).rect;
            var rect = new Rect(full.center - full.size * extent / 2, full.size * extent);
            for (int horizontal = 0; horizontal < 2; horizontal++)
                for (int vertical = 0; vertical < 2; vertical++)
                {
                    float x = horizontal == 0 ? rect.xMin : rect.xMax, y = vertical == 0 ? rect.yMin : rect.yMax;
                    var point = camera.WorldToViewportPoint(position + rotation * Vector3.Scale(new Vector3(x, y, 0), RestScale), eye);
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                        float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z) ||
                        point.z <= 0 || point.x < .02f || point.x > .98f || point.y < .02f || point.y > .98f)
                    { LastRejection = "viewport:" + eye + ":" + point.x.ToString("0.00") + "," + point.y.ToString("0.00"); return false; }
                }
            CollectActionPoints();
            foreach (var actionPoint in actionPoints)
            {
                var point = camera.WorldToViewportPoint(position + rotation * Vector3.Scale(actionPoint, RestScale), eye);
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z) ||
                    point.z <= 0 || point.x < .02f || point.x > .98f || point.y < .02f || point.y > .98f)
                { LastRejection = "action_viewport:" + eye + ":" + point.x.ToString("0.00") + "," + point.y.ToString("0.00"); return false; }
            }
            return true;
        }

        bool FullMainPanel => panelId == "task_information" && view && view.controller &&
            (view.controller.Flow?.IsTaskActive == true || (view.controller.Flow?.IsTutorial == true && !view.controller.ReferenceOpen));

        // requireView=false keeps a participant-chosen pose (restore/tab change) when it is world-clear.
        public void EnsureClear(bool requireView = true)
        {
            clearanceKnown = false;
            if (FixedWorkingSlate && placementAccepted)
            {
                bool clear = ReadableFace(transform.position, transform.rotation)
                    && (!requireView || ReadableFromHeadForDrag(transform.position, transform.rotation))
                    && PlacementClear(transform.position, transform.rotation);
                SetPlacementBlocked(!clear);
                return;
            }
            if (PlacementBusy)
            {
                if (!RecenterPending)
                { Recenter(false, false, onlyIfNeeded: true, reason: "clearance"); pendingRequireView = requireView; }
                return;
            }
            if (ReadableFace(transform.position, transform.rotation) && (!requireView || ReadableFromHeadForDrag(transform.position, transform.rotation)) && PlacementClear(transform.position, transform.rotation))
            {
                if (!RecenterPending) SetPlacementBlocked(false);
                return;
            }
            SetPlacementBlocked(true);
            if (!RecenterPending) Recenter(false, false);
        }

        // Nearer Reader pose on the current head-to-panel line, with the same stereo view and clearance checks as every placement.
        static readonly float[] CloserDistances = { 0.80f, 0.95f, 1.10f, 1.25f, 1.40f };
        static readonly float[] CloserHeightOffsets = { 0f, 0.10f, 0.20f, -0.08f };
        public bool TryCloserPose(out Pose pose)
        {
            pose = default;
            if (!view || !view.Head) return false;
            var head = view.Head.position; var line = transform.position - head;
            foreach (float distance in CloserDistances)
            {
                if (line.magnitude <= distance + .05f) continue;
                foreach (float dy in CloserHeightOffsets)
                {
                    var position = head + line.normalized * distance + Vector3.up * dy;
                    var flat = Vector3.ProjectOnPlane(position - head, Vector3.up);
                    var rotation = flat.sqrMagnitude > .0001f ? Quaternion.LookRotation(flat, Vector3.up) : transform.rotation;
                    bool viewOk = distance < 1.15f ? ReadableFromHeadForDrag(position, rotation) : FitsView(position, rotation);
                    if (viewOk && PlacementClear(position, rotation)) { pose = new Pose(position, rotation); return true; }
                }
            }
            return false;
        }

        public void RestorePose(Vector3 position, Quaternion rotation)
        {
            if (!view || !view.Head) return;
            if (FixedReader) { placementAccepted = false; LastRejection = "awaiting_saved_pose"; SetPlacementBlocked(true); }
            pendingRestore = new Pose(position, rotation);
            pendingOnlyIfNeeded = false;
            pendingReason = "saved_task_pose";
            QueueRecenter(false, false, "None");
            if (!PlacementBusy && (!Application.isPlaying || Application.isEditor))
                TryApplyPendingRecenter(true, true, true, Time.frameCount + 1);
        }

        void SetPlacementBlocked(bool blocked)
        {
            if (!blocked && FixedReader && !HasAcceptedPose) return;
            if (PlacementBlocked == blocked) return;
            var canvas = GetComponent<Canvas>();
            var raycaster = GetComponent<BaseRaycaster>();
            if (blocked)
            {
                view?.controller?.Log("PANEL_PLACEMENT_BLOCKED", panelId, "AwaitingClearPosition",
                    after: new { geometry_blocked = PlacementFailureKind == "geometry", rejection_kind = PlacementFailureKind, rejection = LastRejection }, source: "System", reason: LastRejection);
                canvasEnabledBeforeBlock = canvas && canvas.enabled;
                raycasterEnabledBeforeBlock = raycaster && raycaster.enabled;
            }
            if (canvas) canvas.enabled = !blocked && canvasEnabledBeforeBlock;
            if (raycaster) raycaster.enabled = !blocked && raycasterEnabledBeforeBlock;
            PlacementBlocked = blocked;
            if (!blocked) view?.controller?.Log("PANEL_PLACEMENT_RECOVERED", panelId, "ClearPositionRestored",
                after: new { pose = Pose(), readable = IsReadableFromHead }, source: "System", reason: "placement_restored");
            view?.NotifyReferenceVisibility(blocked ? "placement_concealed" : "placement_restored");
        }

        public bool PlacementClear(Vector3 position, Quaternion rotation)
        {
            clearanceKnown = false;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            PlacementCalls++;
            try
            {
                bool clear = CheckPlacement(position, rotation);
                if (!clear) PlacementRejections++;
                return clear;
            }
            finally { placementTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }

        bool CheckPlacement(Vector3 position, Quaternion rotation)
        {
            if (!view) return false;
            if (panelId == "recovery" && view.Head && view.Head.TryGetComponent<Camera>(out var recoveryCamera))
            {
                bool clear = recoveryCamera.stereoEnabled
                    ? RecoveryClearEye(position, rotation, recoveryCamera, Camera.MonoOrStereoscopicEye.Left)
                        && RecoveryClearEye(position, rotation, recoveryCamera, Camera.MonoOrStereoscopicEye.Right)
                    : RecoveryClearEye(position, rotation, recoveryCamera, Camera.MonoOrStereoscopicEye.Mono);
                if (!clear) return false;
            }
            var rect = ((RectTransform)transform).rect;
            CollectActionPoints();
            var min = new Vector3(rect.xMin, rect.yMin, 0);
            var max = new Vector3(rect.xMax, rect.yMax, 0);
            foreach (var point in actionPoints) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
            var localCentre = (min + max) * .5f;
            var centre = position + rotation * Vector3.Scale(localCentre, RestScale);
            var size = Vector3.Scale(max - min, RestScale);
            var half = new Vector3(Mathf.Abs(size.x) * .5f + .025f, Mathf.Abs(size.y) * .5f + .025f, Mathf.Abs(size.z) * .5f + .04f);
            var box = new Bounds(centre, Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        box.Encapsulate(centre + rotation * Vector3.Scale(half, new Vector3(x, y, z)));
            var other = panelId == "reference" ? view.mainPanel : view.referencePanel;
            Bounds? peerBounds = null;
            var otherGroup = other ? other.GetComponent<CanvasGroup>() : null;
            if (!idleCheck && (!otherGroup || otherGroup.alpha > .01f) && other && other.gameObject.activeInHierarchy && !other.PlacementBlocked && other.GetComponent<Canvas>().enabled)
            {
                ((RectTransform)other.transform).GetWorldCorners(otherCorners);
                var otherBox = new Bounds(otherCorners[0], Vector3.zero);
                foreach (var corner in otherCorners) otherBox.Encapsulate(corner);
                otherBox.Expand(.08f);
                if (box.Intersects(otherBox)) { LastRejection = "other_panel"; return false; }
                peerBounds = otherBox;
            }
            int overlaps = Physics.OverlapBoxNonAlloc(centre, half, overlapBuffer, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps == overlapBuffer.Length) { LastRejection = "overlap_buffer_full"; return false; }
            for (int i = 0; i < overlaps; i++)
                if (Blocks(overlapBuffer[i])) { LastRejection = "collider:" + overlapBuffer[i].name; return false; }
            if (environment == null)
                environment = gameObject.scene.GetRootGameObjects().Where(g => g.name.StartsWith("Environment_") || g.name == "ResearchFlow_EnvironmentSecondPass")
                    .SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .Concat(view.controller ? view.controller.taskPorts.Where(p => p).SelectMany(p => p.GetComponentsInChildren<Renderer>(true)) : Enumerable.Empty<Renderer>())
                    .Distinct().ToArray();
            foreach (var renderer in environment)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy
                    && renderer.bounds.size.x < 4 && renderer.bounds.size.z < 4 && box.Intersects(renderer.bounds) && !MovablePart(renderer))
                { LastRejection = "renderer:" + renderer.name; return false; }
            if (!view.Head) return true;
            sightCorners[0] = position + rotation * Vector3.Scale(new Vector3(min.x, min.y, localCentre.z), RestScale);
            sightCorners[1] = position + rotation * Vector3.Scale(new Vector3(min.x, max.y, localCentre.z), RestScale);
            sightCorners[2] = position + rotation * Vector3.Scale(new Vector3(max.x, max.y, localCentre.z), RestScale);
            sightCorners[3] = position + rotation * Vector3.Scale(new Vector3(max.x, min.y, localCentre.z), RestScale);
            var camera = view.Head.GetComponent<Camera>();
            if (camera && camera.stereoEnabled)
                return SightVolumeClear(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint(Vector3.zero), centre, rotation, box, peerBounds)
                    && SightVolumeClear(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint(Vector3.zero), centre, rotation, box, peerBounds);
            return SightVolumeClear(view.Head.position, centre, rotation, box, peerBounds);
        }

        bool SceneCollider(Collider collider) => collider && !collider.transform.IsChildOf(view.transform)
            && !collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
        bool Blocks(Collider collider) => SceneCollider(collider) && !(IgnoreMovableParts && collider.attachedRigidbody);
        bool MovablePart(Renderer renderer) => IgnoreMovableParts
            && (renderer.GetComponentInParent<Rigidbody>() || renderer.GetComponentInParent<ResearchS1Cable>());

        bool SightVolumeClear(Vector3 eye, Vector3 centre, Quaternion rotation, Bounds box, Bounds? peer)
        {
            var inside = (eye + centre) * .5f;
            for (int i = 0; i < 4; i++)
            {
                var plane = new Plane(eye, sightCorners[i], sightCorners[(i + 1) % 4]);
                if (plane.GetDistanceToPoint(inside) < 0) plane.Flip();
                sightVolume[i] = plane;
            }
            sightVolume[4] = new Plane((centre - eye).normalized, eye);
            sightVolume[5] = new Plane(-(rotation * Vector3.forward), centre);
            if (peer.HasValue && GeometryUtility.TestPlanesAABB(sightVolume, peer.Value))
            { LastRejection = "other_panel_occluded"; return false; }
            var reach = box; reach.Encapsulate(eye);
            int overlaps = Physics.OverlapBoxNonAlloc(reach.center, reach.extents, overlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps == overlapBuffer.Length) { LastRejection = "sight_buffer_full"; return false; }
            for (int i = 0; i < overlaps; i++)
            {
                var collider = overlapBuffer[i];
                if (Blocks(collider) && GeometryUtility.TestPlanesAABB(sightVolume, collider.bounds))
                { LastRejection = "occluded_collider:" + collider.name; return false; }
            }
            foreach (var renderer in environment)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy
                    && renderer.bounds.size.x < 4 && renderer.bounds.size.z < 4 && reach.Intersects(renderer.bounds)
                    && GeometryUtility.TestPlanesAABB(sightVolume, renderer.bounds) && !MovablePart(renderer))
                { LastRejection = "occluded:" + renderer.name; return false; }
            return true;
        }

        void CollectActionPoints()
        {
            actionPoints.Clear();
            GetComponentsInChildren(false, buttons);
            foreach (var button in buttons)
                if (button.isActiveAndEnabled && button.IsInteractable()) AddActionPoints((RectTransform)button.transform);
            if (!movable) return;
            GetComponentsInChildren(false, handles);
            foreach (var handle in handles)
                if (handle.isActiveAndEnabled) AddActionPoints((RectTransform)handle.transform);
        }

        bool RecoveryClearEye(Vector3 position, Quaternion rotation, Camera camera, Camera.MonoOrStereoscopicEye eye)
        {
            var rect = ((RectTransform)transform).rect;
            for (int horizontal = 0; horizontal < 2; horizontal++)
                for (int vertical = 0; vertical < 2; vertical++)
                    otherCorners[horizontal * 2 + vertical] = position + rotation * Vector3.Scale(
                        new Vector3(horizontal == 0 ? rect.xMin : rect.xMax, vertical == 0 ? rect.yMin : rect.yMax, 0), RestScale);
            if (!TryProjectedRect(camera, eye, out var candidate))
            { LastRejection = "recovery_projection:" + eye; return false; }
            return RecoveryClearOfPeer(candidate, view.mainPanel, camera, eye)
                && RecoveryClearOfPeer(candidate, view.referencePanel, camera, eye);
        }

        bool RecoveryClearOfPeer(Rect candidate, ResearchPanelMotion peer, Camera camera, Camera.MonoOrStereoscopicEye eye)
        {
            if (!peer || !peer.gameObject.activeInHierarchy || peer.PlacementBlocked) return true;
            var canvas = peer.GetComponent<Canvas>();
            var group = peer.GetComponent<CanvasGroup>();
            if (!canvas || !canvas.enabled || (group && group.alpha <= .01f)) return true;
            ((RectTransform)peer.transform).GetWorldCorners(otherCorners);
            if (!TryProjectedRect(camera, eye, out var projected) || !candidate.Overlaps(projected)) return true;
            LastRejection = "other_panel_projected:" + peer.panelId + ":" + eye;
            return false;
        }

        bool TryProjectedRect(Camera camera, Camera.MonoOrStereoscopicEye eye, out Rect rect)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in otherCorners)
            {
                var point = camera.WorldToViewportPoint(corner, eye);
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z) || point.z <= 0)
                { rect = default; return false; }
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            // Conservative bounds protect the entire rendered slate, including toolbar edges.
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        void AddActionPoints(RectTransform control)
        {
            control.GetWorldCorners(otherCorners);
            foreach (var corner in otherCorners) actionPoints.Add(transform.InverseTransformPoint(corner));
            actionPoints.Add(transform.InverseTransformPoint((otherCorners[0] + otherCorners[2]) * .5f));
        }
    }
}
