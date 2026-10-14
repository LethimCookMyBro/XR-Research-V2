using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1FanKinematics
    {
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
