using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchReaderTitleScroll : UnityEngine.UI.ScrollRect, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ICancelHandler
    {
        public ResearchFlowView view;
        readonly HashSet<int> pointers = new HashSet<int>();
        int pointer = int.MinValue;
        string context, input;
        PointerEventData dragEvent;
        bool appFocused = true, appPaused;
        public bool Held => pointer != int.MinValue;
        public bool Focused => pointers.Count > 0;
        bool Available => IsActive() && appFocused && !appPaused && view && view.controller && view.ReferenceViewer
            && view.controller.LocalUICommandsAllowed && view.controller.ReferenceOpen && !view.controller.ReferenceMinimized
            && view.controller.Flow?.Paused == false && view.controller.FloorReady && string.IsNullOrEmpty(view.controller.Error)
            && view.referencePanel && view.referencePanel.gameObject.activeInHierarchy && view.referencePanel.GetComponent<Canvas>().enabled
            && !view.referencePanel.PlacementBlocked && view.referencePanel.IsReadableFromHead
            && !view.referencePanel.IsDragging && !view.ReferenceViewer.ControlHeld;
        bool Accepts(string source) => view.controller.IsTechnical || source == "Controller" || source == "HandTracking";
        string Context() => (view.controller.Record?.run_id ?? "setup") + "/" + view.controller.Flow?.State
            + "/" + view.controller.TutorialInstructionEpoch + "/" + view.ReferenceMediaToken + "/" + view.ReferenceViewer.DocumentName;
        bool Valid => Available && context == Context() && input == view.ReferenceViewer.InputForPointer(pointer) && Accepts(input);

        public void OnPointerEnter(PointerEventData data) { if (data != null && Available) pointers.Add(data.pointerId); }
        public void OnPointerExit(PointerEventData data) { if (data != null) pointers.Remove(data.pointerId); }
        public void OnPointerDown(PointerEventData data)
        {
            if (data == null || data.button != PointerEventData.InputButton.Left || Held || !Available) return;
            string source = view.ReferenceViewer.InputForPointer(data.pointerId);
            if (!Accepts(source)) return;
            pointer = data.pointerId; context = Context(); input = source;
        }
        public override void OnInitializePotentialDrag(PointerEventData data)
        {
            if (data != null && (!Held || data.pointerId == pointer) && Available) base.OnInitializePotentialDrag(data);
        }
        public override void OnBeginDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel(); return; }
            dragEvent = data; base.OnBeginDrag(data);
        }
        public override void OnDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel(); return; }
            base.OnDrag(data);
        }
        public override void OnEndDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel(); return; }
            Release();
        }
        public void OnPointerUp(PointerEventData data) => OnEndDrag(data);
        public override void OnScroll(PointerEventData data)
        {
            if (data == null || Held || !Focused || !Available || !Accepts(view.ReferenceViewer.InputForPointer(data.pointerId))
                || float.IsNaN(data.scrollDelta.y) || float.IsInfinity(data.scrollDelta.y)
                || float.IsNaN(data.scrollDelta.x) || float.IsInfinity(data.scrollDelta.x)) return;
            base.OnScroll(data);
        }
        internal void Scroll(float axis, float seconds, string source)
        {
            if (Held || !Focused || !Available || !Accepts(source) || Mathf.Abs(axis) < .2f
                || float.IsNaN(axis) || float.IsInfinity(axis) || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            float range = content.rect.height - viewport.rect.height;
            if (range > 0) verticalNormalizedPosition = Mathf.Clamp01(verticalNormalizedPosition + axis * 420 * seconds / range);
        }
        void Release()
        {
            if (dragEvent != null) base.OnEndDrag(dragEvent);
            StopMovement(); pointer = int.MinValue; context = input = null; dragEvent = null;
        }
        internal void Cancel() { Release(); pointers.Clear(); }
        public void OnCancel(BaseEventData data) => Cancel();
        protected override void LateUpdate()
        {
            if (!Available || (Held && !Valid)) Cancel();
            base.LateUpdate();
        }
        void OnApplicationFocus(bool focused) { appFocused = focused; if (!focused) Cancel(); }
        void OnApplicationPause(bool paused) { appPaused = paused; if (paused) Cancel(); }
        protected override void OnDisable() { Cancel(); base.OnDisable(); }
    }
}
