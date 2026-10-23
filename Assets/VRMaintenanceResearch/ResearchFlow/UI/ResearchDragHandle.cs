using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Input;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public ResearchPanelMotion panel;
        // One-Euro on the panel TARGET only (raw ray/tracking untouched): ~64 ms time constant while nearly
        // still (mm of lag), rising cutoff with speed so fast drags follow within one or two frames.
        public static readonly OneEuroFilterPropertyBlock TargetFilter = new OneEuroFilterPropertyBlock(2.5f, 6f, 1f);
        // Technical A/B only (adb "drag_filter"): the wearer compares filtered vs unfiltered drags blind.
        public static bool FilterEnabled = true;
        // Tutorial "move the panel" step: pulse this handle as the one obvious target.
        [System.NonSerialized] public bool highlight;
        int hovers;
        // Per-drag frame telemetry (Quest drag was physically FAIL with and without the filter).
        int frames, hitchFrames, stillFrames, rayNotSelectingFrames, ignoredPointerUps, gcBefore;
        float maxFrameMs, maxStepMm, maxRawStepMm, maxYawStepDeg, maxHeadStepMm;
        double stillStepSquares;
        Vector3 lastHead; bool hasHead;
        string releaseCause, rayStateAtRelease;
        readonly IOneEuroFilter<Vector3> filter = OneEuroFilter.CreateVector3();
        Vector3 lastRaw; bool hasRaw;
        double lagMetres, lagSpeed, maxError;
        int filteredSamples;
        Plane plane;
        Vector3 offset;
        Quaternion dragRotation;
        object before;
        int pointer;
        string input, actionId;
        long instructionEpoch;
        VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabInputGuard dragGuard;
        bool trackingAtStart;
        bool dragging;
        bool moved;
        bool nativeDrag;
        RayInteractor rayInteractor;
        float rayDistance;
        Vector3 startRayOrigin, startRayForward, startPanelPosition;
        Vector3 lastClearPosition;
        Quaternion lastClearRotation;
        bool hasClearPose;
        float nextClearanceCheck;
        long moveTicks, maxMoveTicks;
        int placementCallsBefore, placementRejectedBefore;
        double placementMsBefore;
        public bool IsDragging => dragging;
        public Vector3 DesiredPosition => lastRaw; // unfiltered panel target (motion trace compares it with the rendered pose)
        public int MoveSamples { get; private set; }
        public int RejectedSamples { get; private set; }
        public bool ReleaseRecovered { get; private set; }
        public double MoveMilliseconds => moveTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
        public double MaxMoveMilliseconds => maxMoveTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;

        public void OnPointerDown(PointerEventData data)
        {
            // Fixed-position UI policy: the handle object is deactivated by the view; this guard keeps any stale event inert.
            if (!ResearchUIPolicy.PanelDragAllowed || !panel || !panel.movable || !panel.view.Head || dragging) return;
            rayInteractor = null;
            foreach (var ray in FindObjectsByType<RayInteractor>(FindObjectsSortMode.None))
                if (ray.isActiveAndEnabled && ray.Identifier == data.pointerId) { rayInteractor = ray; break; }
            // Meta's event camera follows the canvas. Use the owning native ray
            // so moving the slate cannot feed back into its drag coordinates.
            if (!rayInteractor) return;
            rayDistance = Mathf.Max(.01f, Vector3.Dot(transform.position - rayInteractor.Origin, rayInteractor.Forward));
            startRayOrigin = rayInteractor.Origin;
            startRayForward = rayInteractor.Forward;
            Begin(data, rayInteractor.Ray.GetPoint(rayDistance));
        }

        void Begin(PointerEventData data, Vector3 point)
        {
            if (panel.view.controller && !panel.view.controller.LocalUICommandsAllowed) return;
            dragging = true; moved = false; pointer = data.pointerId; before = panel.Pose(); offset = panel.transform.position - point;
            startPanelPosition = panel.transform.position;
            dragRotation = panel.transform.rotation;
            nativeDrag = rayInteractor;
            input = panel.view.controller?.InputSourceForPointer(pointer) ?? "None";
            var owner = panel.view.controller;
            actionId = owner?.Logger?.NewActionId();
            dragGuard = owner?.GuardForPointer(pointer);
            trackingAtStart = dragGuard ? dragGuard.TrackingValid : ResearchFloorPolicy.EditorFixtureAllowed;
            instructionEpoch = panel.panelId == "reference" ? owner?.TutorialActionAttempted("reposition", actionId, input, trackingAtStart) ?? 0 : 0;
            MoveSamples = RejectedSamples = 0; moveTicks = maxMoveTicks = 0; ReleaseRecovered = false;
            placementCallsBefore = panel.PlacementCalls; placementRejectedBefore = panel.PlacementRejections;
            placementMsBefore = panel.PlacementMilliseconds;
            hasClearPose = false;
            filter.SetProperties(TargetFilter); filter.Reset(); hasRaw = false;
            lagMetres = lagSpeed = maxError = 0; filteredSamples = 0;
            frames = hitchFrames = stillFrames = rayNotSelectingFrames = ignoredPointerUps = 0; gcBefore = System.GC.CollectionCount(0);
            maxFrameMs = maxStepMm = maxRawStepMm = maxYawStepDeg = maxHeadStepMm = 0; stillStepSquares = 0; hasHead = false;
            releaseCause = rayStateAtRelease = null;
            RememberClearPose();
            ResearchUIFeedback.TryPulse(panel.view.controller, pointer);
        }

        void Update()
        {
            // CanvasRenderer tint (like Button transitions): per-frame state/pulse without dirtying the canvas mesh.
            var grip = transform.Find("GripLine"); var graphic = grip ? grip.GetComponent<Graphic>() : null;
            if (graphic) graphic.canvasRenderer.SetColor(dragging || hovers > 0 ? ResearchUITheme.Hover
                : highlight ? Color.Lerp(ResearchUITheme.Ink, ResearchUITheme.Warning, .35f + .35f * ResearchMotion.Ease(Mathf.PingPong(Time.unscaledTime / .8f, 1)))
                : Color.Lerp(ResearchUITheme.Muted, ResearchUITheme.Ink, .65f));
            if (dragging && (!ResearchUIPolicy.PanelDragAllowed || !panel || !panel.movable || !panel.view || !panel.view.Head)) { Finish(false, "panel_unavailable"); return; }
            // Recheck a stationary held pose when scene geometry changes.
            if (dragging && Time.unscaledTime >= nextClearanceCheck) RememberClearPose();
        }

        void RememberClearPose()
        {
            nextClearanceCheck = Time.unscaledTime + .1f;
            if (!panel || !panel.ReadableFromHeadForDrag(panel.transform.position, panel.transform.rotation)
                || !panel.PlacementClear(panel.transform.position, panel.transform.rotation)) return;
            lastClearPosition = panel.transform.position; lastClearRotation = panel.transform.rotation; hasClearPose = true;
        }

        void LateUpdate()
        {
            if (!dragging || !nativeDrag) return;
            if (!rayInteractor || !rayInteractor.isActiveAndEnabled || !panel || !panel.movable || !panel.view || !panel.view.Head) { Finish(false, "ray_inactive"); return; }
            // The owning ray's select state is the trigger truth; the canvas pointer-up can arrive a frame later.
            if (rayInteractor.State != InteractorState.Select) { Finish(true, "ray_released"); return; }
            float handPull = Vector3.Dot(rayInteractor.Origin - startRayOrigin, startRayForward);
            float effectiveDistance = Mathf.Clamp(rayDistance + handPull * 2f, .45f, 2f);
            Move(rayInteractor.Ray.GetPoint(effectiveDistance));
        }

        public void OnPointerUp(PointerEventData data) => OnEndDrag(data);
        public void OnPointerEnter(PointerEventData data) => hovers++;
        public void OnPointerExit(PointerEventData data) => hovers = Mathf.Max(0, hovers - 1);
        public void OnBeginDrag(PointerEventData data)
        {
            if (!ResearchUIPolicy.PanelDragAllowed || dragging || !panel || !panel.movable || !panel.view.Head) return;
            plane = new Plane(panel.transform.forward, panel.transform.position);
            if (!Point(data, out var point)) return;
            Begin(data, point);
        }
        public void OnDrag(PointerEventData data)
        {
            if (!dragging || rayInteractor || data.pointerId != pointer || !panel.movable || !Point(data, out var point)) return;
            Move(point);
        }
        void Move(Vector3 point) => MoveAt(point, Time.unscaledDeltaTime);
        void MoveAt(Vector3 point, float dt)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            MoveSamples++;
            try
            {
                var head = panel.view.Head; var delta = point + offset - head.position;
                if (delta.sqrMagnitude < .01f || float.IsNaN(delta.sqrMagnitude) || float.IsInfinity(delta.sqrMagnitude)) { RejectedSamples++; return; }
                var raw = head.position + delta.normalized * Mathf.Clamp(delta.magnitude, .35f, 2.1f); // Includes the 2m recenter fallback plus its vertical offset.
                raw.y = Mathf.Clamp(raw.y, head.position.y - .65f, head.position.y + .45f);
                float rawStep = hasRaw ? Vector3.Distance(lastRaw, raw) : 0;
                var position = Stabilize(raw, dt);
                var rotation = dragRotation;
                if (ResearchUIPolicy.Active == ResearchUIPolicy.DevMovable && Vector3.Distance(raw, startPanelPosition) > .02f)
                {
                    var look = Vector3.ProjectOnPlane(position - head.position, Vector3.up);
                    if (look.sqrMagnitude > .01f) rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                }
                if (!panel.ReadableFromHeadForDrag(position, rotation) || !panel.PlacementClear(position, rotation))
                { RejectedSamples++; Sample(rawStep, panel.transform.position, panel.transform.rotation, head.position, dt); return; }
                Sample(rawStep, position, rotation, head.position, dt);
                moved |= Vector3.Distance(panel.transform.position, position) > .001f || Quaternion.Angle(panel.transform.rotation, rotation) > .1f;
                panel.transform.SetPositionAndRotation(position, rotation);
                lastClearPosition = position; lastClearRotation = rotation; hasClearPose = true;
            }
            finally
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                moveTicks += elapsed; if (elapsed > maxMoveTicks) maxMoveTicks = elapsed;
            }
        }
        Vector3 Stabilize(Vector3 raw, float dt)
        {
            var filtered = FilterEnabled ? filter.Step(raw, dt > 0 ? dt : 1f / 72) : raw;
            if (hasRaw && dt > 0)
            {
                double speed = (raw - lastRaw).magnitude / dt, error = (raw - filtered).magnitude;
                if (speed > .05) { lagMetres += error; lagSpeed += speed; }
                if (error > maxError) maxError = error;
                filteredSamples++;
            }
            lastRaw = raw; hasRaw = true;
            return filtered;
        }
        // Mean target-vs-rendered distance over mean speed while moving: the filter's added latency.
        public double FilterLatencyMilliseconds => lagSpeed > 0 ? lagMetres / lagSpeed * 1000 : 0;
        public double FilterMaxErrorMillimetres => maxError * 1000;

        public void OnEndDrag(PointerEventData data)
        {
            if (!dragging || data == null || data.pointerId != pointer) return;
            if (nativeDrag && (!rayInteractor || !rayInteractor.isActiveAndEnabled)) { Finish(false, "ray_inactive"); return; }
            // Trigger still held on the owning ray: a canvas-side pointer-up is not the participant's release.
            if (nativeDrag && rayInteractor && rayInteractor.isActiveAndEnabled && rayInteractor.State == InteractorState.Select) { ignoredPointerUps++; return; }
            Finish(true, "pointer_up");
        }
        void Finish(bool released = false, string cause = "cancelled")
        {
            string source = released ? "User" : "System";
            releaseCause = cause; rayStateAtRelease = rayInteractor ? rayInteractor.State.ToString() : "none";
            dragging = false; rayInteractor = null;
            if (!panel || !panel.view) return;
            if (!panel.ReadableFromHeadForDrag(panel.transform.position, panel.transform.rotation)
                || !panel.PlacementClear(panel.transform.position, panel.transform.rotation))
            {
                ReleaseRecovered = true;
                if (hasClearPose) panel.RestorePose(lastClearPosition, lastClearRotation);
                panel.EnsureClear();
            }
            else if (released && moved && panel.panelId == "reference")
                panel.view.SaveReaderTaskPose(panel.transform.position, panel.transform.rotation);
            panel.view.controller?.Log("PANEL_DRAG_METRICS", panel.panelId, ReleaseRecovered ? "RecoveredClearance" : "Clear", after: new
            {
                move_samples = MoveSamples, rejected_samples = RejectedSamples, move_ms = MoveMilliseconds, max_move_ms = MaxMoveMilliseconds,
                placement_calls = panel.PlacementCalls - placementCallsBefore, placement_rejections = panel.PlacementRejections - placementRejectedBefore,
                placement_ms = panel.PlacementMilliseconds - placementMsBefore,
                filter = FilterEnabled ? "OneEuro(min_cutoff=2.5Hz,beta=6,d_cutoff=1Hz)" : "none", filtered_samples = filteredSamples,
                filter_latency_ms = FilterLatencyMilliseconds, filter_max_error_mm = FilterMaxErrorMillimetres,
                release_cause = releaseCause, ray_state_at_release = rayStateAtRelease, ignored_pointer_ups = ignoredPointerUps,
                ray_not_selecting_frames = rayNotSelectingFrames, frames, max_frame_ms = maxFrameMs, hitch_frames = hitchFrames,
                max_step_mm = maxStepMm, max_raw_step_mm = maxRawStepMm, max_yaw_step_deg = maxYawStepDeg, max_head_step_mm = maxHeadStepMm,
                still_frames = stillFrames, still_jitter_rms_mm = stillFrames > 0 ? System.Math.Sqrt(stillStepSquares / stillFrames) : 0,
                gc_collections = System.GC.CollectionCount(0) - gcBefore, hardware_verified = false
            }, source: source, inputSource: input, reason: cause, actionId: actionId);
            panel.view.controller?.Log("PANEL_REPOSITION", panel.panelId, !released ? "Cancelled" : moved ? "Moved" : "Unchanged", before, panel.Pose(), source: source, inputSource: input, reason: cause, actionId: actionId);
            if (released && moved && panel.panelId == "reference")
                panel.view.controller?.TutorialActionCommitted("reposition", instructionEpoch, actionId, input,
                    trackingAtStart && (dragGuard ? dragGuard.TrackingValid && dragGuard.InteractionsAllowed && !dragGuard.SystemCancelling : ResearchFloorPolicy.EditorFixtureAllowed));
        }
        void OnDisable() { hovers = 0; if (dragging) Finish(false, "disabled"); }

        // Rendered-vs-target steps per frame; "still" = target moving < 5 cm/s, where any rendered motion is jitter.
        void Sample(float rawStepMetres, Vector3 rendered, Quaternion rotation, Vector3 head, float dt)
        {
            frames++;
            float ms = dt * 1000; if (ms > maxFrameMs) maxFrameMs = ms; if (ms > 1.5f * 1000 / 72) hitchFrames++;
            if (rayInteractor && rayInteractor.State != InteractorState.Select) rayNotSelectingFrames++;
            if (hasHead)
            {
                float step = Vector3.Distance(panel.transform.position, rendered) * 1000, rawStep = rawStepMetres * 1000;
                maxStepMm = Mathf.Max(maxStepMm, step); maxRawStepMm = Mathf.Max(maxRawStepMm, rawStep);
                maxYawStepDeg = Mathf.Max(maxYawStepDeg, Quaternion.Angle(panel.transform.rotation, rotation));
                maxHeadStepMm = Mathf.Max(maxHeadStepMm, Vector3.Distance(lastHead, head) * 1000);
                if (dt > 0 && rawStep / 1000 / dt < .05f) { stillFrames++; stillStepSquares += step * step; }
            }
            lastHead = head; hasHead = true;
        }
        bool Point(PointerEventData data, out Vector3 point)
        {
            point = default;
            if (!data.pressEventCamera) return false;
            var ray = data.pressEventCamera.ScreenPointToRay(data.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance); return true;
        }
    }

}
