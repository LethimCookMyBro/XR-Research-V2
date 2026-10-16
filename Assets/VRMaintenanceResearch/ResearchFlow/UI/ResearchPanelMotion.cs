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
        readonly Collider[] overlapBuffer = new Collider[64];
        readonly Vector3[] otherCorners = new Vector3[4];
        readonly List<Button> buttons = new List<Button>();
        readonly List<ResearchDragHandle> handles = new List<ResearchDragHandle>();
        readonly List<Vector3> actionPoints = new List<Vector3>(), sightTargets = new List<Vector3>(9);
        long placementTicks;
        public int PlacementCalls { get; private set; }
        public int PlacementRejections { get; private set; }
        public double PlacementMilliseconds => placementTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        bool canvasEnabledBeforeBlock, raycasterEnabledBeforeBlock;
        float nextBlockedRetry;
        float nextClearanceCheck;
        bool clearanceKnown, clearanceValue, clearancePeerVisible;
        Pose clearanceHead, clearancePanel, clearancePeer;
        Rect clearanceRect, clearancePeerRect;
        Vector3 clearanceScale, clearancePeerScale;
        bool deferredRetryUsed = true;
        Pose? anchoredPose;
        bool AtStationAnchor => anchoredPose.HasValue && ResearchUIPolicy.StationAnchoredPanels
            && (transform.position - anchoredPose.Value.position).sqrMagnitude < 1e-8f && Quaternion.Angle(transform.rotation, anchoredPose.Value.rotation) < .01f;
        bool faceHidden;
        float faceTransitionSince = float.NegativeInfinity;
        Pose? pendingRestore;
        Pose? deferredViewportHead;
        bool pendingOnlyIfNeeded, pendingRequireView = true;
        string pendingReason = "recenter";
        bool PlacementBusy => IsDragging || (view && view.PanelPlacementBusy);
        public bool PlacementBlocked { get; private set; }
        public bool WorldPlacementBlocked => PlacementBlocked && !faceHidden;
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
            pendingRestore = null;
            deferredViewportHead = null;
            pendingOnlyIfNeeded = onlyIfNeeded; pendingRequireView = true;
            pendingReason = reason;
            deferredRetryUsed = false;
            bool busy = PlacementBusy;
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
        void OnDisable() { RecenterPending = false; pendingRestore = null; deferredViewportHead = null; clearanceKnown = false; }
        void OnRectTransformDimensionsChange() { clearanceKnown = false; }
        void OnTransformChildrenChanged() { clearanceKnown = false; }
        bool IdlePlacementClear()
        {
            var rect = ((RectTransform)transform).rect;
            var peer = panelId == "reference" ? view.mainPanel : view.referencePanel;
            bool peerVisible = peer && peer.gameObject.activeInHierarchy && !peer.PlacementBlocked;
            var peerRect = peerVisible ? ((RectTransform)peer.transform).rect : default;
            bool changed = !clearanceKnown || Vector3.Distance(clearanceHead.position, view.Head.position) >= .005f
                || Quaternion.Angle(clearanceHead.rotation, view.Head.rotation) >= .5f
                || clearancePanel.position != transform.position || clearancePanel.rotation != transform.rotation
                || clearanceRect != rect || clearanceScale != RestScale || clearancePeerVisible != peerVisible
                || (peerVisible && (clearancePeer.position != peer.transform.position || clearancePeer.rotation != peer.transform.rotation
                    || clearancePeerRect != peerRect || clearancePeerScale != peer.transform.lossyScale));
            if (!changed && Time.unscaledTime < nextClearanceCheck) return clearanceValue;
            clearanceHead = new Pose(view.Head.position, view.Head.rotation);
            clearancePanel = new Pose(transform.position, transform.rotation);
            clearanceRect = rect; clearanceScale = RestScale; clearancePeerVisible = peerVisible;
            if (peerVisible)
            {
                clearancePeer = new Pose(peer.transform.position, peer.transform.rotation);
                clearancePeerRect = peerRect; clearancePeerScale = peer.transform.lossyScale;
            }
            nextClearanceCheck = Time.unscaledTime + .1f;
            clearanceValue = PlacementClear(transform.position, transform.rotation);
            clearanceKnown = true;
            return clearanceValue;
        }
        void LateUpdate()
        {
            if (!RecenterPending && !IsDragging && view && view.Head)
            {
                var sight = transform.position - view.Head.position;
                float facing = sight.sqrMagnitude > .0001f ? Vector3.Dot(transform.forward, sight.normalized) : -1;
                bool clear = IdlePlacementClear();
                // A station anchor is a fixed authored pose: a part the participant carries across the sight line must not move or hide it.
                if (!clear && AtStationAnchor && LastRejection.StartsWith("occluded", System.StringComparison.Ordinal)) clear = true;
                if (!clear) { faceHidden = false; SetPlacementBlocked(true); faceTransitionSince = float.NegativeInfinity; }
                else
                {
                    if (PlacementBlocked && !faceHidden && facing >= .58f) SetPlacementBlocked(false);
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
            // While concealed for clearance, keep searching the candidate poses (about once a second); a single retry could leave
            // the panel hidden for good, e.g. Inspect feedback behind the PC case with the head still (no Continue, no Pause).
            if (PlacementBlocked && !faceHidden && !RecenterPending && view && view.Head && Time.unscaledTime >= nextBlockedRetry)
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
                if (ReadableFace(pose.position, pose.rotation) && PlacementClear(pose.position, pose.rotation))
                {
                    var before = Pose();
                    bool moved = Vector3.Distance(transform.position, pose.position) > .0001f || Quaternion.Angle(transform.rotation, pose.rotation) > .01f;
                    transform.SetPositionAndRotation(pose.position, pose.rotation);
                    faceHidden = false; SetPlacementBlocked(false);
                    if (moved) view.controller?.Log("PANEL_PLACEMENT", panelId, "Restored", before, Pose(), source: "System", reason: "saved_task_pose");
                    return;
                }
            }
            if (pendingOnlyIfNeeded && ReadableFace(transform.position, transform.rotation)
                && (!pendingRequireView || FitsView(transform.position, transform.rotation)) && PlacementClear(transform.position, transform.rotation))
            { faceHidden = false; deferredRetryUsed = true; SetPlacementBlocked(false); return; }
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
            // Fixed-UI policy: working slates go to their station anchor first (same clearance/view checks); head-relative
            // candidates below remain only as a logged System fallback so a blocked anchor never hides the panel.
            string anchorId = null, anchorFallback = null;
            found = TryStationAnchor(out position, out rotation, out anchorId, out anchorFallback);
            // A low eye pose or tall assembly can block the slate. Try bounded
            // height/distance alternatives before turning it farther out of view.
            if (!found) foreach (float angle in angles)
            {
                // The labeled recovery strip must not spawn through a resting hand and acquire a near poke.
                foreach (float distance in panelId == "recovery" ? new[] { .9f, 1.05f, 1.2f } : centered ? new[] { 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.2f } : FullMainPanel ? new[] { 1.2f, 1.4f, 1.6f, 1.8f, 2f } : panelId == "reference" ? new[] { 1.2f, 1.4f, 1.6f, 1.8f } : new[] { 1.2f })
                {
                    // At a native .99 m eye pose, no original Tutorial candidate was both visible and clear near the worktop.
                    foreach (float height in panelId == "recovery" ? new[] { -.25f, -.4f, .2f } : panelId == "reference" || FullMainPanel ? new[] { -.1f, .2f, .4f } : new[] { -.1f, .2f })
                    {
                        var direction = Quaternion.AngleAxis(side * angle, Vector3.up) * forward;
                        rotation = Quaternion.LookRotation(direction, Vector3.up);
                        position = view.Head.position + direction * distance + Vector3.up * height;
                        if (FitsView(position, rotation) && PlacementClear(position, rotation)) { found = true; break; }
                    }
                    if (found) break;
                }
                if (found) break;
            }
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
            anchoredPose = anchorId != null ? new Pose(position, rotation) : (Pose?)null;
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
                // A task anchor is a fixed world pose: it must face the head and be clear, but need not be inside the current gaze.
                // Training keeps the in-view requirement because the trainee walks/teleports between practice instructions.
                bool viewOk = owner.Flow != null && owner.Flow.IsTutorial ? FitsView(p, r) : ReadableFace(p, r);
                if (viewOk && PlacementClear(p, r)) { position = p; rotation = r; anchorId = pose.Id; return true; }
                fallback = pose.Id + ":" + LastRejection;
            }
            return false;
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

        bool FitsView(Vector3 position, Quaternion rotation)
        {
            if (!ReadableFace(position, rotation)) return false;
            var camera = view.Head.GetComponent<Camera>();
            if (!camera) return true;
            // Mono can accept a slate clipped in one native eye. Validate both actual stereo transforms.
            return camera.stereoEnabled
                ? FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Left)
                    && FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Right)
                : FitsViewEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Mono);
        }

        bool FitsViewEye(Vector3 position, Quaternion rotation, Camera camera, Camera.MonoOrStereoscopicEye eye)
        {
            var rect = ((RectTransform)transform).rect;
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
            return true;
        }

        bool FullMainPanel => panelId == "task_information" && view && view.controller &&
            (view.controller.Flow?.IsTaskActive == true || (view.controller.Flow?.IsTutorial == true && !view.controller.ReferenceOpen));

        // requireView=false keeps a participant-chosen pose (restore/tab change) when it is world-clear.
        public void EnsureClear(bool requireView = true)
        {
            clearanceKnown = false;
            if (PlacementBusy)
            {
                if (!RecenterPending)
                { Recenter(false, false, onlyIfNeeded: true, reason: "clearance"); pendingRequireView = requireView; }
                return;
            }
            if (ReadableFace(transform.position, transform.rotation) && (!requireView || FitsView(transform.position, transform.rotation)) && PlacementClear(transform.position, transform.rotation))
            {
                if (!RecenterPending) SetPlacementBlocked(false);
                return;
            }
            SetPlacementBlocked(true);
            if (!RecenterPending) Recenter(false, false);
        }

        // Nearer Reader pose on the current head-to-panel line, with the same stereo view and clearance checks as every placement.
        static readonly float[] CloserDistances = { 1.0f, 1.15f, 1.3f };
        public bool TryCloserPose(out Pose pose)
        {
            pose = default;
            if (!view || !view.Head) return false;
            var head = view.Head.position; var line = transform.position - head;
            foreach (float distance in CloserDistances)
            {
                if (line.magnitude <= distance + .05f) continue;
                var position = head + line.normalized * distance;
                var flat = Vector3.ProjectOnPlane(position - head, Vector3.up);
                var rotation = flat.sqrMagnitude > .0001f ? Quaternion.LookRotation(flat, Vector3.up) : transform.rotation;
                if (FitsView(position, rotation) && PlacementClear(position, rotation)) { pose = new Pose(position, rotation); return true; }
            }
            return false;
        }

        public void RestorePose(Vector3 position, Quaternion rotation)
        {
            if (!view || !view.Head) return;
            pendingRestore = new Pose(position, rotation);
            pendingOnlyIfNeeded = false;
            pendingReason = "saved_task_pose";
            QueueRecenter(false, false, "None");
            if (!PlacementBusy && (!Application.isPlaying || Application.isEditor))
                TryApplyPendingRecenter(true, true, true, Time.frameCount + 1);
        }

        void SetPlacementBlocked(bool blocked)
        {
            if (PlacementBlocked == blocked) return;
            var canvas = GetComponent<Canvas>();
            var raycaster = GetComponent<BaseRaycaster>();
            if (blocked)
            {
                view?.controller?.Log("PANEL_PLACEMENT_BLOCKED", panelId, "AwaitingClearPosition", source: "System", reason: LastRejection);
                canvasEnabledBeforeBlock = canvas && canvas.enabled;
                raycasterEnabledBeforeBlock = raycaster && raycaster.enabled;
            }
            if (canvas) canvas.enabled = !blocked && canvasEnabledBeforeBlock;
            if (raycaster) raycaster.enabled = !blocked && raycasterEnabledBeforeBlock;
            PlacementBlocked = blocked;
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
            // Recovery is closer than the reading slates: separate world planes can still cover their text/actions.
            if (panelId == "recovery" && view.Head && view.Head.TryGetComponent<Camera>(out var camera))
            {
                bool clear = camera.stereoEnabled
                    ? RecoveryClearEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Left)
                        && RecoveryClearEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Right)
                    : RecoveryClearEye(position, rotation, camera, Camera.MonoOrStereoscopicEye.Mono);
                if (!clear) return false;
            }
            var rect = (RectTransform)transform;
            var half = new Vector3(Mathf.Abs(rect.rect.width * RestScale.x) * .5f + .025f,
                Mathf.Abs(rect.rect.height * RestScale.y) * .5f + .025f, .04f);
            var box = new Bounds(position, Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        box.Encapsulate(position + rotation * Vector3.Scale(half, new Vector3(x, y, z)));
            var other = panelId == "reference" ? view.mainPanel : view.referencePanel;
            if (other && other.gameObject.activeInHierarchy && !other.PlacementBlocked)
            {
                ((RectTransform)other.transform).GetWorldCorners(otherCorners);
                var otherBox = new Bounds(otherCorners[0], Vector3.zero);
                foreach (var corner in otherCorners) otherBox.Encapsulate(corner);
                otherBox.Expand(.08f);
                if (box.Intersects(otherBox)) { LastRejection = "other_panel"; return false; }
            }
            int overlaps = Physics.OverlapBoxNonAlloc(position, half, overlapBuffer, rotation, ~0, QueryTriggerInteraction.Ignore);
            // A full buffer may omit a blocking collider; fail closed rather than accept an incomplete query.
            if (overlaps == overlapBuffer.Length) { LastRejection = "overlap_buffer_full"; return false; }
            for (int i = 0; i < overlaps; i++)
            {
                var collider = overlapBuffer[i];
                if (!collider.transform.IsChildOf(view.transform) && !collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>()) { LastRejection = "collider:" + collider.name; return false; }
            }
            if (environment == null)
                environment = gameObject.scene.GetRootGameObjects().Where(g => g.name.StartsWith("Environment_") || g.name == "ResearchFlow_EnvironmentSecondPass")
                    .SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .Concat(view.controller ? view.controller.taskPorts.Where(p => p).SelectMany(p => p.GetComponentsInChildren<Renderer>(true)) : Enumerable.Empty<Renderer>())
                    .Distinct().ToArray();
            actionPoints.Clear();
            GetComponentsInChildren(false, buttons);
            foreach (var button in buttons)
                if (button.isActiveAndEnabled && button.IsInteractable()) AddActionPoints((RectTransform)button.transform);
            if (movable)
            {
                GetComponentsInChildren(false, handles);
                foreach (var handle in handles)
                    if (handle.isActiveAndEnabled) AddActionPoints((RectTransform)handle.transform);
            }
            // Every sight line below starts at the head and ends at a panel point, so a renderer outside the box spanning
            // the head, the panel and its action points cannot block anything. Skipping those keeps the result identical
            // and removed a ~10 ms main-thread hitch every 0.1 s on Quest (338 renderers x 9+ rays each check).
            var head = view.Head;
            var reach = box;
            sightTargets.Clear();
            if (head)
            {
                reach.Encapsulate(head.position);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        sightTargets.Add(position + rotation * Vector3.Scale(new Vector3(x * rect.rect.width * .35f, y * rect.rect.height * .35f, 0), RestScale));
                foreach (var point in actionPoints) reach.Encapsulate(position + rotation * Vector3.Scale(point, RestScale));
            }
            // Large room-shell bounds enclose empty space; colliders handle those surfaces.
            foreach (var renderer in environment)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var bounds = renderer.bounds;
                if (bounds.size.x >= 4 || bounds.size.z >= 4 || !reach.Intersects(bounds)) continue;
                if (box.Intersects(bounds)) { LastRejection = "renderer:" + renderer.name; return false; }
                if (!head) continue;
                // Keep text readable, and independently protect the actual control rects near panel edges.
                foreach (var target in sightTargets)
                    if (Blocks(bounds, head.position, target)) { LastRejection = "occluded:" + renderer.name; return false; }
                foreach (var point in actionPoints)
                    if (Blocks(bounds, head.position, position + rotation * Vector3.Scale(point, RestScale)))
                    { LastRejection = "occluded_action:" + renderer.name; return false; }
            }
            return true;
        }

        static bool Blocks(Bounds bounds, Vector3 from, Vector3 to)
        {
            var sight = to - from;
            return bounds.IntersectRay(new Ray(from, sight.normalized), out float hit) && hit < sight.magnitude - .08f;
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
