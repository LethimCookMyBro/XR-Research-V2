using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum ResearchInspectionStage { None, Checking, Ready, Visible }

    public sealed class ResearchTaskTiming
    {
        public const double TaskLimitSeconds = 420;
        public const double InspectionSeconds = 3;
        public bool Started { get; private set; }
        public bool Paused { get; private set; }
        public bool Ended => EndReason.Length > 0;
        public string EndReason { get; private set; } = "";
        public double ActiveElapsed { get; private set; }
        public double WallElapsed { get; private set; }
        public double StartedAt { get; private set; }
        public double? EndedAt { get; private set; }
        public ResearchInspectionStage Stage { get; private set; }
        public bool? InspectionPassed { get; private set; }
        public bool CanManipulate => Started && !Ended && !Paused && Stage == ResearchInspectionStage.None;
        double lastTime, inspectionActiveStart;

        public void Start(double now)
        {
            Validate(now);
            if (Started && !Ended) throw new InvalidOperationException("Cannot reset a running task clock.");
            Started = true; Paused = false; EndReason = "";
            ActiveElapsed = WallElapsed = 0; StartedAt = lastTime = now; EndedAt = null;
            Stage = ResearchInspectionStage.None; InspectionPassed = null;
        }

        public void Tick(double now)
        {
            Validate(now);
            if (!Started) return;
            if (now < lastTime) throw new ArgumentException("Task clock must be monotonic.", nameof(now));
            double delta = now - lastTime; lastTime = now;
            if (Ended) return;
            WallElapsed += delta;
            if (Paused) return;
            ActiveElapsed += delta;
            if (ActiveElapsed >= TaskLimitSeconds) { EndReason = "time_limit"; EndedAt = now; }
            else if (Stage == ResearchInspectionStage.Checking && ActiveElapsed - inspectionActiveStart >= InspectionSeconds)
                Stage = ResearchInspectionStage.Ready;
        }

        public bool Pause(double now)
        {
            Tick(now);
            if (!Started || Ended || Paused) return false;
            Paused = true; return true;
        }

        public bool Resume(double now)
        {
            Tick(now);
            if (!Started || Ended || !Paused) return false;
            Paused = false; return true;
        }

        public bool RequestInspection(double now, bool passed)
        {
            Tick(now);
            if (!CanManipulate) return false;
            InspectionPassed = passed; inspectionActiveStart = ActiveElapsed;
            Stage = ResearchInspectionStage.Checking; return true;
        }

        public bool MarkFeedbackVisible(double now)
        {
            Tick(now);
            if (Ended || Paused || Stage != ResearchInspectionStage.Ready) return false;
            Stage = ResearchInspectionStage.Visible;
            if (InspectionPassed == true) { EndReason = "passed"; EndedAt = now; }
            return true;
        }

        public bool ReturnToTask(double now)
        {
            Tick(now);
            if (Ended || Paused || Stage != ResearchInspectionStage.Visible || InspectionPassed != false) return false;
            Stage = ResearchInspectionStage.None; InspectionPassed = null; return true;
        }

        public void End(string reason, double now)
        {
            if (reason != "withdrawal" && reason != "safety_stop" && reason != "technical_failure" && reason != "other" && reason != "time_limit")
                throw new ArgumentException("Known task termination reason required.", nameof(reason));
            Tick(now);
            if (reason == "time_limit" && EndReason != "time_limit") throw new InvalidOperationException("The active task deadline has not been reached.");
            if (Started && !Ended) { EndReason = reason; EndedAt = now; }
        }

        static void Validate(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentException("Finite nonnegative monotonic time required.", nameof(now));
        }
    }
}
