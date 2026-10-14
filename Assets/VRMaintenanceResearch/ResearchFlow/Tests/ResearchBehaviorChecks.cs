#if RESEARCH_BEHAVIOR_CHECKS
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.ResearchFlow;

// Source-level adapter contract only. This stub does not simulate Unity lifecycle, physics or input.
namespace UnityEngine { public class MonoBehaviour { } }

static class ResearchBehaviorChecks
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    static string Name(JObject row) => (string)row["event"];
    static JObject Component(bool installed, bool connected, bool incorrect) => JObject.FromObject(new { installed, removed = !installed, connected, incorrectly_placed = incorrect });
    static void Fixture(string root, TaskOrder order)
    {
        string path;
        var flow = new ResearchSessionFlow(order, ResearchFlowState.TutorialComplete, 5);
        using (var logger = new HandsLabLogger(root, "None", "BehaviorFixture", phase: "ResearchSession", taskId: "session", technicalTest: true, testData: true))
        {
            path = logger.EventsPath;
            Check(flow.Continue(), order + " starts first task brief");
            for (int taskIndex = 0; taskIndex < 2; taskIndex++)
            {
                Check(flow.StartTask(), "counterbalanced task active " + flow.CurrentTask);
                string task = flow.CurrentTask;
                var state = new JObject { ["A"] = Component(false, false, false), ["B"] = Component(true, false, true), ["C"] = Component(true, true, false) };
                void Emit(string name, string target, object before = null, object after = null, string action = null, string result = "Observed")
                {
                    Check(logger.Record(name, target, result, state, "Right", before, after, "Tester", actionId: action,
                        inputSourceOverride: "SimulatedController", phaseOverride: "Experiment", taskIdOverride: task), "raw fixture write " + name);
                }
                void Information()
                {
                    string action = logger.NewActionId(); double opened = logger.Timestamp;
                    Emit("INFORMATION_OPEN", "reference.manual", action: action);
                    Emit("INFORMATION_CLOSE", "reference.manual", after: new { exposure_seconds = logger.Timestamp - opened, measurement = "panel_exposure_not_eye_gaze" }, action: action);
                }
                void Inspect(bool passed)
                {
                    string action = logger.NewActionId();
                    Emit("INSPECT", "task." + task, state, state, action);
                    Emit(passed ? "INSPECT_PASS" : "INSPECT_FAIL", "task." + task, state, state, action);
                    Check(flow.Inspect(passed) && !flow.CanInteract, "inspection modal gates action");
                    if (passed) Emit("TASK_COMPLETED", "task." + task);
                    Check(flow.ContinueInspect(), "inspect Continue accepted");
                    if (!passed) Emit("RETRY", "task." + task, result: "Continued");
                }
                Emit("TASK_STARTED", "task." + task);
                Information();
                Emit("ACTION", "A", Component(false, false, false), Component(false, false, false));
                Emit("EXAMINE", "A", state["A"], state["A"]);
                Emit("EXAMINE", "B", state["B"], state["B"]);
                var prior = state["B"].DeepClone(); state["B"] = Component(true, true, true);
                Emit("REPAIR", "B", prior, state["B"]);
                Inspect(false);
                Information(); // Participant-selected first behavior after failure.
                prior = state["B"].DeepClone(); state["B"] = Component(true, true, true);
                Emit("REPAIR", "B", prior, state["B"]);
                Inspect(false);
                Emit("REPAIR", "B", state["B"], state["B"]); // Same target/action remains a distinct raw occurrence.
                Inspect(false);
                prior = state["C"].DeepClone(); state["C"] = Component(false, false, false);
                Emit("REPAIR", "C", prior, state["C"]);
                Inspect(true);
                Check(!flow.CanInteract && !flow.StartTask(), "NASA checkpoint prevents participant task start");
                Check(flow.ResearcherResumeCheckpoint(), "researcher advances checkpoint");
            }
            Check(flow.State == ResearchFlowState.ParticipantComplete && logger.Complete(), order + " completes after second checkpoint");
        }
        var rows = File.ReadAllLines(path).Select(JObject.Parse).ToArray();
        Check(rows.All(r => (bool)r["technical_test"] && (bool)r["test_data"]), "all fixture data explicitly technical");
        // P11: raw fields required for reconstruction; no inferred culture/nationality/strategy/rating fields in the log.
        Check(rows.All(r => new[] { "timestamp", "event", "target", "result" }.All(f => r[f] != null && r[f].Type != JTokenType.Null)
            && ((string)r["phase"] != "Experiment" || r["task_state"].Type != JTokenType.Null)), "every row has timestamp/event/target/result; experiment rows have task_state");
        Check(rows.All(r => r.Properties().All(p => !new[] { "strategy", "score", "culture", "cultural", "nation", "efficien", "group" }.Any(p.Name.Contains))), "no inferred strategy/culture/nationality/rating fields");
        Check(rows.Where(r => Name(r) == "TASK_STARTED").Select(r => (string)r["task_id"]).SequenceEqual(order == TaskOrder.AB ? new[] { "A", "B" } : new[] { "B", "A" }), "saved task order matches assignment");
        foreach (string task in new[] { "A", "B" })
        {
            var events = rows.Where(r => (string)r["task_id"] == task).ToArray();
            var firstAction = events.First(r => Name(r) == "ACTION" || Name(r) == "REPAIR");
            var open = events.First(r => Name(r) == "INFORMATION_OPEN");
            var close = events.First(r => Name(r) == "INFORMATION_CLOSE");
            Check((long)open["seq"] < (long)close["seq"] && (long)close["seq"] < (long)firstAction["seq"] &&
                (string)open["action_id"] == (string)close["action_id"] && (double)close["timestamp"] >= (double)open["timestamp"] &&
                (double)close["after"]["exposure_seconds"] >= 0, task + " H1 information exposure before first action reconstructable");
            Check(events.TakeWhile(r => Name(r) != "REPAIR").Where(r => Name(r) == "EXAMINE").Select(r => (string)r["target"]).SequenceEqual(new[] { "A", "B" }), task + " H2 examined targets and switch before repair reconstructable");
            var inspections = events.Where(r => Name(r) == "INSPECT").ToArray();
            Check((bool)inspections[0]["task_state"]["B"]["incorrectly_placed"] && (bool)inspections[0]["task_state"]["B"]["connected"], task + " H3 first inspection preserves component state");
            Check(events.Count(r => (long)r["seq"] > (long)inspections[0]["seq"] && (long)r["seq"] < (long)inspections[1]["seq"] && (Name(r) == "ACTION" || Name(r) == "REPAIR")) == 1, task + " H3 actions between inspections reconstructable");
            var failures = events.Where(r => Name(r) == "INSPECT_FAIL").ToArray();
            JObject Next(JObject failure) => events.First(r => (long)r["seq"] > (long)failure["seq"] && new[] { "INFORMATION_OPEN", "EXAMINE", "ACTION", "REPAIR" }.Contains(Name(r)));
            Check(Name(Next(failures[0])) == "INFORMATION_OPEN", task + " H4 failure then information retained");
            Check(Name(Next(failures[1])) == "REPAIR" && (string)Next(failures[1])["target"] == "B", task + " H4 repeated repair and target retained");
            Check((string)Next(failures[2])["target"] == "C", task + " H4 changed target retained");
            Check(events.Where(r => Name(r) == "REPAIR").Select(r => (string)r["action_id"]).Distinct().Count() == 4, task + " repeated actions have unique IDs");
            Check(events.Where(r => Name(r) == "INSPECT_FAIL" || Name(r) == "INSPECT_PASS").All(result => inspections.Any(inspect => (string)result["action_id"] == (string)inspect["action_id"])), task + " inspection result correlates to request");
            Check(events.All(r => r.Property("strategy") == null && r.Property("skill_score") == null && Name(r) != "THROW"), task + " raw contract adds no strategy or throw classifications");
        }
        Console.WriteLine("FIXTURE=" + path);
    }
    static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("usage: dotnet run --project ResearchBehaviorChecks.csproj -- <fixture-output-dir>"); return 2; }
        var port = new ResearchTaskPort { taskId = "A", contentApproved = true };
        port.Bind(() => Component(true, true, false), () => true);
        port.BindReference((section, language) => "Approved fixture");
        Check(!port.IsReadyForStudy, "real task without physical interaction gate cannot start study");
        bool physicalEnabled = true;
        port.Bind(() => Component(true, true, false), () => true, allowed => physicalEnabled = allowed);
        Check(!port.IsReadyForStudy, "approved text without complete reference media cannot start study");
        port.BindReferenceMediaReadiness(() => true);
        Check(port.IsReadyForStudy && !physicalEnabled && !port.InteractionAllowed, "binding starts task mechanics disabled");
        Check(!port.TryInspect(out _), "paused task cannot inspect");
        bool rejected = false; try { port.Examine("A", "Controller"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "paused task rejects deliberate interaction");
        port.SetInteractionAllowed(true);
        Check(physicalEnabled && port.TryInspect(out bool passed) && passed, "active task enables mechanics and evaluator");
        port.SetInteractionAllowed(false);
        Check(!physicalEnabled && !port.TryInspect(out _), "inspection modal can disable mechanics independently of UI");
        port.Bind(() => Component(true, true, false), () => true, allowed => { if (allowed) throw new IOException("Fixture gate failure"); });
        rejected = false; try { port.SetInteractionAllowed(true); } catch (IOException) { rejected = true; }
        Check(rejected && !port.InteractionAllowed, "failing physical gate remains unavailable");
        Fixture(args[0], TaskOrder.AB); Fixture(args[0], TaskOrder.BA);
        Console.WriteLine("RESEARCH_BEHAVIOR_PASS checks=" + checks + "; offline raw-log/flow fixtures only, not runtime input or hardware evidence"); return 0;
    }
}
#endif
