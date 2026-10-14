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
