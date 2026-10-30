using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Focus belongs to the whole slate, including the separated manipulation bar.
    public sealed class ResearchSlateFocus : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public CanvasGroup toolbar;
        public ResearchFlowView view;
        public ScrollRect scroll;
        internal ResearchReaderTitleScroll titleScroll;
        readonly HashSet<int> pointers = new HashSet<int>();
        readonly HashSet<int> heldPointers = new HashSet<int>();
        public bool PointerHeld => heldPointers.Count > 0;
        public void OnPointerDown(PointerEventData data) { if (data != null) heldPointers.Add(data.pointerId); SyncNavigation(); }
        public void OnPointerUp(PointerEventData data) { if (data != null) heldPointers.Remove(data.pointerId); SyncNavigation(); }
        float lastFocus = float.NegativeInfinity;
        CanvasGroup[] utilities;
        ResearchDragHandle drag;
        public bool Focused => pointers.Count > 0;
        public void OnPointerEnter(PointerEventData data) { pointers.Add(data.pointerId); lastFocus = Time.unscaledTime; SyncNavigation(); }
        public void OnPointerExit(PointerEventData data) { pointers.Remove(data.pointerId); lastFocus = Time.unscaledTime; SyncNavigation(); }
        void SyncNavigation()
        {
            if (view && view.controller) view.controller.UpdateReferenceNavigation(view.controller.ReferenceNavigationInputAvailable, view.controller.ReferenceScrollAxis);
        }
        void OnDisable() { heldPointers.Clear(); pointers.Clear(); lastFocus = float.NegativeInfinity; }
        void Update()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas && !canvas.enabled) heldPointers.Clear();
            if (!view) return;
            if (!drag) drag = GetComponentInChildren<ResearchDragHandle>(true);
            bool visible = (!drag || !drag.IsDragging) && (Focused || Time.unscaledTime - lastFocus < .6f);
            if (toolbar)
            {
                toolbar.alpha = 1;
                toolbar.blocksRaycasts = toolbar.interactable = true;
                if (utilities == null)
                {
                    var groups = new List<CanvasGroup>();
                    foreach (var key in new[] { "Minimize", "Recenter", "Close" })
                    {
                        var control = toolbar.transform.Find(key);
                        if (!control) continue;
                        var group = control.GetComponent<CanvasGroup>();
                        if (!group) group = control.gameObject.AddComponent<CanvasGroup>();
                        groups.Add(group);
                    }
                    utilities = groups.ToArray();
                }
                foreach (var group in utilities)
                {
                    group.alpha = visible ? 1 : 0;
                    group.blocksRaycasts = group.interactable = visible;
                }
            }
            if (!Focused || !scroll || !scroll.gameObject.activeInHierarchy || !view || view.controller?.ReferenceMinimized != false) return;
            Scroll(view.controller.ReferenceScrollAxis.y, Time.unscaledDeltaTime);
        }
        public void Scroll(float axis, float seconds)
        {
            if (!Focused || !scroll || !scroll.gameObject.activeInHierarchy || !view || !view.controller || !view.controller.LocalUICommandsAllowed
                || !view.controller.ReferenceOpen || view.controller.ReferenceMinimized || Mathf.Abs(axis) < .2f
                || float.IsNaN(axis) || float.IsInfinity(axis) || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            if (titleScroll && (titleScroll.Focused || titleScroll.Held))
            {
                titleScroll.Scroll(axis, seconds, view.controller.ReferenceScrollInputSource); return;
            }
            float range = scroll.content.rect.height - scroll.viewport.rect.height;
            if (range <= 0) return;
            int page = view.ReferencePageIndex; float before = scroll.verticalNormalizedPosition;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(before + axis * 420 * seconds / range);
            int next = view.ReferencePageIndex;
            if (next != page)
            {
                string input = view.controller.ReferenceScrollInputSource;
                view.controller.Log("INFORMATION_SCROLL", "reference." + view.controller.ReferenceSection, "PageBoundary", before: new { page, normalized = before },
                    after: new { page = next, normalized = scroll.verticalNormalizedPosition, axis }, source: input == "None" && view.controller.IsTechnical ? "Tester" : "User", inputSource: input);
            }
        }
    }
}
