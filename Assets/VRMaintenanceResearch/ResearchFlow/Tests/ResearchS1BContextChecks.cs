#if RESEARCH_S1_CONTEXT_CHECKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.ResearchFlow;

static class ContextChecks
{
    static int checks, failures;
    static void Check(string name, bool value)
    {
        checks++;
        if (!value) failures++;
        Console.WriteLine((value ? "PASS " : "FAIL ") + name);
    }

    static S1TaskBState Initial() => new S1TaskBState
    {
        systemIntegrityValid = true, capturedAtSeconds = 1, workpieceYawDegrees = 0,
        baseId = "B.base", frontGuardId = "B.front_guard", bladeId = "B.blade", spinnerId = "B.spinner",
        rearGuardId = "B.rear_guard", rearLockId = "B.rear_guard_lock", powerIsolated = true,
        baseStable = true, frontGuardPlacement = S1Placement.Seated, bladePlacement = S1Placement.Seated,
        rearGuardPlacement = S1Placement.Seated, frontGuardAligned = true, frontGuardClipped = true,
        bladeCompatible = true, bladeKeyAligned = true, rearGuardAligned = true,
        spinner = S1ThreadState.Tight, rearLock = S1ThreadState.EngagedLoose
    };

    static void Context(S1TaskBState state)
    {
        typeof(S1TaskBState).GetMethod("WithB04Context")?.Invoke(state, null);
    }

    static int Main(string[] args)
    {
        string path;
        var expected = new Dictionary<string, JObject>();
        using (var writer = new HandsLabLogger(args[0], "SimulatedController", "S1 B context unit fixture, NOT E2E",
            executionEnvironment: "Editor", phase: "Experiment", taskId: "B", technicalTest: true, testData: true))
        {
            path = writer.EventsPath;
            Action<string, Action<S1TaskBState>> record = (name, change) =>
            {
                var before = Initial();
                Context(before);
                var state = Initial(); change(state);
                var evaluation = ResearchS1TaskEvaluator.Evaluate(state).Category;
                Context(state);
                Check(name + "_evaluation_unchanged", evaluation == ResearchS1TaskEvaluator.Evaluate(state).Category);
                expected[name] = JObject.FromObject(state);
                Check(name + "_actual_writer", writer.Record("TEST_CONTEXT_CASE", name, "UnitFixture", state, "None",
                    before, state, "System", "not_simulator_or_human_observation"));
                state.frontGuardClipped = !state.frontGuardClipped;
                Context(state);
            };
            record("initial", s => {});
            record("front_access", s => { s.frontGuardPlacement = S1Placement.NotInstalled; s.frontGuardClipped = false; });
            record("spinner_access", s => s.spinner = S1ThreadState.Absent);
            record("blade_access", s => s.bladePlacement = S1Placement.NotInstalled);
            record("repair", s => s.rearLock = S1ThreadState.Tight);
            record("rear_lock_removed", s => s.rearLock = S1ThreadState.Absent);
            record("rear_guard_misaligned", s => s.rearGuardAligned = false);
            record("blade_misaligned", s => s.bladeKeyAligned = false);
            record("spinner_released_loose", s => { s.rearLock = S1ThreadState.Tight; s.spinner = S1ThreadState.EngagedLoose; });
            record("unsafe_power", s => s.powerIsolated = false);
            record("motor_running", s => s.motorRunning = true);
            record("base_moved", s => s.baseStable = false);
            record("held", s => s.assemblyObstructed = true);
            record("invalid_binding", s => s.systemIntegrityValid = false);
            record("invalid_enum", s => s.rearLock = (S1ThreadState)99);
            record("invalid_identity", s => s.rearLockId = s.bladeId);
        }
        var rows = File.ReadAllLines(path).Select(JObject.Parse).Where(r => (string)r["event"] == "TEST_CONTEXT_CASE").ToArray();
        Check("actual_writer_row_count", rows.Length == expected.Count);
        foreach (var row in rows)
        {
            string target = (string)row["target"];
            Check(target + "_writer_freezes_full_state", JToken.DeepEquals(row["task_state"], expected[target]) && JToken.DeepEquals(row["after"], expected[target]));
            Check(target + "_provenance", (bool)row["test_data"] && (bool)row["technical_test"] && (string)row["input_source"] == "SimulatedController" && (string)row["execution_environment"] == "Editor");
        }
        Func<string, JObject> stateOf = name => (JObject)rows.Single(r => (string)r["target"] == name)["task_state"];
        Func<JObject, string, string[], bool> array = (s, key, values) => s[key] is JArray a && a.Values<string>().SequenceEqual(values);
        var initial = stateOf("initial");
        Check("fault_identity", (string)initial["original_fault_id"] == "B04.rear_guard_lock_engaged_loose");
        Check("fixed_initial_fault_active", (bool?)initial["original_fault_active"] == true);
        Check("initial_not_user_issue", array(initial, "user_created_issues", new string[0]));
        Check("initial_external_observation_access", array(initial, "observation_accessible_targets", new[] { "B.front_guard", "B.rear_guard_mount" }));
        Check("access_not_repair", (bool?)stateOf("front_access")["original_fault_active"] == true);
        Check("front_access_conditions", array(stateOf("front_access"), "user_created_issues", new[] { "front_guard_not_seated", "front_clip_open" }));
        Check("front_open_exposes_blade_target", array(stateOf("front_access"), "observation_accessible_targets", new[] { "B.front_guard", "B.blade_mount", "B.rear_guard_mount" }));
        Check("corrective_repair_resolves_fault", (bool?)stateOf("repair")["original_fault_active"] == false);
        Check("corrective_repair_no_issues", array(stateOf("repair"), "user_created_issues", new string[0]));
        Check("original_resolved_but_user_condition_remains", (bool?)stateOf("spinner_released_loose")["original_fault_active"] == false && array(stateOf("spinner_released_loose"), "user_created_issues", new[] { "spinner_not_tight" }));
        Check("removed_lock_not_hidden_by_original_fault", array(stateOf("rear_lock_removed"), "user_created_issues", new[] { "rear_lock_absent" }));
        Check("rear_alignment_condition", array(stateOf("rear_guard_misaligned"), "user_created_issues", new[] { "rear_guard_not_aligned" }));
        Check("blade_alignment_condition", array(stateOf("blade_misaligned"), "user_created_issues", new[] { "blade_key_not_aligned" }));
        foreach (var name in new[] { "unsafe_power", "motor_running" })
            Check(name + "_no_access", array(stateOf(name), "observation_accessible_targets", new string[0]));
        foreach (var name in new[] { "base_moved", "held", "unsafe_power", "motor_running" })
            Check(name + "_not_attributed_user_error", array(stateOf(name), "user_created_issues", new string[0]));
        foreach (var name in new[] { "invalid_binding", "invalid_enum", "invalid_identity" })
            Check(name + "_unknown_not_false", new[] { "original_fault_active", "user_created_issues", "observation_accessible_targets" }.All(k => stateOf(name)[k]?.Type == JTokenType.Null));
        Check("no_exposure_fabricated", rows.All(r => r["task_state"]?["observation_seen"] == null && r["task_state"]?["human_seen"] == null));
        Check("context_interpretation_is_explicit", (string)initial["context_basis"] == "authored_B04_readiness_delta_not_error_or_intent");
        Console.WriteLine("EVENT_CONTEXT_CHECKS checks=" + checks + " failures=" + failures + " writer=" + path);
        return failures == 0 ? 0 : 1;
    }
}

#endif
