#if RESEARCH_S1_FAN_CHECKS
using System;
using VRMaintenanceResearch.ResearchFlow;

internal static class ResearchS1FanKinematicsChecks
{
    private static int checks;
    private static void Equal(double actual, double expected, string name)
    {
        checks++;
        if (Math.Abs(actual - expected) > 0.000001) throw new Exception(name + " expected=" + expected + " actual=" + actual);
    }
    private static double Grip(double start, params double[] clockwiseDeltas)
    {
        double backoff = 0;
        foreach (var d in clockwiseDeltas) start = ResearchS1FanKinematics.TurnWithDetent(start, d, 540, true, ref backoff);
        return start;
    }
    private static void Truth(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception(name);
    }
    private static void Reject(Action action, string name)
    {
        checks++;
        try { action(); } catch (ArgumentOutOfRangeException) { return; }
        throw new Exception(name + " did not reject");
    }
    public static void Main()
    {
        Equal(ResearchS1FanKinematics.Turn(270, 45, 540, true), 315, "RH clockwise tightens");
        Equal(ResearchS1FanKinematics.Turn(360, 45, 720, false), 315, "LH clockwise loosens");
        Equal(ResearchS1FanKinematics.Turn(270, -45, 540, true), 225, "RH anticlockwise loosens");
        Equal(ResearchS1FanKinematics.Turn(360, -45, 720, false), 405, "LH anticlockwise tightens");
        Equal(ResearchS1FanKinematics.Turn(0, -30, 540, true), 0, "unthread stop");
        Equal(ResearchS1FanKinematics.Turn(530, 30, 540, true), 540, "clamp stop");
        Equal(ResearchS1FanKinematics.Turn(270, 0, 540, true), 270, "push or release is not tightening");
        Equal(ResearchS1FanKinematics.Turn(270, 130, 540, true), 270, "tracking jump cannot tighten");
        Equal(ResearchS1FanKinematics.ThreadDepthMm(0, 540, 16.9), 16.9, "ring mouth");
        Equal(ResearchS1FanKinematics.ThreadDepthMm(270, 540, 16.9), 8.45, "loose ring remains axially retained");
        Equal(ResearchS1FanKinematics.ThreadDepthMm(540, 540, 16.9), 0, "ring clamp");
        Equal(ResearchS1FanKinematics.ThreadDepthMm(360, 720, 5), 2.5, "simplified spinner half travel");
        Equal(ResearchS1FanKinematics.KeyErrorDegrees(180, 180), 0, "blade opposite groove fits");
        Equal(ResearchS1FanKinematics.KeyErrorDegrees(90, 180), 90, "blade quarter-turn blocked");
        Equal(ResearchS1FanKinematics.KeyErrorDegrees(-120, 120), 0, "rear third-turn fits");
        Equal(ResearchS1FanKinematics.KeyErrorDegrees(30, 120), 30, "rear holes misaligned");
        double progress = 540;
        for (int i = 0; i < 18; i++) progress = ResearchS1FanKinematics.Turn(progress, -30, 540, true);
        Equal(progress, 0, "deliberate complete removal");
        for (int i = 0; i < 18; i++) progress = ResearchS1FanKinematics.Turn(progress, 30, 540, true);
        Equal(progress, 540, "reassembly after removal");
        Equal(Grip(540, -.6), 540, "Quest 9b39: 0.6 deg release twist after reaching the stop stays at the stop");
        Equal(Grip(540, -5.8), 540, "Quest 9b39: largest measured release twist (5.8 deg) stays at the stop");
        Equal(Grip(530, -.6), 529.4, "a screw that never reached the stop keeps its real angle");
        Truth(Grip(530, 0) < 539.5, "530 deg is not counted as tight");
        Equal(Grip(540, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3), 510, "deliberate 30 deg loosening from the stop is fully applied");
        Equal(Grip(540, -12, 12), 540, "turned back past the detent then retightened reaches the stop again");
        Equal(Grip(540, -3, 3, -3, 3, -3), 540, "repeated small wobbles at the stop never accumulate");
        Equal(Grip(0, 2.3), 0, "Quest 9b39: 2.3 deg twist at the zero stop stays loose");
        Equal(Grip(0, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3), 30, "deliberate 30 deg tightening from zero is fully applied");
        Equal(Grip(270, -.6), 269.4, "mid-thread motion is unchanged by the detent");
        Equal(Grip(540, -3, 0, -3, 0, -3), 531, "stationary samples do not erase intentional loosening accumulated at the stop");
        Equal(Grip(0, 3, 0, 3, 0, 3), 9, "stationary samples do not erase intentional tightening accumulated at zero");
        Equal(Grip(540, -6, 2, -5), 531, "reverse correction subtracts from net backoff instead of discarding it");
        Equal(Grip(0, 6, -2, 5), 9, "zero-stop detent follows net displacement with reverse correction");
        Equal(Grip(540, -5, 130, -4), 531, "rejected tracking spike does not erase valid backoff");
        Equal(Grip(540, -6, 8, -4), 540, "returning fully to the stop clears earlier backoff");
        double lh = 0, back = 0;
        foreach (var d in new[] { -3.0, -3, -3, -3 }) lh = ResearchS1FanKinematics.TurnWithDetent(lh, d, 720, false, ref back);
        Equal(lh, 12, "left-hand thread: anticlockwise tightens out of the zero detent");
        Reject(() => ResearchS1FanKinematics.Turn(double.NaN, 0, 540, true), "NaN");
        Reject(() => ResearchS1FanKinematics.Turn(0, double.PositiveInfinity, 540, true), "Infinity");
        Reject(() => ResearchS1FanKinematics.Turn(-1, 0, 540, true), "negative progress");
        Reject(() => ResearchS1FanKinematics.Turn(541, 0, 540, true), "overshoot progress");
        Reject(() => ResearchS1FanKinematics.ThreadDepthMm(0, 540, 0), "zero travel");
        Reject(() => ResearchS1FanKinematics.KeyErrorDegrees(0, 0), "zero key interval");
        Console.WriteLine("S1_FAN_KINEMATICS_PASS checks=" + checks + "; scope=pure_rules_only; unity_e2e=false");
    }
}
#endif
