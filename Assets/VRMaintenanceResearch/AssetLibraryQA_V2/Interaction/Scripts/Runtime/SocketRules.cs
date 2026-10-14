using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRMaintenanceResearch.AssetLibraryQA
{
    public enum PairOutcome { Seat, Block }

    // One PASS fit test from the library (QA/unity/interaction_rules.json).
    [Serializable]
    public sealed class PairRule
    {
        public string plug, mate, testId, category;
        public PairOutcome outcome;
        public float turnDeg, reachMm;
    }

    // SEAT props from the family sidecar.
    [Serializable]
    public sealed class SeatSpec
    {
        public float approachMm = 20, posTolMm = 3, angTolDeg = 6, hysteresisMm = 1, hysteresisDeg = 2, seatedDepthTolMm = .3f;
    }

    // MATE expressed in the SEAT frame. Unity seat frames insert along local +Z (Blender -Z), so depth is -z.
    public readonly struct MatePose
    {
        public readonly float depthMm, lateralMm, tiltDeg, turnDeg;
        public MatePose(float depthMm, float lateralMm, float tiltDeg, float turnDeg)
        {
            this.depthMm = depthMm; this.lateralMm = lateralMm; this.tiltDeg = tiltDeg; this.turnDeg = turnDeg;
        }
        public override string ToString() => $"depth {depthMm:F3} mm, lateral {lateralMm:F3} mm, tilt {tiltDeg:F3} deg, turn {turnDeg:F3} deg";
    }

    public enum Stop { Seat, Block, Entrance }

    public readonly struct Guidance
    {
        public readonly bool captured;
        public readonly Stop stop;
        public readonly PairRule rule;
        public readonly float minDepthMm, depthMm, turnDeg;
        public Guidance(bool captured, Stop stop, PairRule rule, float minDepthMm, float depthMm, float turnDeg)
        {
            this.captured = captured; this.stop = stop; this.rule = rule; this.minDepthMm = minDepthMm; this.depthMm = depthMm; this.turnDeg = turnDeg;
        }
        public static readonly Guidance Free = new Guidance(false, Stop.Entrance, null, 0, 0, 0);
    }

    public static class SocketRules
    {
        public static MatePose Measure(Matrix4x4 seatWorld, Matrix4x4 mateWorld)
        {
            var rel = seatWorld.inverse * mateWorld;
            Vector3 p = rel.GetColumn(3), forward = rel.GetColumn(2), up = rel.GetColumn(1);
            var untilt = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * up.normalized;
            float turn = Mathf.Atan2(-untilt.x, untilt.y) * Mathf.Rad2Deg;
            return new MatePose(-p.z * 1000, new Vector2(p.x, p.y).magnitude * 1000, Vector3.Angle(forward, Vector3.forward), turn);
        }

        // The rule whose tested turn is nearest the offered turn, within the seat's angle tolerance.
        public static PairRule Match(IEnumerable<PairRule> rules, string plug, string mate, float turnDeg, float tolDeg)
        {
            PairRule best = null; float bestErr = float.MaxValue;
            foreach (var r in rules)
            {
                if (r.plug != plug || r.mate != mate) continue;
                float err = Mathf.Abs(Mathf.DeltaAngle(turnDeg, r.turnDeg));
                if (err <= tolDeg && err < bestErr) { best = r; bestErr = err; }
            }
            return best;
        }

        // Where the hand puts the part versus where the socket lets it go. A held part enters the socket only
        // inside the approach cylinder; once in, hysteresis keeps it there. A tested Seat pair goes to depth 0,
        // a tested Block pair stops at its measured reach, anything untested (or a missing prerequisite) stops
        // at the entrance.
        public static Guidance Guide(MatePose hand, SeatSpec s, PairRule rule, bool wasCaptured, bool prerequisitesMet)
        {
            float pos = s.posTolMm + (wasCaptured ? s.hysteresisMm : 0);
            float ang = s.angTolDeg + (wasCaptured ? s.hysteresisDeg : 0);
            float far = s.approachMm + (wasCaptured ? s.hysteresisMm : 0);
            if (hand.lateralMm > pos || hand.tiltDeg > ang || hand.depthMm > far) return Guidance.Free;

            Stop stop = !prerequisitesMet || rule == null ? Stop.Entrance : rule.outcome == PairOutcome.Seat ? Stop.Seat : Stop.Block;
            float min = stop == Stop.Seat ? 0 : stop == Stop.Block ? rule.reachMm : s.approachMm;
            float depth = Mathf.Clamp(hand.depthMm, min, s.approachMm);
            float turn = rule != null && stop != Stop.Entrance ? rule.turnDeg : hand.turnDeg;
            return new Guidance(true, stop, rule, min, depth, turn);
        }

        public static Matrix4x4 RootPose(Matrix4x4 seatWorld, float depthMm, float turnDeg, Matrix4x4 mateInRoot)
        {
            return seatWorld * Matrix4x4.Translate(new Vector3(0, 0, -depthMm / 1000)) *
                   Matrix4x4.Rotate(Quaternion.AngleAxis(turnDeg, Vector3.forward)) * mateInRoot.inverse;
        }

        public static bool SeatsOnRelease(Guidance g, SeatSpec s) => g.captured && g.stop == Stop.Seat && g.depthMm <= s.seatedDepthTolMm;
    }
}
