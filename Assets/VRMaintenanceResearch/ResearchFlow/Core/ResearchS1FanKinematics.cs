using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1FanKinematics
    {
        // Quest 28 Oct session 9b39: the Training screw reached 540 deg, then read 534.2-539.9 deg while the trigger was let go
        // (largest measured backward twist 5.8 deg). A thread that has reached an end stop stays there until the hand has turned
        // it away by more than this; a thread that never reached the stop is unaffected.
        public const double StopDetentDegrees = 8;

        /// <summary>Turn with an end-stop detent. <paramref name="backoff"/> is the caller's per-grip accumulator (reset on grab).</summary>
        public static double TurnWithDetent(double progressDegrees, double clockwiseDeltaDegrees, double fullDegrees, bool rightHanded, ref double backoff)
        {
            double next = Turn(progressDegrees, clockwiseDeltaDegrees, fullDegrees, rightHanded);
            bool atFull = progressDegrees >= fullDegrees, atZero = progressDegrees <= 0;
            if (!atFull && !atZero) { backoff = 0; return next; }
            double stop = atFull ? fullDegrees : 0;
            // A stationary sample or a rejected tracking jump must not clear motion already accumulated in this grip.
            // Track net hand displacement: turning back toward the stop subtracts from backoff rather than erasing it.
            if (Math.Abs(clockwiseDeltaDegrees) > 90) return stop;
            double directedDelta = clockwiseDeltaDegrees * (rightHanded ? 1 : -1);
            double away = atFull ? -directedDelta : directedDelta;
            backoff = Math.Max(0, backoff + away);
            if (backoff <= StopDetentDegrees) return stop;
            double released = atFull ? fullDegrees - backoff : backoff;
            backoff = 0;
            return Math.Max(0, Math.Min(fullDegrees, released));
        }

        public static double Turn(double progressDegrees, double clockwiseDeltaDegrees, double fullDegrees, bool rightHanded)
        {
            RequireFinite(progressDegrees);
            RequireFinite(clockwiseDeltaDegrees);
            RequireFinite(fullDegrees);
            if (fullDegrees <= 0 || progressDegrees < 0 || progressDegrees > fullDegrees)
                throw new ArgumentOutOfRangeException(nameof(progressDegrees));
            if (Math.Abs(clockwiseDeltaDegrees) > 90) return progressDegrees;
            return Math.Max(0, Math.Min(fullDegrees, progressDegrees + clockwiseDeltaDegrees * (rightHanded ? 1 : -1)));
        }

        public static double ThreadDepthMm(double progressDegrees, double fullDegrees, double travelMm)
        {
            RequireFinite(travelMm);
            if (travelMm <= 0) throw new ArgumentOutOfRangeException(nameof(travelMm));
            Turn(progressDegrees, 0, fullDegrees, true);
            return travelMm * (1 - progressDegrees / fullDegrees);
        }

        public static double KeyErrorDegrees(double turnDegrees, double intervalDegrees)
        {
            RequireFinite(turnDegrees);
            RequireFinite(intervalDegrees);
            if (intervalDegrees <= 0 || intervalDegrees > 360) throw new ArgumentOutOfRangeException(nameof(intervalDegrees));
            return Math.Abs(turnDegrees - Math.Round(turnDegrees / intervalDegrees) * intervalDegrees);
        }

        private static void RequireFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
