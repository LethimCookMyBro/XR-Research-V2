#if RESEARCH_TASK_SNAPSHOT_CHECKS
using System;
using System.Collections.Generic;
using VRMaintenanceResearch.ResearchFlow;

namespace UnityEngine { public class MonoBehaviour { } }

static class ResearchTaskSnapshotChecks
{
    sealed class State
    {
        public readonly int Generation;
        public readonly bool Complete;
        public State(int generation, bool complete) { Generation = generation; Complete = complete; }
    }

    static int checks;
    static void Check(bool ok, string name)
    { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }

    static void Unavailable(ResearchTaskPort port, string name)
    {
        bool passed = true;
        object snapshot = new object();
        bool available = port.TryInspect(out passed, out snapshot);
        Check(!available && !passed && snapshot == null, name + " clears result and snapshot");
    }

    static void Throws<T>(Action action, string name) where T : Exception
    {
        bool threw = false;
        try { action(); } catch (T) { threw = true; }
        Check(threw, name);
    }

    static void SnapshotAndLegacy()
    {
        int captures = 0, evaluations = 0;
        bool enabled = true;
        State captured = null, evaluated = null;
        var port = new ResearchTaskPort { taskId = "A", demoMode = true, demoPassed = true };
        port.BindSnapshot(() => captured = new State(++captures, captures == 1), raw =>
        { evaluations++; evaluated = (State)raw; return evaluated.Complete; }, allowed => enabled = allowed);
        Check(port.HasBoundMechanics && !enabled && !port.InteractionAllowed, "snapshot binding starts physical mechanics disabled");
        Unavailable(port, "paused inspection");
        Check(captures == 0 && evaluations == 0, "paused inspection does not observe or evaluate");
        port.SetInteractionAllowed(true);
        Check(port.TryInspect(out bool passed, out object snapshot) && passed, "first snapshot passes its captured state");
        var envelope = (Dictionary<string, object>)snapshot;
        Check(captures == 1 && evaluations == 1 && ReferenceEquals(captured, evaluated) && ReferenceEquals(evaluated, envelope["task_state"]), "captured evaluated and returned raw state is the same instance");
        Check((bool)envelope["technical_test"] && (bool)envelope["test_data"] && (string)envelope["state_provenance"] == "BoundTechnicalFixture", "technical snapshot retains provenance wrapper");
        Check(port.TryInspect(out passed, out snapshot) && !passed && ((State)((Dictionary<string, object>)snapshot)["task_state"]).Generation == 2,
            "changed state fails fresh inspection instead of retaining success or demoPassed");
        Check(captures == 2 && evaluations == 2 && !port.IsReadyForStudy, "each inspection captures once without approving technical content");
        port.Bind(() => captured = new State(++captures, true), () => { evaluations++; return captured.Complete; }, _ => { });
        Check(!port.InteractionAllowed, "legacy rebind resets interaction gate");
        port.SetInteractionAllowed(true);
        Check(port.TryInspect(out passed) && passed && captures == 3 && evaluations == 3, "legacy Bind and TryInspect signatures keep their behavior");
        Check(port.TryInspect(out passed, out snapshot) && passed && captures == 4 &&
            ReferenceEquals(captured, ((Dictionary<string, object>)snapshot)["task_state"]), "legacy evaluator can return its same captured snapshot to controller");
        port.SetInteractionAllowed(false);
        Unavailable(port, "disabled after successful inspection");
        Check(captures == 4 && evaluations == 4, "disabled inspection does not recapture stale state");
    }

    static void StudyGuards()
    {
        int captures = 0, evaluations = 0;
        var raw = new State(1, true);
        var port = new ResearchTaskPort { taskId = "B" };
        port.BindSnapshot(() => { captures++; return raw; }, value => { evaluations++; return ((State)value).Complete; }, _ => { });
        port.SetInteractionAllowed(true);
        Unavailable(port, "unapproved study content");
        port.contentApproved = true;
        Unavailable(port, "missing reference provider");
        port.BindReference((section, language) => "TEST FIXTURE ONLY");
        Unavailable(port, "missing complete reference media provider");
        bool mediaReady = false;
        port.BindReferenceMediaReadiness(() => mediaReady);
        Unavailable(port, "incomplete reference media");
        Check(captures == 0 && evaluations == 0, "study approval and reference guards run before state capture");
        mediaReady = true;
        Check(port.TryInspect(out bool passed, out object snapshot) && passed && ReferenceEquals(raw, snapshot) && captures == 1 && evaluations == 1,
            "approved fixture study path returns evaluated raw state without technical wrapper");
        Throws<InvalidOperationException>(() => port.Action("fixture", raw, raw, "SimulatedController"), "study still rejects simulated deliberate input");
        port.contentApproved = false;
        Unavailable(port, "revoked content approval");
        port.contentApproved = true;
        mediaReady = false;
        Unavailable(port, "revoked reference media readiness");
        Check(captures == 1 && evaluations == 1, "revoked approval cannot reuse successful snapshot");
        port.demoMode = true;
        port.taskId = "invalid";
        Unavailable(port, "invalid task identity");
        Throws<InvalidOperationException>(() => port.CaptureState(), "CaptureState still rejects invalid task identity");
    }

