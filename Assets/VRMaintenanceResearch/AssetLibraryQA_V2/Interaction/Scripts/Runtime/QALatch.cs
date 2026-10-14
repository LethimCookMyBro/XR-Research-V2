using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.AssetLibraryQA
{
    // A socket latch lever (MOV node). Selecting it with a grab toggles it; the pose comes from the
    // library's own open/close clips, sampled at their last frame.
    public sealed class QALatch : MonoBehaviour
    {
        public QASocket socket;
        public GameObject animationRoot;
        public AnimationClip openClip, closeClip;
        public MonoBehaviour[] interactables;
        public bool startOpen = true;

        public bool Closed { get; private set; } = true;
        public int Presses { get; private set; }

        void Start()
        {
            if (startOpen) Apply(false);
            foreach (var i in interactables) ((IInteractableView)i).WhenStateChanged += Changed;
        }

        void OnDestroy()
        {
            foreach (var i in interactables) if (i) ((IInteractableView)i).WhenStateChanged -= Changed;
        }

        void Changed(InteractableStateChangeArgs args)
        {
            if (args.NewState != InteractableState.Select) return;
            Presses++;
            Toggle();
        }

        public bool Toggle() => Closed ? Open() : Close();

        public bool Open()
        {
            if (!Closed) return false;
            Apply(false);
            return true;
        }

        public bool Close()
        {
            if (Closed) return false;
            if (!socket.CanCloseLatch) { socket.Log("LATCH_BLOCKED", socket.Occupant); return false; }
            Apply(true);
            return true;
        }

        void Apply(bool closed)
        {
            var clip = closed ? closeClip : openClip;
            clip.SampleAnimation(animationRoot, clip.length);
            Closed = closed;
            socket.LatchChanged(this);
        }
    }
}
