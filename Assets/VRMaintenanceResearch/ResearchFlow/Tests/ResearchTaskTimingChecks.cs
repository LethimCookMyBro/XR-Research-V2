#if RESEARCH_TASK_TIMING_CHECKS
using System;
using VRMaintenanceResearch.ResearchFlow;

static class ResearchTaskTimingChecks
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        checks++; Console.WriteLine("PASS " + name);
    }
    static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, name);
    }
    static ResearchTaskTiming Start(double now = 0)
    {
        var clock = new ResearchTaskTiming(); clock.Start(now); return clock;
    }
    static void Main()
    {
        var t = new ResearchTaskTiming();
        Check(!t.Started && !t.CanManipulate && !t.RequestInspection(0, true), "no task or Inspect before TASK_STARTED");
        Reject(() => t.Start(double.NaN), "NaN time rejected");
        Reject(() => t.Start(double.PositiveInfinity), "infinite time rejected");
        Reject(() => t.Start(-1), "negative time rejected");
        t.Start(10);
        Check(t.Started && t.CanManipulate && t.ActiveElapsed == 0 && t.WallElapsed == 0, "start enables one fresh task clock");
        Reject(() => t.Start(11), "duplicate start cannot reset live deadline");
        Check(t.RequestInspection(11, true) && t.Stage == ResearchInspectionStage.Checking && !t.CanManipulate, "request freezes manipulation without formal pause");
        Check(!t.RequestInspection(11.1, false), "duplicate request cannot replace pending result");
        Check(!t.MarkFeedbackVisible(13.999), "feedback before three active seconds rejected");
        t.Tick(14);
        Check(t.Stage == ResearchInspectionStage.Ready && t.EndReason == "" && !t.Ended, "three seconds only makes result renderable, not complete");
        Check(t.MarkFeedbackVisible(14.2) && t.EndReason == "passed" && Math.Abs(t.ActiveElapsed - 4.2) < 1e-9, "pass ends only on feedback visibility acknowledgement");
        Check(t.StartedAt == 10 && t.EndedAt == 14.2, "exact start and visible-end timestamps retained for raw replay");
        Check(!t.MarkFeedbackVisible(14.3), "feedback completion credited once");
        t.Tick(500);
        Check(t.EndReason == "passed" && Math.Abs(t.ActiveElapsed - 4.2) < 1e-9, "ended pass duration does not grow or turn into timeout");
        Check(t.EndedAt == 14.2, "late ticks cannot move recorded end timestamp");
        t.Start(501);
        Check(t.CanManipulate && t.Stage == ResearchInspectionStage.None && !t.InspectionPassed.HasValue && t.ActiveElapsed == 0, "next task clears prior result and timing");

        t = Start(); t.RequestInspection(1, false); t.Tick(4);
        Check(!t.ReturnToTask(4), "continue blocked before visible failure");
        Check(t.MarkFeedbackVisible(4), "failure visibility acknowledged");
        Check(!t.Ended && !t.CanManipulate && t.EndReason == "", "fail is not task completion");
        t.Tick(20);
        Check(t.ActiveElapsed == 20, "reading failure feedback counts as active task time");
        Check(t.ReturnToTask(20) && t.CanManipulate && t.Stage == ResearchInspectionStage.None, "continue permits unconstrained retry after visible fail");
        Check(t.RequestInspection(20, false), "retry available without increased penalty");
        t.Tick(23);
        Check(t.Stage == ResearchInspectionStage.Ready, "retry takes same three active seconds");

        foreach (string reason in new[] { "participant", "application_focus_lost", "tracking_lost" })
        {
            t = Start(); t.RequestInspection(10, true);
            Check(t.Pause(11), reason + " formal pause enters");
            Check(!t.Pause(12), reason + " duplicate pause does not reset origin");
            t.Tick(100);
            Check(t.ActiveElapsed == 11 && t.WallElapsed == 100 && t.Stage == ResearchInspectionStage.Checking, reason + " freezes active inspect wait but not wall clock");
            Check(!t.MarkFeedbackVisible(100) && !t.CanManipulate, reason + " cannot show feedback during pause");
            Check(t.Resume(101), reason + " resume accepted");
            t.Tick(102.999);
            Check(t.Stage == ResearchInspectionStage.Checking, reason + " remaining active wait retained");
            t.Tick(103);
            Check(t.Stage == ResearchInspectionStage.Ready && t.ActiveElapsed == 13, reason + " completes three active seconds after resume");
            Check(t.MarkFeedbackVisible(103) && t.EndReason == "passed" && t.WallElapsed == 103, reason + " preserves wall and active durations separately");
        }
        t = Start(); t.RequestInspection(0, false); t.Tick(3); t.Pause(3);
        Check(!t.MarkFeedbackVisible(100), "ready feedback still suppressed during pause");
        t.Resume(100);
        Check(t.MarkFeedbackVisible(100), "already ready feedback may render after resume without extra artificial delay");

        t = Start(); t.RequestInspection(416.999, true); t.Tick(419.999);
        Check(t.MarkFeedbackVisible(419.999) && t.EndReason == "passed", "pass visible strictly before 420 accepted");
        t = Start(); t.RequestInspection(417, true); t.Tick(420);
        Check(t.EndReason == "time_limit" && !t.MarkFeedbackVisible(420), "feedback exactly at 420 loses tie to deadline");
        Check(!t.CanManipulate && !t.ReturnToTask(421), "timeout cannot continue failed modal or manipulate");
        t = Start(); t.RequestInspection(416, true); t.Tick(419);
        Check(t.Stage == ResearchInspectionStage.Ready && !t.Ended, "backend pass ready at 419 is not yet completion");
        Check(!t.MarkFeedbackVisible(420.1) && t.EndReason == "time_limit", "render delay past deadline cannot backdate pass");
        t = Start(); t.Tick(420);
        Check(t.EndReason == "time_limit" && !t.RequestInspection(420, true), "late Inspect cannot grant extra time");
        t = Start(); t.RequestInspection(415, false); t.Tick(418); t.MarkFeedbackVisible(418); t.Tick(420);
        Check(t.EndReason == "time_limit", "visible fail modal still expires at deadline");
        t = Start(); t.Pause(419); t.Tick(900);
        Check(!t.Ended && t.ActiveElapsed == 419, "official pause at 419 may cross wall deadline without timeout");
        t.Resume(900); t.Tick(901);
        Check(t.EndReason == "time_limit" && t.WallElapsed == 901 && t.ActiveElapsed == 420, "deadline excludes only formally paused interval");
        t = Start(); t.Tick(30);
        Check(t.ActiveElapsed == 30, "information reading and thinking have no implicit time exclusion");
        Reject(() => t.Tick(29), "backward monotonic clock rejected");
        Reject(() => t.Tick(double.NaN), "nonfinite tick rejected");
        t.End("technical_failure", 31);
        Check(t.EndReason == "technical_failure" && !t.MarkFeedbackVisible(35), "technical end never creates pass exposure");
        Reject(() => Start().End("invented_reason", 1), "unknown end reason rejected");
        Reject(() => Start().End("time_limit", 1), "researcher cannot label early abort as the 420-second deadline");
        t = Start(); t.End("time_limit", 420);
        Check(t.EndReason == "time_limit", "researcher timeout requires actual active deadline");

        foreach (var order in new[] { TaskOrder.AB, TaskOrder.BA })
        {
            var flow = new ResearchSessionFlow(order, ResearchFlowState.Task1Ready, 5);
            Check(!flow.EndTask(), order + " cannot end before task start");
            flow.StartTask(); flow.Inspect(false);
            Check(flow.EndTask() && flow.State == ResearchFlowState.PostTask1Checkpoint && !flow.InspectPassed.HasValue, order + " timeout from fail modal reaches checkpoint without pass");
            Check(!flow.EndTask() && !flow.CanInteract, order + " timeout idempotent with mechanics gated");
            flow.ResearcherResumeCheckpoint(); flow.StartTask();
            Check(flow.EndTask() && flow.State == ResearchFlowState.PostTask2Checkpoint, order + " second task timeout retains researcher checkpoint");
        }
        Console.WriteLine("TASK_TIMING_CHECKS_PASS checks=" + checks + "; scope=pure production timing/flow; no Unity render or physical E2E claim");
    }
}
#endif
