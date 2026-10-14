using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Desktop Display 2 input while the existing Meta input module owns the EventSystem.</summary>
    public sealed class ResearcherDesktopPointer : MonoBehaviour
    {
        public Canvas researcherCanvas;
        public const int PointerId = -73021;
        private PointerEventData pointer;
        private EventSystem sharedEventSystem;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private readonly List<GraphicRaycaster> raycasters = new List<GraphicRaycaster>();
        private GameObject hovered;

        private void Update()
        {
            var mouse = Mouse.current;
            var events = EventSystem.current;
            bool metaOwnsInput = events != null && events.currentInputModule != null && events.currentInputModule.GetType().FullName == "Oculus.Interaction.PointableCanvasModule";
            if (!Application.isFocused || !metaOwnsInput || researcherCanvas == null || !researcherCanvas.isActiveAndEnabled || researcherCanvas.targetDisplay != 1 || mouse == null || mouse.displayIndex.ReadValue() != 1)
            { Cancel(); return; }
            if (sharedEventSystem != events || pointer == null)
            {
                Cancel(); sharedEventSystem = events;
                pointer = new PointerEventData(events) { pointerId = PointerId, button = PointerEventData.InputButton.Left, displayIndex = 1 };
            }
            var position = mouse.position.ReadValue();
            pointer.delta = position - pointer.position;
            pointer.position = position;
            pointer.scrollDelta = mouse.scroll.ReadValue() / 120f;
            hits.Clear(); raycasters.Clear();
            researcherCanvas.GetComponentsInChildren(false, raycasters);
            foreach (var raycaster in raycasters)
            {
                var canvas = raycaster.GetComponent<Canvas>();
                if (raycaster.isActiveAndEnabled && canvas != null && canvas.targetDisplay == 1 && canvas.rootCanvas == researcherCanvas)
                    raycaster.Raycast(pointer, hits);
            }
            hits.Sort((left, right) => left.sortingOrder != right.sortingOrder ? right.sortingOrder.CompareTo(left.sortingOrder) : right.depth.CompareTo(left.depth));
            pointer.pointerCurrentRaycast = hits.Count == 0 ? new RaycastResult() : hits[0];
            var target = pointer.pointerCurrentRaycast.gameObject;
            if (hovered != target)
            {
                if (hovered != null) ExecuteEvents.ExecuteHierarchy(hovered, pointer, ExecuteEvents.pointerExitHandler);
                hovered = target; pointer.pointerEnter = target;
                if (target != null) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
            }
            if (mouse.leftButton.wasPressedThisFrame) Press(target);
            if (mouse.leftButton.isPressed && pointer.pointerDrag != null)
            {
                if (!pointer.dragging && (pointer.position - pointer.pressPosition).sqrMagnitude >= events.pixelDragThreshold * events.pixelDragThreshold)
                {
                    pointer.dragging = true; pointer.eligibleForClick = false;
                    ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.beginDragHandler);
                }
                if (pointer.dragging) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.dragHandler);
            }
            if (mouse.leftButton.wasReleasedThisFrame) Release(target, true);
            if (target != null && pointer.scrollDelta.sqrMagnitude > 0) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.scrollHandler);
        }

        private void Press(GameObject target)
        {
            pointer.eligibleForClick = true; pointer.dragging = false; pointer.useDragThreshold = true;
            pointer.pressPosition = pointer.position; pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            pointer.clickCount = 1; pointer.clickTime = Time.unscaledTime;
            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler) ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            pointer.rawPointerPress = target;
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
        }

        private void Release(GameObject target, bool allowClick)
        {
            if (pointer == null) return;
            if (pointer.pointerPress != null) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            if (allowClick && pointer.eligibleForClick && pointer.pointerPress != null && pointer.pointerPress == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
                ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            if (pointer.dragging && pointer.pointerDrag != null)
            {
                if (allowClick && target != null) ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.dropHandler);
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            }
            pointer.eligibleForClick = false; pointer.dragging = false; pointer.pointerPress = null; pointer.rawPointerPress = null; pointer.pointerDrag = null;
        }

        private void Cancel()
        {
            Release(null, false);
            if (hovered != null && pointer != null) ExecuteEvents.ExecuteHierarchy(hovered, pointer, ExecuteEvents.pointerExitHandler);
            hovered = null;
            if (pointer != null) pointer.pointerEnter = null;
        }

        private void OnDisable() => Cancel();
    }
}
