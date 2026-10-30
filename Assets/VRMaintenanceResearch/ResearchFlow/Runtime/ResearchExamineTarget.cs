using System;
using System.Collections.Generic;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Neutral physical-target focus; only an explicit chip press records EXAMINE.</summary>
    public sealed class ResearchExamineTarget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public ResearchSessionFlowController controller;
        public ResearchTaskPort task;
        public ResearchTrainingStation training; // Training practice target instead of a task port
        public string stableTarget;
        public int targetKind;
        public Canvas stationCanvas;
        public CanvasGroup stationGroup;
        public GameObject[] interactionRoots = Array.Empty<GameObject>();
        [SerializeField] Button button;
        [SerializeField] TMP_Text label;
        [SerializeField] Collider[] focusColliders = Array.Empty<Collider>();
        [SerializeField] Transform focusAnchor;
        [SerializeField] Vector3 chipOffset = new Vector3(.08f, .10f, 0);
        static readonly List<ResearchExamineTarget> Targets = new List<ResearchExamineTarget>();
        static ResearchExamineTarget focused;
        static RayInteractor[] rays;
        float lastFocusTime;
        int hoveringPointer = int.MinValue;
        int pressedPointer = int.MinValue;
        string pointerSource = "", shownLanguage = "";
        readonly Dictionary<int, (string context, string source)> presses = new Dictionary<int, (string, string)>();
        string releasedContext, observedInformation;
        float observationUntil;
        RectTransform connector;
        readonly Vector3[] screenCorners = new Vector3[4];
        readonly Plane[] targetFrustum = new Plane[6];

        public bool Available => controller && controller.UsesDirectPartFeedback ? false : training ? isActiveAndEnabled && training.Active : isActiveAndEnabled && controller && task && controller.Flow != null && controller.Flow.IsTaskActive
            && controller.Flow.CanInteract && controller.Flow.CurrentTask == task.taskId && task.InteractionAllowed
            && !controller.Transitioning && string.IsNullOrEmpty(controller.Error);
        public bool ChipVisible => false;

        public static ResearchExamineTarget[] AvailableOnMain(ResearchSessionFlowController owner)
        {
            return Targets.FindAll(t => t && t.controller == owner && t.Available).ToArray();
        }

        public static bool TryExamineFromMain(ResearchSessionFlowController owner, string targetId, string input, out string information)
        {
            information = null;
            if (!owner || !owner.LocalUICommandsAllowed || string.IsNullOrEmpty(input) ||
                (!owner.IsTechnical && (input == "None" || input.StartsWith("Simulated", StringComparison.Ordinal)))) return false;
            var target = Targets.Find(t => t && t.controller == owner && t.stableTarget == targetId && t.Available);
            if (!target || (target.training && owner.TutorialArmedFact != "examine")) return false;
            information = target.training ? target.training.ExamineInfo(targetId, owner.Language) : target.task.ExamineInfo(targetId, owner.Language);
            if (target.training) target.training.Examine(targetId, input);
            else target.task.Examine(targetId, input);
            return string.IsNullOrEmpty(owner.Error);
        }

        // Each target needs its own compact canvas and SDK canvas interactions. No invisible focus canvas.
        public void ConfigureFocus(Collider[] colliders, Transform anchor)
        {
            focusColliders = colliders ?? Array.Empty<Collider>();
            focusAnchor = anchor;
        }

        void OnEnable()
        {
            if (!button) button = GetComponent<Button>();
            if (!label) label = GetComponentInChildren<TMP_Text>(true);
            if (button && stationCanvas)
            {
                var rect = (RectTransform)button.transform; rect.sizeDelta = new Vector2(240, 96);
                ((RectTransform)stationCanvas.transform).sizeDelta = rect.sizeDelta;
                if (label) { label.rectTransform.sizeDelta = new Vector2(220, 88); label.fontSize = 28; }
            }
            if (button) { ResearchUITheme.Button(button, ResearchButtonVariant.Secondary); button.onClick.AddListener(Examine); }
            if (label) label.fontSize = 32;
            if (!Targets.Contains(this)) Targets.Add(this);
            rays = null;
            ClearPress();
            SetVisible(false);
        }

        void OnDisable()
        {
            if (button) button.onClick.RemoveListener(Examine);
            if (focused == this) SetFocus(null);
            Targets.Remove(this);
            ClearPress();
            SetVisible(false);
        }

        void Update() { SetVisible(false); }

        void RefreshLabel()
        {
            if (!label || !controller) return;
            string identity = ResearchStrings.Get("examine.target." + stableTarget, controller.Language);
            if (identity.StartsWith("examine.target.", StringComparison.Ordinal)) identity = stableTarget;
            label.text = "<size=85%>" + identity + "</size>\n" + (string.IsNullOrEmpty(observedInformation)
                ? "<b>" + ResearchStrings.Get("action.examine", controller.Language) + "</b>" : "<size=90%>" + observedInformation + "</size>");
            label.fontSize = 28;
            var size = string.IsNullOrEmpty(observedInformation) ? new Vector2(240, 96) : new Vector2(340, 220);
            ((RectTransform)button.transform).sizeDelta = size;
            if (stationCanvas) ((RectTransform)stationCanvas.transform).sizeDelta = size;
            label.rectTransform.sizeDelta = size - new Vector2(20, 12);
            // Revalidate the new bounds in the same callback, before the expanded chip can render outside the view.
            if (ChipVisible && focusAnchor && stationCanvas && controller.origin && controller.origin.Camera)
                SetVisible(TryFocusBounds(out var bounds) && PlaceBesidePanels(controller.origin.Camera, bounds));
        }

        bool TryFocusBounds(out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (var collider in focusColliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (found) bounds.Encapsulate(collider.bounds); else { bounds = collider.bounds; found = true; }
            }
            return found;
        }

        static Vector3 PositionBesideBounds(Bounds bounds, Vector3 right, Vector3 forward, float chipHalfWidth, Vector3 offset)
        {
            // Imported meshes can store geometry far from their transform pivot. Use the visible physical extent.
            float halfWidth = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z)));
            return bounds.center + right * (halfWidth + chipHalfWidth + Mathf.Clamp(offset.x, .06f, .10f))
                + Vector3.up * offset.y + forward * offset.z;
        }

        bool PlaceBesidePanels(Camera camera, Bounds bounds)
        {
            if (!TargetInView(camera, bounds)) return false;
            var head = camera.transform;
            var rect = (RectTransform)stationCanvas.transform;
            // Keep the acquired action still. Information expansion after a completed click may reflow.
            if (stationCanvas.enabled && string.IsNullOrEmpty(observedInformation) && (hoveringPointer != int.MinValue || presses.Count > 0))
            {
                if (Vector3.Dot(rect.forward, (rect.position - head.position).normalized) < .55f
                    || !(ReadablePlacement(camera, rect) || (training && ViewportPlacementOnly(camera, rect)))) return false;
                AttachConnector(bounds); return true;
            }
            float half = rect.rect.width * Mathf.Abs(rect.lossyScale.x) * .5f;
            // Owner 29 Oct: a chip beside a part lying on the bench sank into the bench once it grew to show information.
            // Its bottom edge never goes below the bottom of the part it labels.
            float floor = bounds.min.y + rect.rect.height * Mathf.Abs(rect.lossyScale.y) * .5f + .02f;
            // Try the same beside-part poses lower before concealing expanded information above the viewport.
            for (int level = 0; level < 2; level++)
            {
                var offset = chipOffset + Vector3.down * (level * .20f);
                for (int side = 0; side < 2; side++)
                {
                    var position = PositionBesideBounds(bounds, side == 0 ? head.right : -head.right, head.forward, half, offset); position.y = Mathf.Max(position.y, floor);
                    rect.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
                    if (ReadablePlacement(camera, rect)) { AttachConnector(bounds); return true; }
                }
            }
            // Training only: a Task chip over a panel could catch a ray sweeping to a Reader button and log a false EXAMINE.
            if (training)
            {
                for (int level = 0; level < 2; level++)
                {
                    var offset = chipOffset + Vector3.down * (level * .20f);
                    for (int side = 0; side < 2; side++)
                    {
                        var position = PositionBesideBounds(bounds, side == 0 ? head.right : -head.right, head.forward, half, offset); position.y = Mathf.Max(position.y, floor);
                        rect.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
                        if (ViewportPlacementOnly(camera, rect)) { AttachConnector(bounds); return true; }
                    }
                }
            }
            return false;
        }

        // Frustum intersection only, not occlusion or wearer visibility. The chip still must fit both eyes.
        bool TargetInView(Camera camera, Bounds bounds)
        {
            if (!camera.stereoEnabled)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, targetFrustum);
                return GeometryUtility.TestPlanesAABB(targetFrustum, bounds);
            }
            GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left), targetFrustum);
            if (GeometryUtility.TestPlanesAABB(targetFrustum, bounds)) return true;
            GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)
                * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right), targetFrustum);
            return GeometryUtility.TestPlanesAABB(targetFrustum, bounds);
        }

        bool ReadablePlacement(Camera camera, RectTransform rect) => camera.stereoEnabled
            ? ReadablePlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Left)
                && ReadablePlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Right)
            : ReadablePlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Mono);

        bool ViewportPlacementOnly(Camera camera, RectTransform rect) => camera.stereoEnabled
            ? ViewportPlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Left)
                && ViewportPlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Right)
            : ViewportPlacementEye(camera, rect, Camera.MonoOrStereoscopicEye.Mono);

        bool ViewportPlacementEye(Camera camera, RectTransform rect, Camera.MonoOrStereoscopicEye eye)
        {
            var chip = ScreenBounds(camera, rect, eye);
            foreach (var corner in screenCorners)
            {
                var point = camera.WorldToViewportPoint(corner, eye);
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
                    || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z)
                    || point.z <= camera.nearClipPlane) return false;
            }
            return chip.xMin >= .02f && chip.xMax <= .98f && chip.yMin >= .02f && chip.yMax <= .98f;
        }

        bool ReadablePlacementEye(Camera camera, RectTransform rect, Camera.MonoOrStereoscopicEye eye)
        {
            var chip = ScreenBounds(camera, rect, eye);
            return ViewportPlacementEye(camera, rect, eye)
                && !OverlapsPanels(camera, chip, eye);
        }

        void AttachConnector(Bounds bounds)
        {
            if (!connector)
            {
                connector = ResearchFlowView.Rect(stationCanvas.transform, "TargetConnection", Vector2.zero, Vector2.one);
                connector.gameObject.AddComponent<CanvasRenderer>();
                var line = connector.gameObject.AddComponent<VRMaintenanceResearch.TrainingV2.TrainingRoundedGraphic>();
                line.color = ResearchUITheme.Muted; line.Radius = 1.5f; line.raycastTarget = false;
            }
            var rect = (RectTransform)stationCanvas.transform;
            var from = rect.InverseTransformPoint(bounds.ClosestPoint(rect.position));
            var to = new Vector3(Mathf.Sign(from.x) * rect.rect.width / 2, 0, 0); var delta = to - from;
            connector.anchoredPosition = (from + to) / 2; connector.sizeDelta = new Vector2(delta.magnitude, 3);
            connector.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        bool OverlapsPanels(Camera camera, Rect chip, Camera.MonoOrStereoscopicEye eye) => controller.view
            && (OverlapsPanel(camera, chip, controller.view.mainPanel, eye) || OverlapsPanel(camera, chip, controller.view.referencePanel, eye)
                || (controller.view.RecoveryCanvas && OverlapsPanel(camera, chip, controller.view.RecoveryCanvas.GetComponent<ResearchPanelMotion>(), eye)));

        bool OverlapsPanel(Camera camera, Rect chip, ResearchPanelMotion panel, Camera.MonoOrStereoscopicEye eye)
        {
            if (!panel || !panel.gameObject.activeInHierarchy || panel.PlacementBlocked || !panel.GetComponent<Canvas>().enabled) return false;
            var group = panel.GetComponent<CanvasGroup>();
            return (!group || group.alpha > .01f) && chip.Overlaps(ScreenBounds(camera, (RectTransform)panel.transform, eye));
        }

        Rect ScreenBounds(Camera camera, RectTransform rect, Camera.MonoOrStereoscopicEye eye)
        {
            rect.GetWorldCorners(screenCorners);
            var first = camera.WorldToViewportPoint(screenCorners[0], eye);
            float xMin = first.x, xMax = first.x, yMin = first.y, yMax = first.y;
            for (int i = 1; i < screenCorners.Length; i++)
            {
                var point = camera.WorldToViewportPoint(screenCorners[i], eye);
                xMin = Mathf.Min(xMin, point.x); xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y); yMax = Mathf.Max(yMax, point.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static string RunPlacementChecks()
        {
            var bounds = new Bounds(new Vector3(8, 1, 5), new Vector3(2, 2, 2));
            var position = PositionBesideBounds(bounds, Vector3.right, Vector3.forward, .095f, new Vector3(.08f, .10f, 0));
            if (Vector3.Distance(position, new Vector3(9.175f, 1.10f, 5)) > .0001f)
                throw new InvalidOperationException("Chip must clear physical bounds plus its own half width and gap.");
            var right = new Vector3(1, 0, 1).normalized;
            bounds.size = new Vector3(4, 2, 6);
            position = PositionBesideBounds(bounds, right, Vector3.forward, .095f, new Vector3(.08f, .10f, 0));
            if (Mathf.Abs(Vector3.Dot(position - bounds.center, right) - (5 / Mathf.Sqrt(2) + .175f)) > .0001f)
                throw new InvalidOperationException("Oblique viewing direction must include both projected physical extents.");
            return "EXAMINE_PLACEMENT_PASS checks=2; rendered overlap and Quest interaction still require verification";
        }

        static void UpdateFocus()
        {
            if (focused && !focused.Available) SetFocus(null);
            if (focused && focused.hoveringPointer != int.MinValue
                && focused.controller.InputSourceForPointer(focused.hoveringPointer) != "None")
            { focused.lastFocusTime = Time.unscaledTime; return; }
            // Cache both input modes; the existing guard activates hand/controller rays as tracking changes.
            if (rays == null) rays = FindObjectsByType<RayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (var pointer in rays)
            {
                if (!pointer || !pointer.isActiveAndEnabled || pointer.State == InteractorState.Disabled) continue;
                ResearchExamineTarget candidate = null;
                float nearest = pointer.MaxRayLength;
                ResearchSessionFlowController sourceController = null;
                string source = "None";
                foreach (var target in Targets)
                {
                    if (!target.Available) continue;
                    if (sourceController != target.controller)
                    { sourceController = target.controller; source = sourceController.InputSourceForPointer(pointer.Identifier); }
                    if (source == "None") continue;
                    if (target.TryFocusHit(pointer.Ray, nearest, out float distance)) { candidate = target; nearest = distance; }
                }
                if (!candidate) continue;
                // Respect the SDK's nearer UI surface, including an expanded Reference.
                if (pointer.CollisionInfo.HasValue && pointer.CollisionInfo.Value.Distance < nearest - .005f
                    && !(focused && focused.hoveringPointer == pointer.Identifier)) continue;
                if (Physics.Raycast(pointer.Ray, out var obstruction, nearest - .005f, ~0, QueryTriggerInteraction.Ignore)
                    && Array.IndexOf(candidate.focusColliders, obstruction.collider) < 0
                    && obstruction.collider.name != "Trainer_Body"
                    && !(candidate.training && obstruction.collider.transform.IsChildOf(candidate.training.transform))) continue;
                SetFocus(candidate);
                candidate.lastFocusTime = Time.unscaledTime;
                return;
            }
            foreach (var target in Targets)
            {
                if (!target || !target.training || !target.Available || !target.controller || target.controller.TutorialArmedFact != "examine") continue;
                SetFocus(target);
                target.lastFocusTime = Time.unscaledTime;
                return;
            }
            // A short bridge lets the pointer travel from the part onto its adjacent chip.
            if (focused && Time.unscaledTime - focused.lastFocusTime > .35f && Time.unscaledTime >= focused.observationUntil) SetFocus(null);
        }

        public bool TryFocusHit(Ray ray, float maxDistance, out float distance)
        {
            distance = maxDistance;
            bool hit = false;
            foreach (var collider in focusColliders)
                if (collider && collider.enabled && collider.gameObject.activeInHierarchy && collider.Raycast(ray, out var result, distance))
                { distance = result.distance; hit = true; }
            return hit;
        }

        static void SetFocus(ResearchExamineTarget target)
        {
            if (focused == target) return;
            var previous = focused;
            focused = target;
            if (previous) { previous.hoveringPointer = int.MinValue; previous.SetVisible(false); }
            if (target) { target.lastFocusTime = Time.unscaledTime; target.SetVisible(target.Available); }
        }

        void SetVisible(bool visible)
        {
            visible = false;
            if (button) button.interactable = visible;
            if (stationCanvas) stationCanvas.enabled = visible;
            if (stationGroup) { stationGroup.alpha = visible ? 1 : 0; stationGroup.interactable = visible; stationGroup.blocksRaycasts = visible; }
            foreach (var root in interactionRoots) if (root && root.activeSelf != visible) root.SetActive(visible);
            // Also hide fixtures/legacy graphics when their canvas has not yet been migrated.
            if (button && button.targetGraphic) { button.targetGraphic.enabled = visible; button.targetGraphic.raycastTarget = visible; }
            if (label) label.enabled = visible;
            if (!visible)
            {
                ClearPress(); observationUntil = 0;
                if (observedInformation != null) { observedInformation = null; RefreshLabel(); }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ChipVisible && eventData != null) hoveringPointer = eventData.pointerId;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData != null && hoveringPointer == eventData.pointerId)
                hoveringPointer = int.MinValue;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerSource = ""; pressedPointer = int.MinValue;
            if (!ChipVisible || eventData == null) return;
            string source = controller.InputSourceForPointer(eventData.pointerId);
            if (source == "None" && !controller.IsTechnical) return;
            presses[eventData.pointerId] = (Context(), source);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pointerSource = ""; pressedPointer = int.MinValue;
            if (!ChipVisible || eventData == null || !presses.TryGetValue(eventData.pointerId, out var press)) return;
            presses.Remove(eventData.pointerId);
            if (press.context != Context() || press.source != controller.InputSourceForPointer(eventData.pointerId)) return;
            pointerSource = press.source; pressedPointer = eventData.pointerId; releasedContext = press.context;
        }

        void Examine()
        {
            string input = pointerSource; int pointer = pressedPointer; string acquiredContext = releasedContext; ClearPress();
            if (!ChipVisible || !controller.LocalUICommandsAllowed || string.IsNullOrEmpty(input) || pointer == int.MinValue || acquiredContext != Context()) return;
            bool nativeController = ResearchUIFeedback.TryGetHapticDevice(controller, pointer, out var device);
            if (training) training.Examine(stableTarget, input); else task.Examine(stableTarget, input);
            if (string.IsNullOrEmpty(controller.Error))
            {
                observedInformation = training ? training.ExamineInfo(stableTarget, controller.Language) : task.ExamineInfo(stableTarget, controller.Language);
                observationUntil = Time.unscaledTime + 6f; RefreshLabel();
            }
            if (nativeController && string.IsNullOrEmpty(controller.Error)) ResearchUIFeedback.TryPulse(device);
        }

        string Context() => stableTarget + "/" + controller?.Record?.run_id + "/" + controller?.Flow?.State
            + "/" + controller?.Flow?.Paused + "/" + controller?.ReferenceOpen + "/" + controller?.ReferenceMinimized;
        void ClearPress() { pointerSource = ""; pressedPointer = int.MinValue; releasedContext = null; presses.Clear(); }
    }
}
