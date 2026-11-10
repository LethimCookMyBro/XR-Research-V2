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
        // User decision 17 Oct 2026: DEV/technical sessions restore participant-initiated panel drag and recenter
        // with station-anchored defaults and no compulsory drag training lesson. Study sessions remain FixedDemo until approved.
        public const string DevMovable = "S1_DEV_MOVABLE_UI_20261017";
        // Owner direction 28 Oct 2026 (Documentation/S1/OWNER_UI_DIRECTION_20261028.md): new sessions of every group restore the
        // single-grip panel drag of DevMovable. Recorded sessions keep the policy they started with.
        public const string OwnerMovable = "S1_MOVABLE_UI_20261028";

        public const string ProtocolBaseline = "S1_LOCKED_TH_BASELINE";
        public const string ProtocolFixedDemo = "S1_LOCKED_TH_BASELINE+ADVISOR_UI_AMENDMENT_20261014";
        public const string ProtocolDevMovable = "S1_LOCKED_TH_BASELINE+DEV_MOVABLE_UI_20261017";
        public const string ProtocolOwnerMovable = "S1_LOCKED_TH_BASELINE+OWNER_MOVABLE_UI_20261028";
        public const string TrainingBaseline = "S1_TRAINING_BASELINE_DRAG_LESSON";
        // 20261015: practice-station order builds toward 'ready' without repeated insert/latch/tighten (no session used 20261014).
        public const string TrainingFixedDemo = "S1_TRAINING_FIXED_UI_20261015";
        // 28 Oct 2026 (Quest session 9b39): trainee inserts the module the fitting way first, then tries the blocked way; a screw
        // at a stop holds there until turned back past ResearchS1FanKinematics.StopDetentDegrees; part-side status labels. Same skills, new order.
        public const string TrainingTrainerV2 = "S1_TRAINING_TRAINER_V2_20261028";
        public const string TrainingTrainerV3 = "S1_TRAINING_TRAINER_V3_20261029";
        public const string TrainingTrainerV4 = "S1_TRAINING_TRAINER_V4_20261030";
        public const string TrainingTrainerV5 = "S1_TRAINING_TRAINER_V5_20261030";
        public const string TrainingTrainerV6 = "S1_TRAINING_TRAINER_V6_20261031";
        public const string TrainingTrainerV7 = "S1_TRAINING_TRAINER_V7_20261106";
        public const string TrainingNoticeV8 = "S1_TRAINING_NOTICE_V8_20261107";
        // Owner 7 Nov (release brief): V8 Examine mechanics, but Training teaches open/switch/close Reader on the neutral T6
        // package and a Pause/Resume practice; no forced tour of all four formats.
        public const string TrainingReaderPauseV9 = "S1_TRAINING_READER_PAUSE_V9_20261107";
        public static bool UsesTutorialNotice(string trainingVersion) => trainingVersion == TrainingNoticeV8;
        public const string IntentionalExamineVersion = "S1_INTENTIONAL_EXAMINE_20261106";
        // Owner 7 Nov: Examine by pointing at the real part (A: the option; B: front guard, blade/spinner or rear guard -> S1 area) and
        // pressing its small chip; the result shows on the main panel. No main-panel Examine button or area menu. Same EXAMINE/REJECTED events.
        public const string PointedExamineVersion = "S1_POINTED_EXAMINE_20261107";
        public static bool UsesPointedExamine(string trainingVersion) => trainingVersion == TrainingReaderPauseV9;
        public static bool UsesIntentionalExamine(string trainingVersion) => trainingVersion == TrainingTrainerV7 || UsesTutorialNotice(trainingVersion)
            || trainingVersion == TrainingReaderPauseV9;
        public static bool UsesPausePractice(string trainingVersion) => trainingVersion == TrainingReaderPauseV9;
        public static string ExamineVersionFor(string trainingVersion, bool legacyDev) => UsesPointedExamine(trainingVersion) ? PointedExamineVersion
            : UsesIntentionalExamine(trainingVersion) ? IntentionalExamineVersion : legacyDev && UsesDirectPartFeedback(trainingVersion) ? DevExamineVersion : null;
        public static string[] ExamineTasksFor(string trainingVersion, bool legacyDev) => UsesIntentionalExamine(trainingVersion)
            ? new[] { "A", "B" } : UsesDirectPartFeedback(trainingVersion) ? (legacyDev ? new[] { "B" } : Array.Empty<string>()) : null;
        public static string ReaderTaskFor(string trainingVersion, string task, bool tutorial, string layoutVersion = null)
        {
            if (tutorial && UsesTutorialNotice(trainingVersion)) return null;
            if (tutorial && UsesPausePractice(trainingVersion)) return "T6";
            string suffix = UsesIntentionalExamine(trainingVersion) ? "7" : trainingVersion == TrainingTrainerV6 ? "6"
                : trainingVersion == TrainingTrainerV5 ? "5" : trainingVersion == TrainingTrainerV4 ? "4" : "";
            // A8 = A7 rewritten for the cables that start on the tray and are plugged at both ends; sessions on older layouts keep A7.
            if (!tutorial && task == "A" && suffix == "7" && layoutVersion == LayoutVersion) return "A8";
            return tutorial ? "T" + (suffix != "" ? suffix : trainingVersion == TrainingTrainerV3 ? "3" : trainingVersion == TrainingTrainerV2 ? "2" : "")
                : string.IsNullOrEmpty(task) ? task : task + suffix;
        }
        public const string InteractionFeedbackVersion = "S1_MAIN_PART_FEEDBACK_HOVER_TURN_20261102";
        // Owner 5 Nov: S1 §6.3/§10 intentional Examine from the task panel (area chosen by the participant, read-only), plus
        // PART_CONDITION_SHOWN for the condition line a GRAB shows. DEV flavour, DEV/TEST sessions only; Study/Pilot unchanged.
        public const string DevExamineVersion = "S1_DEV_INTENTIONAL_EXAMINE_20261105";
        // Owner 2 Nov: one name per control and part, task panel without part counts, restored scope/base wording. AI draft, not human-reviewed.
        public const string ParticipantTextVersion = "S1_PARTICIPANT_TEXT_FRONT_GUARD_MARKS_20261103_AI_DRAFT";
        public static bool UsesDirectPartFeedback(string trainingVersion) => trainingVersion == TrainingTrainerV4 || trainingVersion == TrainingTrainerV5 || trainingVersion == TrainingTrainerV6 || UsesIntentionalExamine(trainingVersion);
        public const string LegacyLayoutVersion = "S1_LAYOUT_ENV_V2_FAN_TASK_FALLBACK_20261015";
        public const string ReachLayoutVersion = "S1_LAYOUT_REACH_20261029";
        public const string LoweredFanLayoutVersion = "S1_LAYOUT_REACH_LOWER_FAN_20261031";
        // Owner 2 Nov: Task A options moved within reach of the case (P/Q mirroring kept), case side panels removed,
        // bench mats and trays given colliders, AC cord behind the case. Fan lowering and station geometry are unchanged.
        public const string OptionsInReachLayoutVersion = "S1_LAYOUT_OPTIONS_IN_REACH_20261102";
        // Owner 7 Nov: same geometry and mechanics; the 24-pin runs in the board-edge channel and the loose SATA data lead first
        // bends rearward inside the case instead of out through the front grille.
        public const string CableTidyLayoutVersion = "S1_LAYOUT_CABLE_TIDY_20261107";
        // Owner 10 Nov (Documentation/S1/AMENDMENT_TASK_EASE_20261110_TH.md): the three task cables (SATA data, SATA power, PCIe)
        // start fully detached, folded on one tray beside the rear end of the case; the participant plugs both ends (board/PSU and
        // SSD/GPU). A head let go away from a port returns to the tray. Parts and W2 criteria are unchanged; Task A Reader is A8.
        public const string LayoutVersion = "S1_LAYOUT_CABLE_TRAY_20261110";
        public static bool LowersFan(string layoutVersion) => layoutVersion == LayoutVersion || layoutVersion == CableTidyLayoutVersion
            || layoutVersion == OptionsInReachLayoutVersion || layoutVersion == LoweredFanLayoutVersion;

        // Owner 29 Oct 2026: Study sessions start without the on-device authorization file, content approval or bilingual-review
        // flags. Every session logs this version; consent and approvals are then handled outside the app by the researcher.
        public const string StudyGateVersion = "S1_OWNER_OPEN_STUDY_20261029";
        public const bool StudyOpenWithoutDeviceApprovals = true;

        public const string Default = OwnerMovable;

        /// <summary>Policy of the open session (or the default before a session). Set only by the session controller.</summary>
        public static string Active { get; private set; } = Default;

        /// <summary>DevMovable and its successor OwnerMovable: station-anchored defaults the participant may drag or recenter.</summary>
        public static bool AnchoredMovable => Active == DevMovable || Active == OwnerMovable;
        public static bool PanelDragAllowed => Active == LegacyMovable || AnchoredMovable;
        public static bool ParticipantRecenterAllowed => Active == LegacyMovable || AnchoredMovable;
        public static bool ReaderPoseFixedPerTask => Active == FixedDemo || AnchoredMovable;
        public static bool TrainingIncludesPanelDrag => Active == LegacyMovable;
        /// <summary>Working slates use station anchors (ResearchStationAnchors) before any head-relative fallback.</summary>
        public static bool StationAnchoredPanels => Active == FixedDemo || AnchoredMovable;

        public static bool IsKnown(string policy) => policy == FixedDemo || policy == LegacyMovable || policy == DevMovable || policy == OwnerMovable;

        // User request 16 Oct 2026: the fixed Reader (~1.8 m, PDF ~19 deg wide) is hard to read. One control toggles between the
        // station anchor and a nearer validated pose; no free drag. Technical sessions only until the researcher approves it
        // for Study (the advisor amendment fixes UI pose for Study).
        public const string ReaderDistanceVersion = "S1_READER_DISTANCE_PRESET_20261016";
        public static bool ReaderDistanceAllowed(bool technical) => ReaderPoseFixedPerTask && technical;

        /// <summary>
        /// Runtime continuity only: a resumed session without a recorded policy keeps the movable-panel behaviour it most likely
        /// started with, so the UI never changes mid-session. This is NOT an analysis attribution - exports report such sessions as
        /// unknown unless the session log shows the policy or drag events (see Tools/research_export.py).
        /// </summary>
        public static string ForRecord(string recordedPolicy) => string.IsNullOrEmpty(recordedPolicy) ? LegacyMovable : recordedPolicy;

        public static string ProtocolFor(string policy) => policy == FixedDemo ? ProtocolFixedDemo : policy == DevMovable ? ProtocolDevMovable
            : policy == OwnerMovable ? ProtocolOwnerMovable : ProtocolBaseline;
        public static string TrainingFor(string policy) => policy == OwnerMovable ? TrainingReaderPauseV9
            : policy == FixedDemo || policy == DevMovable ? TrainingFixedDemo : TrainingBaseline;

        /// <summary>Accepts the ids above plus short operator aliases ("fixed", "dev_movable", "legacy_movable").</summary>
        public static string Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Default;
            string v = value.Trim();
            if (string.Equals(v, "fixed", StringComparison.OrdinalIgnoreCase)) return FixedDemo;
            if (string.Equals(v, "dev_movable", StringComparison.OrdinalIgnoreCase)) return DevMovable;
            if (string.Equals(v, "movable", StringComparison.OrdinalIgnoreCase)) return OwnerMovable;
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
