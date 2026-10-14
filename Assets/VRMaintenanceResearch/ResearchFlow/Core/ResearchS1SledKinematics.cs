using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum S1SledPhase { Free, Approaching, Hooked }

    public readonly struct S1SledMotion
    {
        public readonly S1SledPhase Phase;
        public readonly double SlideMm, DepthMm;
        public readonly bool Blocked;
        public bool Seated => Phase == S1SledPhase.Hooked && SlideMm <= .3 && DepthMm <= .3;
        public S1SledMotion(S1SledPhase phase, double slideMm, double depthMm, bool blocked = false)
        { Phase = phase; SlideMm = slideMm; DepthMm = depthMm; Blocked = blocked; }
    }

    public static class ResearchS1SledKinematics
    {
        public static S1SledMotion Step(S1SledMotion current, double slideMm, double depthMm, double transverseMm,
            bool aligned, bool occupied, bool threaded)
        {
            foreach (double value in new[] { slideMm, depthMm, transverseMm })
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(slideMm));
            if (current.Phase == S1SledPhase.Hooked)
            {
                if (threaded || !aligned || Math.Abs(transverseMm) > 4)
                    return new S1SledMotion(current.Phase, current.SlideMm, 0, true);
                if (current.SlideMm >= 5.7 && slideMm >= 5.7 && depthMm > 2) return new S1SledMotion();
                return new S1SledMotion(S1SledPhase.Hooked, Math.Max(0, Math.Min(6, slideMm)), 0, depthMm > .3);
            }
            if (depthMm > 20 || depthMm < -3 || Math.Abs(transverseMm) > 3 || slideMm < -3 || slideMm > 9)
                return new S1SledMotion();
            bool entry = aligned && !occupied && !threaded && Math.Abs(slideMm - 6) <= .3;
            double stop = entry ? 0 : 1.59;
            double depth = Math.Max(stop, depthMm);
            return new S1SledMotion(entry && depth <= .3 ? S1SledPhase.Hooked : S1SledPhase.Approaching,
                entry ? 6 : Math.Max(0, Math.Min(6, slideMm)), depth, !entry);
        }
    }
}
