using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Tutorial practice objects only; Task A/B objects are never auto-recovered.
    // Uses the real Training room geometry. A drop on the table or on the room floor is never a reason.
    public static class ResearchObjectRecovery
    {
        public const float DwellSeconds = 2f, BelowFloorMetres = .5f, RoomMarginMetres = .5f;

        public static string Reason(Vector3 position, Bounds room, float floorTop, bool embedded)
        {
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) return "non_finite";
            if (position.y < floorTop - BelowFloorMetres) return "below_floor";
            room.Expand(2 * RoomMarginMetres);
            if (!room.Contains(position)) return "out_of_bounds";
            return embedded ? "unreachable" : null;
        }

        // The state must persist while released; holding restarts the wait, so a throw is never reset mid-flight.
        public static bool Due(bool held, float lostSeconds) => !held && lostSeconds >= DwellSeconds;
    }
}
