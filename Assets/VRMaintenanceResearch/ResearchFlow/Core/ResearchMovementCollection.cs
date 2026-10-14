using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Scheduling only. It never manufactures pose observations or changes logger timestamps.
    public sealed class ResearchMovementSchedule
    {
        readonly double interval;
        double next;
        bool initialized;
        public ResearchMovementSchedule(double sampleRateHz)
        {
            if (double.IsNaN(sampleRateHz) || double.IsInfinity(sampleRateHz) || sampleRateHz <= 0 || sampleRateHz > 120)
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            interval = 1d / sampleRateHz;
        }
        public bool Due(double now) => !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0 && now + 1e-9 >= next;
        public void Advance(double now)
        {
            if (!Due(now)) throw new InvalidOperationException("Only a due current observation can advance the schedule.");
            if (!initialized || now - next >= Math.Max(.5, interval * 8))
            { next = now + interval; initialized = true; return; }
            // Keep the cadence independent of render-frame quantization (20Hz over72Hz callbacks).
            double following = next + interval;
            if (following <= now + 1e-9)
                following += (Math.Floor((now - following + 1e-9) / interval) + 1) * interval;
            next = following; // Missed deadlines are skipped, never emitted as historical observations.
        }
    }

    /// <summary>One caller of the existing logger; no session or file-writer ownership.</summary>
    public sealed class ResearchMovementCollection
    {
        public const string ContextSchema = "research.movement-context.1";
        readonly HandsLabLogger logger;
        readonly Dictionary<string, Counts> taskCounts = new Dictionary<string, Counts>();
        readonly ResearchMovementSchedule schedule;
        long segment;
        string previousContext = "", pendingBoundary = "session_start_or_resume";
        bool stopped;
        public long Frames { get; private set; }
        public bool Due => !stopped && logger.State == HandsLabLogState.Active && schedule.Due(logger.Timestamp);
        public sealed class Counts
        {
            public long scheduled_frames, valid_head_frames, valid_left_frames, valid_right_frames;
            public double first_timestamp = double.NaN, last_timestamp = double.NaN, maximum_gap_seconds;
            public object Snapshot() => new { scheduled_frames, valid_head_frames, valid_left_frames, valid_right_frames,
                effective_hz = scheduled_frames > 1 && last_timestamp > first_timestamp ? (double?)(scheduled_frames - 1) / (last_timestamp - first_timestamp) : null,
                maximum_gap_seconds, data_status = scheduled_frames == 0 || valid_head_frames == 0 || valid_left_frames == 0 || valid_right_frames == 0 ? "MISSING_REQUIRED_MOVEMENT" : "OBSERVATIONS_PRESENT_REQUIRES_OFFLINE_QC" };
        }

        public ResearchMovementCollection(HandsLabLogger logger)
        { this.logger = logger ?? throw new ArgumentNullException(nameof(logger)); schedule = new ResearchMovementSchedule(logger.SampleRateHz); }

        public void Boundary(string reason)
        {
            if (!stopped && !string.IsNullOrWhiteSpace(reason))
                pendingBoundary = pendingBoundary.Length == 0 ? reason : pendingBoundary + ";" + reason;
        }

        public bool Tick(HandsLabFrame frame, string phase, string task, string state, bool activeTask, bool paused, bool focused)
        {
            if (!Due) return false;
            if (frame == null || string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(state))
                throw new ArgumentException("Explicit movement frame/context required.");
            double now = logger.Timestamp;
            // No catch-up bursts or invented rows after a stalled frame/application suspension.
            schedule.Advance(now);
            if (paused || !focused)
            {
                string reason = paused ? "session_or_application_paused" : "application_focus_lost";
                frame.head = Invalid(frame.head, reason); frame.leftProvider = Invalid(frame.leftProvider, reason);
                frame.rightProvider = Invalid(frame.rightProvider, reason); frame.leftVisual = Invalid(frame.leftVisual, reason);
                frame.rightVisual = Invalid(frame.rightVisual, reason);
            }
            bool head = Valid(frame.head), left = Valid(frame.leftProvider), right = Valid(frame.rightProvider);
            string context = phase + "/" + task + "/" + state + "/" + paused + "/" + focused + "/" + head + left + right;
            if (context != previousContext || pendingBoundary.Length != 0)
            {
                // Global sequence gives a unique segment across component disable/enable and logger resume.
                segment = logger.LastSequence + 1;
                string reason = pendingBoundary.Length == 0 ? "phase_state_or_tracking_changed" : pendingBoundary;
                if (!logger.Record("MOVEMENT_CONTEXT", "session.movement", "Boundary", state, "None", after: new {
                    context_schema = ContextSchema, phase, task_id = task, flow_state = state, segment_id = segment,
                    active_task = activeTask, paused, focused, required_provider_validity = new { head, left, right }, boundary_reason = reason,
                    coordinate_system = "Unity world left-handed; metres; quaternion xyzw",
                    observation_timestamp_kind = "logger-monotonic provider query/update observation; native sensor timestamps unavailable",
                    attribution = "context applies to subsequent movement rows by global seq until next MOVEMENT_CONTEXT"
                }, source: "System", reason: reason, phaseOverride: phase, taskIdOverride: task)) return false;
                previousContext = context; pendingBoundary = "";
            }
            frame.taskState = state;
            if (!logger.Sample(frame)) return false;
            Frames++;
            if (activeTask)
            {
                if (!taskCounts.TryGetValue(task, out var count)) taskCounts[task] = count = new Counts();
                count.scheduled_frames++; if (head) count.valid_head_frames++; if (left) count.valid_left_frames++; if (right) count.valid_right_frames++;
                if (double.IsNaN(count.first_timestamp)) count.first_timestamp = now;
                if (!double.IsNaN(count.last_timestamp)) count.maximum_gap_seconds = Math.Max(count.maximum_gap_seconds, now - count.last_timestamp);
                count.last_timestamp = now;
            }
            return true;
        }

        public void NoteActiveTask(string task)
        { if (!string.IsNullOrWhiteSpace(task) && !taskCounts.ContainsKey(task)) taskCounts[task] = new Counts(); }

        public bool FlushAndStop(string reason)
        {
            if (stopped) return true;
            stopped = true;
            var tasks = new Dictionary<string, object>();
            foreach (var pair in taskCounts) tasks[pair.Key] = pair.Value.Snapshot();
            return logger.State == HandsLabLogState.Active && logger.Record("MOVEMENT_QC", "session.movement", "RequiresOfflineVerification", "Stopped", "None",
                after: new { context_schema = ContextSchema, reason, scheduled_frames = Frames, target_hz = logger.SampleRateHz, tasks,
                    rate_scope = "actual monotonic observations, no catch-up samples; valid counts are not data-completeness certification" }, source: "System");
        }

        bool Valid(PoseSample pose) => (bool)logger.SnapshotPose(pose)["tracking_valid"];
        static PoseSample Invalid(PoseSample pose, string reason) => new PoseSample { provider = pose?.provider ?? "", inputSource = pose?.inputSource ?? "None",
            sampleTimestamp = pose?.sampleTimestamp ?? double.NaN, trackingValid = false, reason = reason };
    }
}
