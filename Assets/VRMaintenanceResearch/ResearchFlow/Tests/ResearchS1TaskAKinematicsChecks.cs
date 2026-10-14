#if RESEARCH_S1_A_KINEMATICS
using System;
using VRMaintenanceResearch.ResearchFlow;
internal static class ResearchS1TaskAKinematicsChecks
{
    static int checks;
    static void Check(bool pass, string name) { checks++; if (!pass) throw new Exception(name); }
    static S1SledMotion Step(S1SledMotion current, double x, double depth, bool aligned = true, bool occupied = false, bool threaded = false) =>
        ResearchS1SledKinematics.Step(current, x, depth, 0, aligned, occupied, threaded);
    static void Main()
    {
        var free = new S1SledMotion();
        var straight = Step(free, 0, 0);
        Check(straight.Phase == S1SledPhase.Approaching && straight.Blocked && straight.DepthMm == 1.59, "Straight through tray must physically stop.");
        var hook = Step(free, 6, 0);
        Check(hook.Phase == S1SledPhase.Hooked && hook.SlideMm == 6 && !hook.Blocked, "Hooks enter only at +6 mm.");
        Check(!hook.Seated, "Hook entry is not slide completion.");
        var seated = Step(hook, 0, 0);
        Check(seated.Seated && seated.DepthMm == 0 && seated.SlideMm == 0, "User's six millimeter slide seats.");
        Check(!Step(free, 6, 0, false).Seated, "Reversed sled must not be auto corrected.");
        Check(Step(free, 6, 0, true, true).Blocked, "Occupied tray blocks second sled.");
        var retained = Step(seated, 6, 8, true, false, true);
        Check(retained.Seated && retained.Blocked, "Engaged screw retains sled.");
        var pull = Step(seated, 0, 8);
        Check(pull.Phase == S1SledPhase.Hooked && pull.Blocked && pull.DepthMm == 0, "Do not lift locked hooks directly.");
        var raised = Step(seated, 6, 0);
        Check(raised.Phase == S1SledPhase.Hooked && !raised.Seated, "Slide back before lift.");
        Check(Step(raised, 6, 8).Phase == S1SledPhase.Free, "Release after reverse slide.");
        Check(Step(free, 6, 30).Phase == S1SledPhase.Free, "Far object not snapped.");
        Check(Step(hook, -4, 0).SlideMm == 0, "Slide lower stop.");
        Check(Step(hook, 10, 0).SlideMm == 6, "Slide upper stop.");
        Check(!Step(hook, 0, 0, false).Seated, "Misaligned held input blocks motion.");
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool rejected = false;
            try { Step(free, bad, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid pose fails closed.");
        }
        foreach (double travel in new[] { 2.5, 5.1 })
        {
            double full = travel / .79375 * 360, progress = 0;
            Check(ResearchS1FanKinematics.Turn(0, -20, full, true) == 0, "RH cannot tighten counterclockwise.");
            Check(ResearchS1FanKinematics.Turn(0, 120, full, true) == 0, "Tracking jump cannot tighten.");
            for (int i = 0; i < 100; i++) progress = ResearchS1FanKinematics.Turn(progress, 30, full, true);
            Check(progress == full && ResearchS1FanKinematics.ThreadDepthMm(progress, full, travel) == 0, "Tight only after actual travel.");
            progress = ResearchS1FanKinematics.Turn(progress, -30, full, true);
            Check(progress < full && progress > 0, "Reverse turn produces loose not absent.");
            for (int i = 0; i < 100; i++) progress = ResearchS1FanKinematics.Turn(progress, -30, full, true);
            Check(progress == 0 && ResearchS1FanKinematics.ThreadDepthMm(progress, full, travel) == travel, "Fully unthread before removal.");
        }
        Console.WriteLine("S1_TASK_A_KINEMATICS_PASS checks=" + checks);
    }
}
#endif
