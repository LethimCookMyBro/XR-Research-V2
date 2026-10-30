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
            GetComponent<ResearchS1FreeBody>()?.Refresh();
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

        public void Observe(PartOperation operation, PartBlockReason reason = PartBlockReason.None) =>
            owner.port.UpdateInteraction(attempt, operation, reason);

        public void Motion(string action, S1TaskAState before, bool blocked, PartBlockReason reason = PartBlockReason.Unspecified)
        {
            if (!Allowed) return;
            last = owner.Capture();
            var operation = latch ? (action == "FASTEN" ? PartOperation.Close : PartOperation.Open)
                : action == "FASTEN" || action == "UNFASTEN" || action == "ROTATE_WORKPIECE" ? PartOperation.Turn
                : action == "INSTALL" ? PartOperation.Insert : action == "REMOVE" ? PartOperation.Remove
                : action == "CONNECT" ? PartOperation.Connect : action == "DISCONNECT" ? PartOperation.Disconnect : PartOperation.Move;
            owner.port.UpdateAttempt(attempt, action, before, last, blocked, operation, reason);
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
