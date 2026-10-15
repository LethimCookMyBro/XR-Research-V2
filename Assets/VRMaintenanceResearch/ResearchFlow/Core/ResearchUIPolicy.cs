using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Participant UI policy and the protocol/training/layout versions written with every new session
    /// (Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md). The S1 baseline spec is unchanged; the fixed policy
    /// is a versioned demo revision so fixed-panel sessions are never pooled silently with movable-panel ones.
    /// </summary>
    public static class ResearchUIPolicy
    {
        // Revised technical demo: four Reader formats at fixed positions, no participant panel drag/recenter,
        // no panel-drag training exercise, Reader pose kept for the current task.
        public const string FixedDemo = "S1_FIXED_UI_DEMO_20261014";
        // Earlier behaviour (free panel drag + drag lesson). Kept only for technical regression, never a participant choice.
        // Sessions recorded before the policy field existed are reported under this id.
        public const string LegacyMovable = "S1_BASELINE_MOVABLE_PANELS";

        public const string ProtocolBaseline = "S1_LOCKED_TH_BASELINE";
        public const string ProtocolFixedDemo = "S1_LOCKED_TH_BASELINE+ADVISOR_UI_AMENDMENT_20261014";
        public const string TrainingBaseline = "S1_TRAINING_BASELINE_DRAG_LESSON";
        // 20261015: practice-station order builds toward 'ready' without repeated insert/latch/tighten (no session used 20261014).
        public const string TrainingFixedDemo = "S1_TRAINING_FIXED_UI_20261015";
        // Table/station/Task A P/Q geometry is unchanged from the Q5 source in this revision.
        public const string LayoutVersion = "S1_LAYOUT_ENV_V2_Q5_20261014";

        public const string Default = FixedDemo;

        /// <summary>Policy of the open session (or the default before a session). Set only by the session controller.</summary>
        public static string Active { get; private set; } = Default;

        public static bool PanelDragAllowed => Active == LegacyMovable;
        public static bool ParticipantRecenterAllowed => Active == LegacyMovable;
        public static bool ReaderPoseFixedPerTask => Active == FixedDemo;
        public static bool TrainingIncludesPanelDrag => Active == LegacyMovable;
        /// <summary>Working slates use station anchors (ResearchStationAnchors) before any head-relative fallback.</summary>
        public static bool StationAnchoredPanels => Active == FixedDemo;

        public static bool IsKnown(string policy) => policy == FixedDemo || policy == LegacyMovable;

        /// <summary>
        /// Runtime continuity only: a resumed session without a recorded policy keeps the movable-panel behaviour it most likely
        /// started with, so the UI never changes mid-session. This is NOT an analysis attribution - exports report such sessions as
        /// unknown unless the session log shows the policy or drag events (see Tools/research_export.py).
        /// </summary>
        public static string ForRecord(string recordedPolicy) => string.IsNullOrEmpty(recordedPolicy) ? LegacyMovable : recordedPolicy;

        public static string ProtocolFor(string policy) => policy == FixedDemo ? ProtocolFixedDemo : ProtocolBaseline;
        public static string TrainingFor(string policy) => policy == FixedDemo ? TrainingFixedDemo : TrainingBaseline;

        /// <summary>Accepts the ids above plus short operator aliases ("fixed", "legacy_movable").</summary>
        public static string Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Default;
            string v = value.Trim();
            if (string.Equals(v, "fixed", StringComparison.OrdinalIgnoreCase)) return FixedDemo;
            if (string.Equals(v, "legacy_movable", StringComparison.OrdinalIgnoreCase)) return LegacyMovable;
            if (IsKnown(v)) return v;
            throw new ArgumentException("Unknown UI policy: " + value);
        }

        internal static void Apply(string policy)
        {
            if (!IsKnown(policy)) throw new ArgumentException("Unknown UI policy: " + policy);
            Active = policy;
        }

        internal static void Reset() => Active = Default;

        /// <summary>
        /// Editor regression fixtures that still exercise the retired panel-drag lesson run inside this scope.
        /// Not used by any participant path; StartSession always re-applies the policy recorded for the session.
        /// </summary>
        public static IDisposable LegacyRegressionScope() => new Scope(LegacyMovable);

        sealed class Scope : IDisposable
        {
            readonly string previous; bool disposed;
            internal Scope(string policy) { previous = Active; Apply(policy); }
            public void Dispose() { if (disposed) return; disposed = true; Active = previous; }
        }
    }
}
