using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.AssetLibraryQA
{
    // A removable library part. It is the Grabbable's one-grab transformer, so every grab update passes
    // through the sockets before the part moves.
    [DefaultExecutionOrder(10000)]
    public sealed class QAPlug : MonoBehaviour, ITransformer
    {
        public string assetId, stableId;
        public Rigidbody body;
        public Grabbable grabbable;
        public List<Transform> mates = new List<Transform>();

        public QASocket Socket { get; internal set; }
        public Transform HomeParent { get; private set; }
        public bool IsHeld => grabbable && grabbable.SelectingPointsCount > 0;
        public Matrix4x4 LastFreePose { get; private set; }
        public System.Predicate<Matrix4x4> PoseAllowed;

        // Diagnostics for the QA runner: how many pointers drive the grab and how often per frame.
        public int MaxGrabPoints { get; private set; }
        public int MaxUpdatesPerFrame { get; private set; }
        int updateFrame, updatesThisFrame;

        IGrabbable grab;
        Matrix4x4 grabToRoot;
        bool wasHeld, holding;

        void Awake() => HomeParent = transform.parent;

        public string MateName(Transform mate) => mate.name.Substring(("MATE_" + assetId + "_").Length);
        public Matrix4x4 MateInRoot(Transform mate) => transform.worldToLocalMatrix * mate.localToWorldMatrix;

        public void Initialize(IGrabbable grabbable) => grab = grabbable;

        public void BeginTransform()
        {
            var p = grab.GrabPoints[0];
            grabToRoot = Matrix4x4.TRS(p.position, p.rotation, Vector3.one).inverse * transform.localToWorldMatrix;
            LastFreePose = transform.localToWorldMatrix;
        }

        public void UpdateTransform() => UpdateTransform(null);

        public void UpdateTransform(System.Predicate<Matrix4x4> acceptsPose)
        {
            QASocket proposedSocket;
            UpdateTransform(acceptsPose, out proposedSocket);
        }

        public void UpdateTransform(System.Predicate<Matrix4x4> acceptsPose, out QASocket proposedSocket)
        {
            var p = grab.GrabPoints[0];
            if (updateFrame != Time.frameCount) { updateFrame = Time.frameCount; updatesThisFrame = 0; }
            MaxUpdatesPerFrame = Mathf.Max(MaxUpdatesPerFrame, ++updatesThisFrame);
            MaxGrabPoints = Mathf.Max(MaxGrabPoints, grab.GrabPoints.Count);
            LastFreePose = Matrix4x4.TRS(p.position, p.rotation, Vector3.one) * grabToRoot;
            var pose = QASocket.Constrain(this, LastFreePose, acceptsPose, out proposedSocket);
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }

        public void EndTransform() { }

        // The socket's guidance as of the last frame the part was held. On release the ISDK re-poses the part
        // from the unselect event, which can sit a few mm off the held pose; the release decision uses this.
        public Guidance HeldGuidance { get; private set; }
        public QASocket HeldSocket { get; private set; }
        public Transform HeldMate { get; private set; }

        void LateUpdate()
        {
            bool held = IsHeld;
            if (held && !wasHeld) QASocket.Grabbed(this);
            if (!held && wasHeld) QASocket.Released(this);
            if (held)
            {
                HeldSocket = Socket;
                HeldMate = Socket ? Socket.Mate : null;
                HeldGuidance = Socket ? Socket.Last : Guidance.Free;
            }
            wasHeld = held;
        }

        // Kinematic while a socket holds the part; ref-counted with the Grabbable's own lock.
        internal void Hold(bool on)
        {
            if (on == holding) return;
            holding = on;
            if (on)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.LockKinematic();
            }
            else body.UnlockKinematic();
        }

        internal void Place(Matrix4x4 pose)
        {
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            body.position = transform.position;
            body.rotation = transform.rotation;
        }
    }
}
