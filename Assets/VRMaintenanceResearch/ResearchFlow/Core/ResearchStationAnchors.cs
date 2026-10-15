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
    /// Offsets reproduce the first candidate of the earlier head-relative placement (20 degrees to the side at 1.2 m, 0.1 m below
    /// the eye) so the nominal layout is unchanged; alternatives are bounded and still station-relative. Task A P/Q only permute
    /// option parts among the same authored seats (ResearchS1TaskAMechanics.ApplyLayout), so both layouts share one table footprint
    /// and one anchor set; the layout is still part of the anchor id and must be P or Q at the Computer station.
    /// Scene distances/angles have NOT been verified in Unity by this code; see HANDOFF_20261015_CLOUD_TH.md.
    /// </summary>
    public static class ResearchStationAnchors
    {
        public const string Version = "S1_STATION_ANCHORS_20261015";
        public const int TrainingStation = 0, ComputerStation = 1, FanStation = 2;
        public const double MinEyeHeight = 1.0, MaxEyeHeight = 1.95, DefaultEyeHeight = 1.6;

        // Station-local offsets: right (+ = participant's right), up relative to the calibrated eye, forward from the station point.
        struct Offset { public double Right, Up, Forward; public Offset(double r, double u, double f) { Right = r; Up = u; Forward = f; } }
        static readonly double Side = 1.2 * Math.Sin(20 * Math.PI / 180), Ahead = 1.2 * Math.Cos(20 * Math.PI / 180);
        // Task brief on the right, Reader on the left (as before); the parts trays at Computer/Fan sit low on the left (~0.94 m),
        // well below a Reader centred near eye height.
        static readonly Offset Main = new Offset(Side, -.1, Ahead), Reader = new Offset(-Side, -.1, Ahead);
        // Bounded clearance alternatives: raise, then step back, then both. Never toward the head and never swapped to the other side.
        static readonly (double up, double scale)[] Alternatives = { (0, 1), (.3, 1), (0, 1.15), (.3, 1.15) };
        public static int CandidateCount => Alternatives.Length;

        public static double ClampEyeHeight(double measured) =>
            double.IsNaN(measured) || double.IsInfinity(measured) ? DefaultEyeHeight : Math.Max(MinEyeHeight, Math.Min(MaxEyeHeight, measured));

        public static bool Supports(int station, string panelId) =>
            station >= TrainingStation && station <= FanStation && (panelId == "task_information" || panelId == "reference");

        public static bool TryGet(int station, string layout, string panelId, double stationX, double stationY, double stationZ,
            double stationYawDegrees, double eyeHeight, int candidate, out ResearchAnchorPose pose)
        {
            pose = default;
            if (!Supports(station, panelId) || candidate < 0 || candidate >= Alternatives.Length) return false;
            if (station == ComputerStation && layout != "P" && layout != "Q") return false;
            if (double.IsNaN(stationX + stationY + stationZ + stationYawDegrees) || double.IsInfinity(stationX + stationY + stationZ + stationYawDegrees)) return false;
            var offset = panelId == "reference" ? Reader : Main;
            var (up, scale) = Alternatives[candidate];
            double eye = ClampEyeHeight(eyeHeight);
            double yaw = stationYawDegrees * Math.PI / 180;
            // Unity: forward = (sin yaw, 0, cos yaw), right = (cos yaw, 0, -sin yaw).
            double fx = Math.Sin(yaw), fz = Math.Cos(yaw), rx = Math.Cos(yaw), rz = -Math.Sin(yaw);
            double right = offset.Right * scale, forward = offset.Forward * scale;
            pose.X = stationX + rx * right + fx * forward;
            pose.Z = stationZ + rz * right + fz * forward;
            pose.Y = stationY + eye + offset.Up + up;
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