    static void Failures()
    {
        int errors = 0, captures = 0, evaluations = 0;
        var port = new ResearchTaskPort { taskId = "A", demoMode = true };
        port.Behavior += (name, target, before, after) =>
        {
            Check(name == "SYSTEM_ERROR" && target == "task.A.inspect" && before == null &&
                (string)((Dictionary<string, object>)after)["source"] == "system", "inspection exception is a system error, not participant failure");
            errors++;
        };
        port.BindSnapshot(() => { captures++; return null; }, raw => { evaluations++; return true; }, _ => { });
        port.SetInteractionAllowed(true);
        Unavailable(port, "null state provider result");
        Check(errors == 1 && captures == 1 && evaluations == 0, "null state emits one error without evaluating");
        port.BindSnapshot(() => { captures++; throw new InvalidOperationException("fixture capture failure"); }, raw => { evaluations++; return true; }, _ => { });
        port.SetInteractionAllowed(true);
        Unavailable(port, "throwing state provider");
        Check(errors == 2 && captures == 2 && evaluations == 0, "capture failure emits one error without evaluating");
        port.BindSnapshot(() => { captures++; return new State(1, true); }, raw => { evaluations++; throw new InvalidOperationException("fixture evaluate failure"); }, _ => { });
        port.SetInteractionAllowed(true);
        Unavailable(port, "throwing snapshot evaluator");
        Check(errors == 3 && captures == 3 && evaluations == 1, "evaluation failure does not leak captured snapshot");
        port.BindSnapshot(() => { captures++; return new State(1, true); }, raw => true);
        port.SetInteractionAllowed(true);
        Unavailable(port, "bound technical mechanics without physical gate");
        Check(errors == 4 && captures == 3 && !port.HasBoundMechanics, "missing gate rejects capture instead of taking demo fallback");
        port.BindSnapshot(() => new State(1, true), raw => true, allowed => { if (allowed) throw new InvalidOperationException("fixture gate failure"); });
        Throws<InvalidOperationException>(() => port.SetInteractionAllowed(true), "failing interaction gate reports failure");
        Unavailable(port, "failing physical gate");
        Check(!port.InteractionAllowed && errors == 4, "failing gate remains disabled without a fabricated inspection");
        port.demoMode = false;
        port.contentApproved = true;
        port.BindSnapshot(() => { captures++; return new State(1, true); }, raw => true, _ => { });
        port.BindReference((section, language) => "TEST FIXTURE ONLY");
        port.BindReferenceMediaReadiness(() => throw new InvalidOperationException("fixture readiness failure"));
        port.SetInteractionAllowed(true);
        Unavailable(port, "throwing readiness provider");
        Check(errors == 5 && captures == 3, "readiness failure clears outputs and is recorded before capture");
        Throws<ArgumentNullException>(() => port.BindSnapshot(null, raw => true), "snapshot binding rejects null capture delegate");
        Throws<ArgumentNullException>(() => port.BindSnapshot(() => new State(1, true), null), "snapshot binding rejects null evaluator delegate");
        Throws<ArgumentNullException>(() => port.Bind(() => new State(1, true), null), "legacy binding rejects null evaluator delegate");
    }

    static void RejectedRebind()
    {
        int originalCaptures = 0, replacementCaptures = 0, evaluations = 0, originalGateCalls = 0, replacementGateCalls = 0;
        var original = new State(7, true);
        var port = new ResearchTaskPort { taskId = "A", demoMode = true };
        port.BindSnapshot(() => { originalCaptures++; return original; }, raw => { evaluations++; return ((State)raw).Complete; }, _ => originalGateCalls++);
        port.SetInteractionAllowed(true);
        Throws<ArgumentNullException>(() => port.BindSnapshot(() => { replacementCaptures++; return new State(99, false); }, null, _ => replacementGateCalls++),
            "rebind rejects null evaluator");
        bool available = port.TryInspect(out bool passed, out object snapshot);
        int generation = snapshot == null ? -1 : ((State)((Dictionary<string, object>)snapshot)["task_state"]).Generation;
        Console.WriteLine("rejected_rebind generation=" + generation + "; original_captures=" + originalCaptures + "; replacement_captures=" + replacementCaptures +
            "; evaluations=" + evaluations + "; original_gate_calls=" + originalGateCalls + "; replacement_gate_calls=" + replacementGateCalls + "; allowed=" + port.InteractionAllowed);
        Check(available && passed && generation == 7 && originalCaptures == 1 && replacementCaptures == 0 && evaluations == 1 && originalGateCalls == 2 && replacementGateCalls == 0 && port.InteractionAllowed,
            "rejected null-evaluator rebind retains provider evaluator gate and allowed state");
        Throws<ArgumentNullException>(() => port.BindSnapshot(null, _ => false, _ => replacementGateCalls++), "rebind rejects null capture provider");
        Check(port.TryInspect(out passed, out snapshot) && passed && ReferenceEquals(original, ((Dictionary<string, object>)snapshot)["task_state"]) &&
            originalCaptures == 2 && replacementCaptures == 0 && evaluations == 2 && originalGateCalls == 2 && replacementGateCalls == 0 && port.InteractionAllowed,
            "rejected null-provider rebind retains original binding");
        port.SetInteractionAllowed(false);
        Check(originalGateCalls == 3 && replacementGateCalls == 0, "rejected rebind retains original physical gate delegate");
    }

    static int Main()
    {
        RejectedRebind(); SnapshotAndLegacy(); StudyGuards(); Failures();
        var demo = new ResearchTaskPort { taskId = "B", demoMode = true, demoPassed = true };
        demo.SetInteractionAllowed(true);
        Check(demo.TryInspect(out bool passed, out object snapshot) && passed && (bool)((Dictionary<string, object>)snapshot)["demo_passed"], "unbound legacy demo retains explicit fallback");
        Console.WriteLine("RESEARCH_TASK_SNAPSHOT_PASS checks=" + checks + "; source-level adapter fixtures only, not Unity runtime or study approval");
        return 0;
    }
}
#endif
