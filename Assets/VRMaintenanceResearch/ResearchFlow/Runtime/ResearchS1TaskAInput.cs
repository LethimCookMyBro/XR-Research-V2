using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(13000)]
    public sealed class ResearchS1TaskAInput : MonoBehaviour
    {
        public ResearchS1TaskAMechanics owner;
        public string stableId;
        public QAPlug plug;
        public QALatch latch;
        public PointableElement pointable;
        public HandGrabInteractable interactable;
        public bool customMotion;
        private ResearchTaskPort.Attempt attempt;
        private S1TaskAState last;
        private int pointer = -1;
        private bool endPending, cancelled;
        public bool Allowed => !endPending && pointable.SelectingPointsCount == 1 &&
            attempt != null && owner.port.IsAttemptActive(attempt) && owner.InputSource(pointer) == attempt.InputSource;

        private void Start()
        {
            pointable.TransferOnSecondSelection = true;
            pointable.WhenPointerEventRaised += Pointer;
        }
        private void OnDestroy()
        {
            if (pointable) pointable.WhenPointerEventRaised -= Pointer;
            Finish(true);
        }

        public void Gate(bool allowed)
        {
            interactable.enabled = allowed;
            if (plug) plug.grabbable.enabled = allowed;
            if (!allowed) Finish(true);
        }

        private void Pointer(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select)
            {
                Finish(attempt != null && !endPending || cancelled);
                pointer = evt.Identifier;
                string source = owner.InputSource(pointer);
                if (source == "None") return;
                attempt = owner.port.BeginAttempt(stableId, source);
                last = owner.Capture();
                if (latch)
                {
                    bool closing = !latch.Closed;
                    var before = owner.Capture();
                    bool blocked = !latch.Toggle();
                    Motion(closing ? "FASTEN" : "UNFASTEN", before, blocked);
                }
            }
            else if ((evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel) && evt.Identifier == pointer)
            {
                endPending = true;
                cancelled = evt.Type == PointerEventType.Cancel;
            }
        }

        public void Motion(string action, S1TaskAState before, bool blocked)
        {
            if (!Allowed) return;
            last = owner.Capture();
            owner.port.UpdateAttempt(attempt, action, before, last, blocked);
        }

        private void LateUpdate()
        {
            if (endPending) Finish(cancelled);
        }

        private void Finish(bool system)
        {
            if (attempt != null && owner && owner.port) owner.port.EndAttempt(attempt, system);
            attempt = null;
            endPending = cancelled = false;
            pointer = -1;
        }
    }
}
