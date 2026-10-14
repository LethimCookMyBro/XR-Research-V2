#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRMaintenanceResearch.AssetLibraryQA.Editor
{
    // Edit Mode checks of the pure socket rules; no scene, rig or Play Mode needed.
    public static class AssetQAChecks
    {
        public static string RulesSelfTest()
        {
            var spec = new SeatSpec { approachMm = 30, posTolMm = 4, angTolDeg = 8, hysteresisMm = 1.5f, hysteresisDeg = 3, seatedDepthTolMm = .3f };
            var seat = Matrix4x4.TRS(new Vector3(.1f, .8f, .4f), Quaternion.Euler(90, 20, 0), Vector3.one);
            var mateInRoot = Matrix4x4.TRS(new Vector3(0, 0, -.0155f), Quaternion.Euler(0, 0, 0), Vector3.one);
            var seatRule = new PairRule { plug = "P", mate = "Edge", outcome = PairOutcome.Seat, turnDeg = 0, testId = "seat" };
            var reversed = new PairRule { plug = "P", mate = "Edge", outcome = PairOutcome.Block, turnDeg = 180, reachMm = 2.89f, testId = "rev" };
            var rules = new List<PairRule> { seatRule, reversed };
            var failures = new List<string>();
            int count = 0;
            void Expect(string name, bool ok) { count++; if (!ok) failures.Add(name); }
            MatePose At(float depth, float turn, Vector3 lateral = default, float tilt = 0) =>
                SocketRules.Measure(seat, seat * Matrix4x4.Translate(lateral + new Vector3(0, 0, -depth / 1000)) * Matrix4x4.Rotate(Quaternion.AngleAxis(turn, Vector3.forward) * Quaternion.AngleAxis(tilt, Vector3.right)));

            var m = At(10, 0);
            Expect("measure depth", Mathf.Abs(m.depthMm - 10) < 1e-3f && m.lateralMm < 1e-3f && m.tiltDeg < 1e-2f && Mathf.Abs(m.turnDeg) < 1e-2f);
            Expect("measure turn", Mathf.Abs(Mathf.DeltaAngle(At(5, 37).turnDeg, 37)) < 1e-2f);
            Expect("match seat", SocketRules.Match(rules, "P", "Edge", 3, 8) == seatRule);
            Expect("match reversed", SocketRules.Match(rules, "P", "Edge", -177, 8) == reversed);
            Expect("no rule at 90", SocketRules.Match(rules, "P", "Edge", 90, 8) == null);
            Expect("other plug untested", SocketRules.Match(rules, "Q", "Edge", 0, 8) == null);

            var g = SocketRules.Guide(At(-1, 0), spec, seatRule, false, true);
            Expect("seat clamps at 0", g.captured && g.stop == Stop.Seat && g.depthMm == 0 && SocketRules.SeatsOnRelease(g, spec));
            g = SocketRules.Guide(At(10, 0), spec, seatRule, true, true);
            Expect("partial stays partial", g.captured && Mathf.Abs(g.depthMm - 10) < 1e-3f && !SocketRules.SeatsOnRelease(g, spec));
            g = SocketRules.Guide(At(0, 180), spec, reversed, false, true);
            Expect("block stops at reach", g.captured && g.stop == Stop.Block && Mathf.Abs(g.depthMm - 2.89f) < 1e-4f && !SocketRules.SeatsOnRelease(g, spec));
            g = SocketRules.Guide(At(0, 90), spec, null, false, true);
            Expect("untested stops at entrance", g.captured && g.stop == Stop.Entrance && g.depthMm == spec.approachMm);
            g = SocketRules.Guide(At(0, 0), spec, seatRule, false, false);
            Expect("missing prerequisite stops at entrance", g.stop == Stop.Entrance && g.depthMm == spec.approachMm);
            Expect("outside lateral is free", !SocketRules.Guide(At(5, 0, new Vector3(.0045f, 0, 0)), spec, seatRule, false, true).captured);
            Expect("hysteresis keeps it", SocketRules.Guide(At(5, 0, new Vector3(.0045f, 0, 0)), spec, seatRule, true, true).captured);
            Expect("tilt beyond tolerance is free", !SocketRules.Guide(At(5, 0, default, 9), spec, seatRule, false, true).captured);
            Expect("beyond approach is free", !SocketRules.Guide(At(31, 0), spec, seatRule, false, true).captured);

            var root = SocketRules.RootPose(seat, 4.5f, 180, mateInRoot);
            var back = SocketRules.Measure(seat, root * mateInRoot);
            Expect("root pose round trip", Mathf.Abs(back.depthMm - 4.5f) < 1e-3f && Mathf.Abs(Mathf.DeltaAngle(back.turnDeg, 180)) < 1e-2f && back.lateralMm < 1e-3f);

            if (failures.Count > 0) throw new Exception("socket rules self-test failed: " + string.Join(", ", failures));
            return $"socket rules self-test ok: {count} checks";
        }
    }
}
#endif
