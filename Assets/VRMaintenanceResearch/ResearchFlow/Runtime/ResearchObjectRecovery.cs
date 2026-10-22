using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Tutorial practice objects only; Task A/B objects are never auto-recovered.
    // Uses the real Training room geometry. A drop on the table or on the room floor is never a reason.
    public static class ResearchObjectRecovery
    {
        public const float DwellSeconds = 2f, BelowFloorMetres = .5f, RoomMarginMetres = .5f;
        // Only tutorial props: if a dropped object settles on a distant room floor, return it
        // after a longer grace period. Task A/B parts remain intentionally excluded.
        public const float FarFloorDwellSeconds = 8f;
        public const float FarFromStartMetres = 2.25f;
        public const float NearWearerMetres = 1.15f;

        public static bool IsUnreachableFloorDrop(Vector3 position, Vector3 authored, Vector3 head,
            float floorTop, float speed, bool held)
        {
            if (held || !float.IsFinite(speed) || speed > .12f
                || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)
                || !float.IsFinite(authored.x) || !float.IsFinite(authored.z)
                || !float.IsFinite(head.x) || !float.IsFinite(head.z)) return false;
            float height = position.y - floorTop;
            if (height < -.03f || height > .20f) return false;
            Vector2 p = new Vector2(position.x, position.z);
            return Vector2.Distance(p, new Vector2(authored.x, authored.z)) > FarFromStartMetres
                && Vector2.Distance(p, new Vector2(head.x, head.z)) > NearWearerMetres;
        }

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
