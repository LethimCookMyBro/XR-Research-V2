using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum PartMotionEvent { OBSERVATION_STARTED, SELECTED, RELEASED, CANCELLED, MOTION_SAMPLE, FIRST_CONTACT, SETTLED, OBSERVATION_GAP }

    [Flags]
    public enum PartMotionDecision { None = 0, Sample = 1, Settled = 2 }

    public sealed class ResearchPartMotionCadence
    {
        public const double SamplePeriodSeconds = .1;
        public const double SettlePeriodSeconds = .25;
        double nextSample, lastTime, quietSince = double.NaN;
        bool moved;

        public void Reset(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentException("Observation time must be finite.");
            nextSample = now; lastTime = now; quietSince = double.NaN; moved = false;
        }

        public PartMotionDecision Observe(double now, bool moving, bool free)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < lastTime)
                throw new ArgumentException("Observation time must be finite and monotonic.");
            lastTime = now;
            var result = PartMotionDecision.None;
            if (moving)
            {
                moved = true; quietSince = double.NaN;
                if (now + 1e-9 >= nextSample)
                {
                    nextSample = now + SamplePeriodSeconds;
                    result |= PartMotionDecision.Sample;
                }
            }
            else if (free && moved)
            {
                if (double.IsNaN(quietSince)) quietSince = now;
                if (now - quietSince + 1e-9 >= SettlePeriodSeconds)
                {
                    result |= PartMotionDecision.Settled;
                    moved = false; quietSince = double.NaN;
                }
            }
            else if (!free) quietSince = double.NaN;
            return result;
        }
    }
}
