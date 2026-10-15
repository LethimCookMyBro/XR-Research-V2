using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>A world pose for a panel computed from a station frame (metres, yaw in degrees, Unity axes: +Y up).</summary>
    public struct ResearchAnchorPose
    {
        public double X, Y, Z, YawDegrees;
        public string Id;
    }

    /// <summary>
    /// Fixed-UI policy panel anchors (ADVISOR_UI_AMENDMENT_20261014 §1/§3): working slates are placed from the authored station
    /// transform (ResearchFlow_Station_Training/Computer/Fan) plus a researcher-calibrated eye height, not from wherever the head
    /// happened to point. Status modals (pause, checkpoint, results) stay head-relative so they remain readable anywhere.
    ///
    /// Each station stands 0.30 m behind a bench that runs 1.40 m deep, so the earlier head-relative spot (±20°, 1.2 m) landed over the
    /// back of the bench, behind the case/fan; the scene sweep (batch3-merge-evidence/anchor_pair_sweep*.txt) found it rejected for
    /// most eye heights. That nominal-size sweep does not prove clearance for every rendered UI state; runtime guards remain authoritative.
    /// The Fan Reader uses the full 1180x896 shell fixture, with a 1.6 m elevation floor relative to the station to clear the bench. The Computer Task slate has a second, wider pose because the case and its parked side panel
    /// hide the 30° pose from eyes below ~1.6 m. Task brief stays right and the Reader left at every station.
    /// Task A P/Q only permute option parts among the same authored seats (ResearchS1TaskAMechanics.ApplyLayout), so both layouts share
    /// one table footprint and one anchor set; the layout is still part of the anchor id and must be P or Q at the Computer station.
    /// </summary>
    public static class ResearchStationAnchors
    {
        public const string Version = "S1_STATION_ANCHORS_20261015_FAN_TASK_FALLBACK";
        public const int TrainingStation = 0, ComputerStation = 1, FanStation = 2;
        public const double MinEyeHeight = 1.0, MaxEyeHeight = 1.95, DefaultEyeHeight = 1.6;

        // Angle from the station's forward (+ = participant's right), horizontal distance from the station point, and height relative to the eye.
        struct Base { public double Angle, Distance, Up; public Base(double a, double d, double u) { Angle = a; Distance = d; Up = u; } }
        static readonly Base[][] TaskBases =
        {
            new[] { new Base(20, 1.4, .1) },
            new[] { new Base(30, 1.8, .1), new Base(55, 1.8, .1) },
            // Keep the calibrated poses first; the low-eye actual-shell sweep clears the Fan with the added .6 m base.
            new[] { new Base(30, 1.4, 0), new Base(30, 1.4, .6) },
        };
        static readonly Base[][] ReaderBases =
        {
            new[] { new Base(-20, 1.8, 0) },
            new[] { new Base(-20, 1.5, .1) },
            new[] { new Base(-35, 1.9, 0) },
        };
        // Bounded clearance ladder per base: raise, then step back, then both. Never toward the head and never to the other side.
        static readonly (double up, double scale)[] Ladder = { (0, 1), (.3, 1), (0, 1.15), (.3, 1.15) };

        public static int CandidateCount(int station, string panelId) => Supports(station, panelId) ? Bases(station, panelId).Length * Ladder.Length : 0;
        static Base[] Bases(int station, string panelId) => (panelId == "reference" ? ReaderBases : TaskBases)[station];

        public static double ClampEyeHeight(double measured) =>
            double.IsNaN(measured) || double.IsInfinity(measured) ? DefaultEyeHeight : Math.Max(MinEyeHeight, Math.Min(MaxEyeHeight, measured));

        public static bool Supports(int station, string panelId) =>
            station >= TrainingStation && station <= FanStation && (panelId == "task_information" || panelId == "reference");

        public static bool TryGet(int station, string layout, string panelId, double stationX, double stationY, double stationZ,
            double stationYawDegrees, double eyeHeight, int candidate, out ResearchAnchorPose pose)
        {
            pose = default;
            if (candidate < 0 || candidate >= CandidateCount(station, panelId)) return false;
            if (station == ComputerStation && layout != "P" && layout != "Q") return false;
            if (double.IsNaN(stationX + stationY + stationZ + stationYawDegrees) || double.IsInfinity(stationX + stationY + stationZ + stationYawDegrees)) return false;
            var b = Bases(station, panelId)[candidate / Ladder.Length];
            var (up, scale) = Ladder[candidate % Ladder.Length];
            double eye = ClampEyeHeight(eyeHeight);
            double yaw = stationYawDegrees * Math.PI / 180, angle = b.Angle * Math.PI / 180;
            // Unity: forward = (sin yaw, 0, cos yaw), right = (cos yaw, 0, -sin yaw).
            double fx = Math.Sin(yaw), fz = Math.Cos(yaw), rx = Math.Cos(yaw), rz = -Math.Sin(yaw);
            double right = Math.Sin(angle) * b.Distance * scale, forward = Math.Cos(angle) * b.Distance * scale;
            pose.X = stationX + rx * right + fx * forward;
            pose.Z = stationZ + rz * right + fz * forward;
            // Keep the full Fan Reader above the bench for seated calibration; never change the measured eye height.
            double panelHeight = station == FanStation && panelId == "reference" ? Math.Max(eye, 1.6) : eye;
            pose.Y = stationY + panelHeight + b.Up + up;
            // Upright slate facing away from the station point, matching ResearchPanelMotion's horizontal LookRotation(head -> panel).
            double dx = pose.X - stationX, dz = pose.Z - stationZ;
            pose.YawDegrees = Math.Atan2(dx, dz) * 180 / Math.PI;
            pose.Id = Version + ":" + StationName(station) + ":" + (station == ComputerStation ? layout : "-") + ":" + panelId + ":" + candidate;
            return true;
        }

        public static string StationName(int station) =>
            station == TrainingStation ? "training" : station == ComputerStation ? "computer" : station == FanStation ? "fan" : "unknown";
    }
}
