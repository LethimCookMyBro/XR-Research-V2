using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.AssetLibraryQA
{
    public enum SocketState { Empty, Guided, Partial, Blocked, Seated, Latched }

    // One SEAT on a library part. States follow the sidecar's names: empty, inserted_partial, seated, latched.
    public sealed class QASocket : MonoBehaviour
    {
        public static readonly List<QASocket> All = new List<QASocket>();

        public string fixtureId, seatName;
        public Transform seat;
        public SeatSpec spec = new SeatSpec();
        public List<PairRule> rules = new List<PairRule>();
        public List<QALatch> latches = new List<QALatch>();
        public List<QASocket> requires = new List<QASocket>(), retainedBy = new List<QASocket>();
        public HandsLabSession session;

        public string Id => fixtureId + "/" + seatName;
        public SocketState State { get; private set; }
        public QAPlug Occupant { get; private set; }
        public Transform Mate { get; private set; }
        public Guidance Last { get; private set; }
        public int PullsBlocked { get; private set; }
        public MatePose Exit { get; private set; }

        public bool Holds => State == SocketState.Partial || State == SocketState.Blocked || State == SocketState.Seated || State == SocketState.Latched;
        public bool PrerequisitesMet => requires.All(s => s.State == SocketState.Seated || s.State == SocketState.Latched)
                                        && (Occupant != null || latches.All(l => !l.Closed));
        public bool Retained => Occupant != null && (latches.Any(l => l.Closed) || retainedBy.Any(s => s.Occupant != null));
        public bool CanCloseLatch => State == SocketState.Empty || State == SocketState.Seated || State == SocketState.Latched;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static Matrix4x4 Constrain(QAPlug plug, Matrix4x4 freeRoot) => Constrain(plug, freeRoot, null);

        public static Matrix4x4 Constrain(QAPlug plug, Matrix4x4 freeRoot, System.Predicate<Matrix4x4> acceptsPose)
        {
            QASocket proposedSocket;
            return Constrain(plug, freeRoot, acceptsPose, out proposedSocket);
        }

        public static Matrix4x4 Constrain(QAPlug plug, Matrix4x4 freeRoot, System.Predicate<Matrix4x4> acceptsPose, out QASocket proposedSocket)
        {
            proposedSocket = null;
            if (acceptsPose == null) acceptsPose = plug.PoseAllowed;
            var current = plug.Socket;
            if (current && current.Retained)
            {
                proposedSocket = current;
                var retained = SocketRules.RootPose(current.seat.localToWorldMatrix, current.Last.depthMm, current.Last.turnDeg, plug.MateInRoot(current.Mate));
                if (acceptsPose != null && !acceptsPose(retained)) return plug.transform.localToWorldMatrix;
                current.PullsBlocked++;
                if (current.PullsBlocked == 1) current.Log("PULL_BLOCKED", plug);
                return retained;
            }
            var ordered = current ? new[] { current }.Concat(All.Where(s => s != current)).ToArray() : All.ToArray();
            for (int pass = 0; pass < 2; pass++)
                foreach (var socket in ordered)
                {
                    if (socket.transform.IsChildOf(plug.transform) || (socket.Occupant && socket.Occupant != plug)) continue;
                    foreach (var mate in plug.mates)
                    {
                        var mateInRoot = plug.MateInRoot(mate);
                        var seatWorld = socket.seat.localToWorldMatrix;
                        var hand = SocketRules.Measure(seatWorld, freeRoot * mateInRoot);
                        bool was = socket == current && socket.Mate == mate;
                        var rule = SocketRules.Match(socket.rules, plug.assetId, plug.MateName(mate), hand.turnDeg,
                                                     socket.spec.angTolDeg + (was ? socket.spec.hysteresisDeg : 0));
                        if ((pass == 0) != (rule != null)) continue;
                        var g = SocketRules.Guide(hand, socket.spec, rule, was, socket.PrerequisitesMet);
                        if (!g.captured) continue;
                        var pose = SocketRules.RootPose(seatWorld, g.depthMm, g.turnDeg, mateInRoot);
                        proposedSocket = socket;
                        if (acceptsPose != null && !acceptsPose(pose)) return plug.transform.localToWorldMatrix;
                        socket.Track(plug, mate, g);
                        return pose;
                    }
                }
            if (acceptsPose != null && !acceptsPose(freeRoot)) return plug.transform.localToWorldMatrix;
            if (current)
            {
                current.Exit = SocketRules.Measure(current.seat.localToWorldMatrix, freeRoot * plug.MateInRoot(current.Mate));
                current.Leave(plug);
            }
            return freeRoot;
        }

        public static void Grabbed(QAPlug plug)
        {
            var s = plug.Socket;
            if (!s) return;
            s.PullsBlocked = 0;
            if (!s.Retained) s.Set(SocketState.Guided, "GRABBED_IN_SOCKET", plug);
        }

        public static void Released(QAPlug plug)
        {
            var s = plug.HeldSocket;
            var g = plug.HeldGuidance;
            if (!s || !g.captured)
            {
                if (plug.Socket) plug.Socket.Leave(plug);
                plug.Hold(false);
                return;
            }
            if (plug.Socket != s) s.Track(plug, plug.HeldMate, g);
            if (s.Retained) { s.Log("RELEASED_RETAINED", plug); return; }
            s.Last = g;
            if (SocketRules.SeatsOnRelease(g, s.spec))
            {
                var seated = SocketRules.RootPose(s.seat.localToWorldMatrix, 0, g.turnDeg, plug.MateInRoot(s.Mate));
                if (plug.PoseAllowed != null && !plug.PoseAllowed(seated))
                {
                    plug.Hold(true);
                    plug.transform.SetParent(s.transform, true);
                    s.Set(SocketState.Partial, "RELEASE_BLOCKED", plug);
                    return;
                }
                s.Last = new Guidance(true, Stop.Seat, g.rule, 0, 0, g.turnDeg);
                s.Settle(plug, SocketState.Seated, "SEATED");
                foreach (var l in s.latches) l.Close();
            }
            else if (g.stop == Stop.Block) s.Settle(plug, SocketState.Blocked, "RELEASED_BLOCKED");
            else if (g.stop == Stop.Seat) s.Settle(plug, SocketState.Partial, "RELEASED_PARTIAL");
            else { s.Leave(plug); plug.Hold(false); }
        }

        void Track(QAPlug plug, Transform mate, Guidance g)
        {
            if (Occupant == plug) { Last = g; return; }
            if (plug.Socket && plug.Socket != this) plug.Socket.Leave(plug);
            Occupant = plug; Mate = mate; plug.Socket = this; Last = g;
            SetIgnoreCollisions(plug, true);
            Set(SocketState.Guided, g.stop == Stop.Entrance ? (g.rule == null ? "ENTERED_UNTESTED" : "ENTERED_PREREQUISITE_MISSING") : "ENTERED", plug);
        }

        void Leave(QAPlug plug)
        {
            SetIgnoreCollisions(plug, false);
            plug.Socket = null; Occupant = null; Mate = null; Last = Guidance.Free;
            if (plug.transform.parent != plug.HomeParent) plug.transform.SetParent(plug.HomeParent, true);
            Set(SocketState.Empty, "LEFT", plug);
        }

        void Settle(QAPlug plug, SocketState state, string evt)
        {
            plug.Hold(true);
            plug.Place(SocketRules.RootPose(seat.localToWorldMatrix, Last.depthMm, Last.turnDeg, plug.MateInRoot(Mate)));
            plug.transform.SetParent(transform, true);
            Set(state, evt, plug);
        }

        internal void LatchChanged(QALatch latch)
        {
            if (State == SocketState.Seated && latches.All(l => l.Closed)) Set(SocketState.Latched, "LATCHED", Occupant);
            else if (State == SocketState.Latched && !latches.All(l => l.Closed)) Set(SocketState.Seated, "UNLATCHED", Occupant);
            else Log(latch.Closed ? "LATCH_CLOSED" : "LATCH_OPENED", Occupant);
        }

        void SetIgnoreCollisions(QAPlug plug, bool ignore)
        {
            var mine = GetComponentsInChildren<Collider>(true).Where(c => !c.transform.IsChildOf(plug.transform)).ToArray();
            foreach (var a in plug.GetComponentsInChildren<Collider>(true))
                foreach (var b in mine) Physics.IgnoreCollision(a, b, ignore);
        }

        void Set(SocketState state, string evt, QAPlug plug)
        {
            State = state;
            Log(evt, plug);
        }

        internal void Log(string evt, QAPlug plug)
        {
            var g = Last;
            session?.Logger?.Record("SOCKET_" + evt, plug ? plug.stableId : Id, State.ToString(), "Active", "None", null,
                new { socket = Id, depth_mm = g.depthMm, stop = g.stop.ToString(), rule = g.rule?.testId, turn_deg = g.turnDeg,
                      retained = Retained, latches = latches.Select(l => l.Closed ? "closed" : "open").ToArray(),
                      exit_hand = evt == "LEFT" ? Exit.ToString() : null, frame = Time.frameCount }, "System");
        }
    }
}
