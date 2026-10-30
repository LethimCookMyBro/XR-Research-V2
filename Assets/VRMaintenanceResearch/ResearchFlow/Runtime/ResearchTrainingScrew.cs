using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Training-only captive screw: the participant's own twist about the seat axis turns it; it never moves by itself.
    // Letting go part-way keeps the reached progress (the "cancel mid-way" condition S1 §4 asks Training to show).
    [DefaultExecutionOrder(11000)]
    public sealed class ResearchTrainingScrew : MonoBehaviour, ITransformer
    {
        public Transform seat, mate;
        public Rigidbody body;
        public Grabbable grabbable;
        public float travelMm = 3, fullDegrees = 540;
        [SerializeField] float progress;
        IGrabbable grab;
        Matrix4x4 mateInRoot;
        float previousTurn;
        double stopBackoff;
        bool held;

        public float Progress => progress;
        public bool Tight => ResearchS1FanKinematics.IsTight(progress, fullDegrees);
        public bool Loose => progress <= 0;
        public float Fraction => progress / fullDegrees;
        /// <summary>+1 last moved in (clockwise), -1 last moved out, 0 not moved this grip.</summary>
        public int Direction { get; private set; }
        float directionAnchor;
        public bool IsHeld => grabbable && grabbable.SelectingPointsCount > 0;
        public bool Allowed = true;

        public void SetProgress(float degrees) { progress = Mathf.Clamp(degrees, 0, fullDegrees); Place(); }
        public void Initialize(IGrabbable value) => grab = value;
        void Awake() => mateInRoot = transform.worldToLocalMatrix * mate.localToWorldMatrix;
        void Start() => Place();

        public void BeginTransform()
        {
            held = true;
            stopBackoff = 0; Direction = 0; directionAnchor = progress;
            previousTurn = HandTurn();
        }

        float HandTurn()
        {
            if (grab == null || grab.GrabPoints.Count == 0) return 0;
            var p = grab.GrabPoints[0];
            return SocketRules.Measure(seat.localToWorldMatrix, Matrix4x4.TRS(p.position, p.rotation, Vector3.one)).turnDeg;
        }

        public void UpdateTransform()
        {
            if (!held || grab.GrabPoints.Count == 0) return;
            float turn = HandTurn();
            // Seat-local +Z is the insertion axis; clockwise seen from the head is a negative turn about it.
            float clockwise = -Mathf.DeltaAngle(previousTurn, turn);
            previousTurn = turn;
            if (Allowed) progress = (float)ResearchS1FanKinematics.TurnWithDetent(progress, clockwise, fullDegrees, true, ref stopBackoff);
            // Owner 29 Oct log: wrist wobble flipped the label between fasten and loosen ~25 times in 5 s. Report a direction
            // only after 8 deg of net travel in it, so the label follows deliberate turning.
            if (Mathf.Abs(progress - directionAnchor) > 8) { Direction = progress > directionAnchor ? 1 : -1; directionAnchor = progress; }
            else if (Direction != 0 && (progress - directionAnchor) * Direction > 0) directionAnchor = progress;
            Place();
        }

        public void EndTransform() { held = false; Place(); }
        void LateUpdate() => Place();

        void Place()
        {
            if (!seat || !mate) return;
            var pose = SocketRules.RootPose(seat.localToWorldMatrix, travelMm * (1 - progress / fullDegrees), -progress, mateInRoot);
            transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            if (body) { body.position = transform.position; body.rotation = transform.rotation; }
        }
    }
}
