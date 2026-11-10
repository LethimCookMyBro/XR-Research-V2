using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum PartOperation { Held, Manipulate, Move, Turn, Insert, Remove, Open, Close, Connect, Disconnect, Tighten, Loosen }
    public enum PartBlockReason { None, Unspecified, ClipClosed, GuardNotSeated, Retained, Alignment, EndStop, Contact, CableLimit, OtherHeld, FrontGuardPresent, BladeCapPresent, BladePresent, RearRingPresent, LatchClosed, MountNotReady }

    public readonly struct PartInteraction
    {
        public readonly string AttemptId, TaskId, Target, InputSource;
        public readonly PartOperation Operation;
        public readonly PartBlockReason Reason;
        public readonly long Sequence;
        public readonly float ObservedAt;
        internal PartInteraction(ResearchTaskPort.Attempt attempt, PartOperation operation, PartBlockReason reason, long sequence)
        {
            AttemptId = attempt.Id; TaskId = attempt.TaskId; Target = attempt.Target; InputSource = attempt.InputSource;
            Operation = operation; Reason = reason; Sequence = sequence; ObservedAt = Time.unscaledTime;
        }
    }

    /// <summary>Connection to real task mechanics. This component does not implement or solve a task.</summary>
    public sealed class ResearchTaskPort : MonoBehaviour
    {
        public string taskId = "A";
        public bool contentApproved;
        public bool demoMode;
        public bool demoPassed;

        private Func<object> state;
        private Func<object, bool> evaluate;
        private Func<string, string, string> reference;
        private Func<bool> referenceMediaReady;
        private Func<string, string, string> examineInfo;
        private Action<bool> interactionGate;
        private readonly HashSet<Attempt> attempts = new HashSet<Attempt>();

        long interactionSequence;
        public PartInteraction? CurrentInteraction
        {
            get
            {
                if (!InteractionAllowed || !ValidTask || (!demoMode && !IsReadyForStudy)) return null;
                PartInteraction? latest = null;
                foreach (var attempt in attempts)
                    if (!latest.HasValue || attempt.interaction.Sequence > latest.Value.Sequence)
                        latest = attempt.interaction;
                return latest;
            }
        }

        // A hand near a part, before any grab. Display-only (not logged); every grabbable part reports it the same way.
        string hoverTarget, hoverInput;
        int hoverPointer = int.MinValue;
        public (string Target, string InputSource)? CurrentHover =>
            InteractionAllowed && hoverTarget != null && (demoMode || IsReadyForStudy) ? (hoverTarget, hoverInput) : ((string, string)?)null;

        public void Hover(string target, string inputSource, int pointer)
        {
            if (!InteractionAllowed || string.IsNullOrEmpty(target) || inputSource == "None") return;
            hoverTarget = target; hoverInput = inputSource; hoverPointer = pointer;
        }

        public void Unhover(string target, int pointer)
        {
            if (pointer == hoverPointer && target == hoverTarget) { hoverTarget = null; hoverPointer = int.MinValue; }
        }

        public sealed class Attempt
        {
            public string Id { get; } = Guid.NewGuid().ToString("N");
            public string Target { get; }
            public string InputSource { get; }
            public string TaskId { get; }
            internal PartInteraction interaction;
            internal object lastState, operationStart;
            internal string operation;
            internal readonly HashSet<string> rejected = new HashSet<string>();
            internal Attempt(string target, string input, string task) { Target = target; InputSource = input; TaskId = task; }
        }

        public sealed class AttemptPayload : Dictionary<string, object>
        {
            public string AttemptId { get; }
            public string TaskId { get; }
            public object TaskState { get; }
            public bool SystemTerminal { get; }
            internal AttemptPayload(Attempt attempt, object state, string action, string phase, bool blocked, bool system, bool technical, PartInteraction? observation = null)
            {
                AttemptId = attempt.Id; TaskId = attempt.TaskId; TaskState = state; SystemTerminal = system;
                this["payload_version"] = "research.task-attempt.2";
                var observed = observation ?? attempt.interaction;
                this["interaction_operation"] = observed.Operation.ToString();
                this["block_reason"] = observed.Reason.ToString();
                this["interaction_sequence"] = observed.Sequence;
                this["interaction_observed_unscaled_seconds"] = observed.ObservedAt;
                this["attempt_id"] = attempt.Id; this["action_type"] = action; this["phase"] = phase;
                this["input_source"] = attempt.InputSource; this["task_state"] = state;
                this["source"] = system ? "System" : "User"; this["blocked"] = blocked;
                this["technical_test"] = technical; this["test_data"] = technical;
            }
        }
        public bool InteractionAllowed { get; private set; }
        public bool HasBoundMechanics => state != null && evaluate != null && interactionGate != null;

        // Arguments: event, target, before payload, after payload. Payloads retain raw provenance.
        public event Action<string, string, object, object> Behavior;
        public event Action<string, string, object, object> Observation;

        public bool IsReadyForStudy => ValidTask && !demoMode && (contentApproved || ResearchUIPolicy.StudyOpenWithoutDeviceApprovals) && state != null && evaluate != null && (ResearchUIPolicy.StudyOpenWithoutDeviceApprovals || (reference != null && referenceMediaReady != null && referenceMediaReady())) && interactionGate != null;
        private bool ValidTask => taskId == "A" || taskId == "B";

        public void Bind(Func<object> captureState, Func<bool> evaluator, Action<bool> interactionGate = null)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            BindSnapshot(captureState, _ => evaluator(), interactionGate);
        }

        public void BindSnapshot(Func<object> captureState, Func<object, bool> evaluator, Action<bool> interactionGate = null)
        {
            if (captureState == null) throw new ArgumentNullException(nameof(captureState));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            SetInteractionAllowed(false);
            state = captureState;
            evaluate = evaluator;
            this.interactionGate = interactionGate;
            InteractionAllowed = false;
            interactionGate?.Invoke(false);
        }

        public void SetInteractionAllowed(bool allowed)
        {
            if (InteractionAllowed == allowed) return;
            InteractionAllowed = allowed;
            try { interactionGate?.Invoke(allowed); }
            catch { InteractionAllowed = false; throw; }
            finally { if (!InteractionAllowed) { CancelAttempts(); hoverTarget = null; hoverPointer = int.MinValue; } }
        }

        // Spec §7 fixes one participant sentence per outcome category; the key is resolved by the view in the session language.
        public string FeedbackKey { get; private set; }
        private Func<object, string> feedback;
        public void BindFeedback(Func<object, string> key) => feedback = key ?? throw new ArgumentNullException(nameof(key));

        public void BindReference(Func<string, string, string> content)
        {
            reference = content ?? throw new ArgumentNullException(nameof(content));
        }

        public void BindReferenceMediaReadiness(Func<bool> actualCompleteApprovedPackage)
        {
            referenceMediaReady = actualCompleteApprovedPackage ?? throw new ArgumentNullException(nameof(actualCompleteApprovedPackage));
        }

        // Authored neutral observations are separate from evaluator/state DTOs. Never infer a solution for display.
        public void BindExamineInfo(Func<string, string, string> content)
        {
            examineInfo = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string ExamineInfo(string target, string language)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Examination requires a stable target.");
            if (!ValidTask || !InteractionAllowed || (!demoMode && !IsReadyForStudy) || examineInfo == null)
                return ResearchStrings.Get("examine.info.unavailable", language);
            return examineInfo(target, language) ?? ResearchStrings.Get("examine.info.unavailable", language);
        }

        public object CaptureState() => CaptureState(out _);

        private object CaptureState(out object rawState)
        {
            if (!ValidTask) throw new InvalidOperationException("Task ID must be A or B.");
            if (demoMode)
            {
                if (state != null && !HasBoundMechanics) throw new InvalidOperationException("Bound technical mechanics require their physical interaction gate.");
                rawState = HasBoundMechanics ? state() ?? throw new InvalidOperationException("Technical fixture state provider returned null.") : null;
                if (HasBoundMechanics)
                    return new Dictionary<string, object> { ["task_id"] = taskId, ["technical_test"] = true, ["test_data"] = true,
                        ["state_contract_version"] = "research.task-state.1", ["state_provenance"] = "BoundTechnicalFixture",
                        ["task_state"] = rawState };
                return new Dictionary<string, object> { ["task_id"] = taskId, ["demo_passed"] = demoPassed, ["technical_test"] = true, ["test_data"] = true };
            }
            if (!IsReadyForStudy) throw new InvalidOperationException("Study task requires approved content and bound state, evaluator, and reference provider.");
            rawState = state() ?? throw new InvalidOperationException("Task state provider returned null.");
            return rawState;
        }

        public bool TryInspect(out bool passed) => TryInspect(out passed, out _);

        public bool TryInspect(out bool passed, out object snapshot)
        {
            passed = false;
            snapshot = null;
            try
            {
                if (!InteractionAllowed || !ValidTask || (!demoMode && !IsReadyForStudy)) return false;
                // An unbound demo keeps its explicit bool. Bound technical fixtures observe their actual component DTOs.
                object captured = CaptureState(out object rawState);
                passed = demoMode && !HasBoundMechanics ? demoPassed : evaluate(rawState);
                FeedbackKey = HasBoundMechanics ? feedback?.Invoke(rawState) : null;
                snapshot = captured;
                return true;
            }
            catch (Exception error)
            {
                Behavior?.Invoke("SYSTEM_ERROR", "task." + taskId + ".inspect", null,
                    new Dictionary<string, object> { ["error_type"] = error.GetType().Name, ["source"] = "system", ["technical_test"] = demoMode, ["test_data"] = demoMode });
                return false;
            }
        }

        public string Reference(string section, string language)
        {
            if (!ValidTask) return string.Empty;
            if (!ResearchReferenceScheme.Contains(section)) return string.Empty;
            if (demoMode) return ResearchDemoManual.Get(taskId, section, language);
            if (!IsReadyForStudy) return string.Empty;
            // The owner-open Study reads the shipped Reader packages; no per-port text provider is bound in the app.
            return reference?.Invoke(section, language) ?? string.Empty;
        }

        // §10 EXAMINE fields (observation_id, access_state, shown text). Null keeps the earlier payload unchanged.
        private Func<string, Dictionary<string, object>> examineDetail;
        public void BindExamineDetail(Func<string, Dictionary<string, object>> detail) => examineDetail = detail ?? throw new ArgumentNullException(nameof(detail));
        public Dictionary<string, object> ExamineDetail(string target) => ValidTask && InteractionAllowed && (demoMode || IsReadyForStudy) ? examineDetail?.Invoke(target) : null;

        // §6.3: an area that cannot be seen yet (blocked or unavailable) is logged as EXAMINE_REJECTED, never as EXAMINE, so H2 counts
        // only areas actually examined. Returns whether the area was examined.
        public bool Examine(string target, string inputSource)
        {
            ValidateInteraction(target, inputSource);
            var snapshot = CaptureState();
            var after = (Dictionary<string, object>)Payload(snapshot, inputSource, snapshot);
            var detail = examineDetail?.Invoke(target);
            object access = null;
            bool examined = detail == null || (detail.TryGetValue("access_state", out access) && Equals(access, "accessible"));
            if (detail != null) after["examine"] = detail;
            if (!examined) after["rejected_reason"] = access ?? "access_state_missing";
            Behavior?.Invoke(examined ? "EXAMINE" : "EXAMINE_REJECTED", target, Payload(snapshot, inputSource, snapshot), after);
            return examined;
        }

        public void Action(string target, object before, object after, string inputSource, bool repair = false)
        {
            ValidateInteraction(target, inputSource);
            if (before == null || after == null) throw new ArgumentException("Actions require before and after component state.");
            var snapshot = CaptureState();
            // Full task state is observed after the action; do not label it as a pre-action snapshot.
            Behavior?.Invoke(repair ? "REPAIR" : "ACTION", target, Payload(before, inputSource), Payload(after, inputSource, snapshot));
        }

        public Attempt BeginAttempt(string target, string inputSource)
        {
            ValidateInteraction(target, inputSource);
            if (inputSource == "None" || !HasBoundMechanics) throw new InvalidOperationException("A physical attempt requires observed input and bound mechanics.");
            var attempt = new Attempt(target, inputSource, taskId);
            attempt.lastState = CaptureState();
            attempt.interaction = new PartInteraction(attempt, PartOperation.Held, PartBlockReason.None, ++interactionSequence);
            attempts.Add(attempt);
            EmitAttempt(attempt, "GRAB", null, "started", attempt.lastState, attempt.lastState, false, false);
            return attempt;
        }

        public bool IsAttemptActive(Attempt attempt) => attempt != null && attempts.Contains(attempt) &&
            InteractionAllowed && ValidTask && (demoMode || IsReadyForStudy);

        public bool UpdateInteraction(Attempt attempt, PartOperation operation, PartBlockReason reason)
        {
            if (!IsAttemptActive(attempt)) return false;
            ValidateInteraction(attempt.Target, attempt.InputSource);
            var previous = attempt.interaction;
            attempt.interaction = new PartInteraction(attempt, operation, reason, ++interactionSequence);
            if (previous.Operation != operation || previous.Reason != reason)
                Observation?.Invoke("PART_INTERACTION_CHANGED", attempt.Target,
                    new AttemptPayload(attempt, attempt.lastState, null, "changed", previous.Reason != PartBlockReason.None, false, demoMode, previous),
                    new AttemptPayload(attempt, CaptureState(), null, "changed", reason != PartBlockReason.None, false, demoMode));
            return true;
        }

        public bool UpdateAttempt(Attempt attempt, string actionType, object beforeState, object afterState, bool blocked = false,
            PartOperation operation = PartOperation.Manipulate, PartBlockReason reason = PartBlockReason.Unspecified)
        {
            if (attempt == null || !attempts.Contains(attempt)) return false;
            ValidateInteraction(attempt.Target, attempt.InputSource);
            if (string.IsNullOrWhiteSpace(actionType) || beforeState == null || afterState == null)
                throw new ArgumentException("Attempt operations require a category and full before/after states.");
            var observedReason = blocked ? (reason == PartBlockReason.None ? PartBlockReason.Unspecified : reason) : PartBlockReason.None;
            UpdateInteraction(attempt, operation, observedReason);
            var before = AttemptState(attempt, beforeState);
            var after = AttemptState(attempt, afterState);
            if (blocked)
            {
                if (!attempt.rejected.Add(actionType)) return false;
                EmitAttempt(attempt, actionType == "REPAIR" ? "REPAIR" : "ACTION", actionType, "rejected", before, after, true, false);
            }
            else if (attempt.operation != actionType)
            {
                if (attempt.operation != null)
                    EmitAttempt(attempt, attempt.operation == "REPAIR" ? "REPAIR" : "ACTION", attempt.operation, "ended", attempt.operationStart, before, false, false);
                attempt.operation = actionType;
                attempt.operationStart = before;
                EmitAttempt(attempt, actionType == "REPAIR" ? "REPAIR" : "ACTION", actionType, "started", before, after, false, false);
            }
            attempt.lastState = after;
            return true;
        }

        public bool EndAttempt(Attempt attempt, bool cancelled = false)
        {
            if (attempt == null || !attempts.Remove(attempt)) return false;
            bool system = cancelled || !InteractionAllowed;
            var after = AttemptState(attempt, state());
            string phase = system ? "cancelled" : "ended";
            if (attempt.operation != null)
                EmitAttempt(attempt, attempt.operation == "REPAIR" ? "REPAIR" : "ACTION", attempt.operation, phase, attempt.operationStart, after, false, system);
            EmitAttempt(attempt, "RELEASE", null, phase, attempt.lastState, after, false, system);
            return true;
        }

        private void CancelAttempts()
        {
            Exception failure = null;
            foreach (var attempt in new List<Attempt>(attempts))
                try { EndAttempt(attempt, true); } catch (Exception error) { if (failure == null) failure = error; }
            if (failure != null) throw failure;
        }

        private object AttemptState(Attempt attempt, object raw)
        {
            if (raw == null) throw new InvalidOperationException("Task attempt state provider returned null.");
            return demoMode ? new Dictionary<string, object> { ["task_id"] = attempt.TaskId, ["technical_test"] = true, ["test_data"] = true,
                ["state_contract_version"] = "research.task-state.1", ["state_provenance"] = "BoundTechnicalFixture", ["task_state"] = raw } : raw;
        }

        private void EmitAttempt(Attempt attempt, string name, string action, string phase, object before, object after, bool blocked, bool system)
        {
            Behavior?.Invoke(name, attempt.Target,
                new AttemptPayload(attempt, before, action, phase, blocked, system, demoMode),
                new AttemptPayload(attempt, after, action, phase, blocked, system, demoMode));
        }

        private void ValidateInteraction(string target, string inputSource)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(inputSource)) throw new ArgumentException("Deliberate behavior needs a stable target and input source.");
            if (inputSource != "None" && inputSource != "Controller" && inputSource != "HandTracking" && inputSource != "SimulatedController")
                throw new ArgumentException("Unknown input source.");
            if (!demoMode && inputSource == "SimulatedController") throw new InvalidOperationException("Study mechanics reject synthetic input.");
            if (!InteractionAllowed) throw new InvalidOperationException("Task interaction is paused or unavailable.");
            if (!ValidTask || (!demoMode && !IsReadyForStudy)) throw new InvalidOperationException("Task is not ready for behavior capture.");
        }

        private object Payload(object componentState, string inputSource, object taskState = null)
        {
            return new Dictionary<string, object>
            {
                ["input_source"] = inputSource, ["task_state"] = taskState,
                ["component_state"] = componentState, ["technical_test"] = demoMode, ["test_data"] = demoMode
            };
        }
    }
}
