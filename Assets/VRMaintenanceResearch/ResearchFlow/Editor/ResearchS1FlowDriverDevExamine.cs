#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 5 Nov 2026: S1 §6.3/§10 intentional Examine in the DEV flavour. The participant opens "View condition" on the task
    // panel and chooses an area; nothing is reached through a guard and nothing moves. Driven by simulated SDK rays only.
    public static partial class ResearchS1FlowDriver
    {
        // Edit mode: every fan state, area and language. One observation id per sentence, and §6.3 access agrees with the
        // B04 context the logger records (observation_accessible_targets).
        public static string DevExamineIdChecks()
        {
            var placements = (S1Placement[])Enum.GetValues(typeof(S1Placement));
            var threads = (S1ThreadState[])Enum.GetValues(typeof(S1ThreadState));
            var bools = new[] { false, true };
            var textForId = new Dictionary<string, string>();
            var idForText = new Dictionary<string, string>();
            int states = 0, pairs = 0, evaluatorRejected = 0, accessCompared = 0;
            foreach (var front in placements) foreach (var aligned in bools) foreach (var clipped in bools)
            foreach (var blade in placements) foreach (var key in bools) foreach (var spinner in threads)
            foreach (var rear in placements) foreach (var rearLock in threads) foreach (var isolated in bools) foreach (var motor in bools)
            {
                var state = new S1TaskBState { systemIntegrityValid = true, powerIsolated = isolated, motorRunning = motor, baseStable = true,
                    frontGuardPlacement = front, frontGuardAligned = aligned, frontGuardClipped = clipped, bladePlacement = blade,
                    bladeCompatible = true, bladeKeyAligned = key, spinner = spinner, rearGuardPlacement = rear, rearGuardAligned = true, rearLock = rearLock };
                states++;
                var context = new S1TaskBState
                {
                    systemIntegrityValid = true, powerIsolated = isolated, motorRunning = motor, baseStable = true, frontGuardPlacement = front,
                    frontGuardAligned = aligned, frontGuardClipped = clipped, bladePlacement = blade, bladeCompatible = true, bladeKeyAligned = key,
                    spinner = spinner, rearGuardPlacement = rear, rearGuardAligned = true, rearLock = rearLock,
                    baseId = "B.base", frontGuardId = "B.front_guard", bladeId = "B.blade", spinnerId = "B.spinner", rearGuardId = "B.rear_guard", rearLockId = "B.rear_guard_lock"
                }.WithB04Context().observation_accessible_targets;
                // The evaluator rejects some combinations as SystemError and then records no context; nothing to compare there.
                if (context == null) evaluatorRejected++;
                var accessible = new HashSet<string>(context ?? new string[0]);
                foreach (var area in ResearchS1FanObservations.Areas)
                {
                    string access = ResearchS1FanObservations.Access(state, area), id = ResearchS1FanObservations.Id(state, area);
                    bool open = access == "accessible";
                    if (context != null) { accessCompared++;
                        Require(open == accessible.Contains(area), "access " + area + " " + access + " disagrees with observation_accessible_targets " + string.Join(",", accessible)); }
                    foreach (var language in new[] { "Thai", "Japanese", "English" })
                    {
                        string text = ResearchS1FanObservations.Get(state, area, language);
                        string idKey = language + "|" + area + "|" + id, textKey = language + "|" + area + "|" + text;
                        Require(!textForId.TryGetValue(idKey, out var seen) || seen == text, "one id, two sentences: " + idKey + " => " + seen + " / " + text);
                        Require(!idForText.TryGetValue(textKey, out var seenId) || seenId == id, "one sentence, two ids: " + textKey + " => " + seenId + " / " + id);
                        textForId[idKey] = text; idForText[textKey] = id; pairs++;
                    }
                }
            }
            var ids = textForId.Keys.Select(k => k.Split('|')[2]).Distinct().OrderBy(k => k).ToArray();
            return JsonConvert.SerializeObject(new { result = "DEV_EXAMINE_ID_PASS", states, evaluator_rejected_states = evaluatorRejected, access_compared = accessCompared, pairs, distinct_ids = ids.Length, ids });
        }

        // Edit mode, owner 5 Nov round 2: a blocked or unavailable area is EXAMINE_REJECTED with its reason, never EXAMINE, and its sentence
        // does not vary with the hidden part state. Uses a real ResearchTaskPort and its Behavior event; no Play mode or focus needed.
        public static string DevExamineAccessChecks(string outPath)
        {
            var events = new List<Dictionary<string, object>>();
            var host = new GameObject("DevExamineAccessChecks") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var port = host.AddComponent<ResearchTaskPort>();
                port.taskId = "B"; port.demoMode = true;
                S1TaskBState current = null;
                port.BindSnapshot(() => current, _ => false, _ => { });
                port.BindExamineDetail(t => ResearchS1TaskBMechanics.ExamineDetail(current, t, "Thai"));
                port.SetInteractionAllowed(true);
                port.Behavior += (name, target, _, after) =>
                {
                    var a = (Dictionary<string, object>)after;
                    events.Add(new Dictionary<string, object> { ["event"] = name, ["target"] = target, ["source"] = "User",
                        ["examine"] = a.TryGetValue("examine", out var d) ? d : null, ["rejected_reason"] = a.TryGetValue("rejected_reason", out var r) ? r : null,
                        ["has_task_state"] = a.TryGetValue("task_state", out var s) && s != null });
                };
                S1TaskBState Fan(bool frontOn, bool motor = false, bool integrity = true) => new S1TaskBState
                {
                    systemIntegrityValid = integrity, powerIsolated = true, motorRunning = motor, baseStable = true,
                    frontGuardPlacement = frontOn ? S1Placement.Seated : S1Placement.NotInstalled, frontGuardAligned = true, frontGuardClipped = frontOn,
                    bladePlacement = S1Placement.Seated, bladeCompatible = true, bladeKeyAligned = true, spinner = S1ThreadState.Tight,
                    rearGuardPlacement = S1Placement.Seated, rearGuardAligned = true, rearLock = S1ThreadState.EngagedLoose
                };
                var cases = new[]
                {
                    ("fitted", Fan(true), "B.front_guard", "EXAMINE", "accessible"),
                    ("fitted", Fan(true), "B.blade_mount", "EXAMINE_REJECTED", "blocked_by_front_guard"),
                    ("fitted", Fan(true), "B.rear_guard_mount", "EXAMINE", "accessible"),
                    ("front_removed", Fan(false), "B.blade_mount", "EXAMINE", "accessible"),
                    ("motor_running", Fan(true, motor: true), "B.front_guard", "EXAMINE_REJECTED", "unavailable"),
                    ("motor_running", Fan(false, motor: true), "B.blade_mount", "EXAMINE_REJECTED", "unavailable"),
                    ("integrity_invalid", Fan(false, integrity: false), "B.rear_guard_mount", "EXAMINE_REJECTED", "unavailable"),
                };
                var rows = new List<object>();
                foreach (var (label, state, area, expectEvent, expectAccess) in cases)
                {
                    current = state;
                    int before = events.Count;
                    bool examined = port.Examine(area, "SimulatedController");
                    Require(events.Count == before + 1, label + " " + area + " emitted exactly one event.");
                    var e = events[before]; var detail = (Dictionary<string, object>)e["examine"];
                    Require((string)e["event"] == expectEvent && examined == (expectEvent == "EXAMINE"), label + " " + area + " event " + e["event"] + ", expected " + expectEvent);
                    Require((string)detail["access_state"] == expectAccess, label + " " + area + " access " + detail["access_state"]);
                    Require(expectEvent == "EXAMINE" ? e["rejected_reason"] == null : (string)e["rejected_reason"] == expectAccess, label + " " + area + " rejected_reason " + e["rejected_reason"]);
                    Require((bool)e["has_task_state"] && !string.IsNullOrEmpty((string)detail["observation_id"]) && !string.IsNullOrEmpty((string)detail["shown_text"]),
                        label + " " + area + " carries snapshot, observation_id and shown text.");
                    rows.Add(new { label, area, e = e["event"], access = detail["access_state"], id = detail["observation_id"], text = detail["shown_text"] });
                }

                // Without the DEV detail binding (Study/Pilot), Examine keeps the earlier EXAMINE payload.
                var plain = new GameObject("DevExamineAccessChecksPlain") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var p = plain.AddComponent<ResearchTaskPort>(); p.taskId = "B"; p.demoMode = true;
                    p.BindSnapshot(() => Fan(true), _ => false, _ => { }); p.SetInteractionAllowed(true);
                    string plainEvent = null; object plainAfter = null;
                    p.Behavior += (name, _, __, after) => { plainEvent = name; plainAfter = after; };
                    Require(p.Examine("B.module", "SimulatedController") && plainEvent == "EXAMINE"
                        && !((Dictionary<string, object>)plainAfter).ContainsKey("examine") && !((Dictionary<string, object>)plainAfter).ContainsKey("rejected_reason"),
                        "Unbound port keeps the earlier EXAMINE payload.");
                }
                finally { UnityEngine.Object.DestroyImmediate(plain); }

                // The sentence shown for a rejected area is the same for every hidden part state (nothing inside is revealed).
                var placements = (S1Placement[])Enum.GetValues(typeof(S1Placement));
                var threads = (S1ThreadState[])Enum.GetValues(typeof(S1ThreadState));
                var bools = new[] { false, true };
                var rejectedTexts = new Dictionary<string, HashSet<string>>();
                int rejectedStates = 0;
                foreach (var language in new[] { "Thai", "Japanese", "English" })
                foreach (var front in placements) foreach (var blade in placements) foreach (var key in bools) foreach (var spinner in threads)
                foreach (var rear in placements) foreach (var rearLock in threads) foreach (var isolated in bools) foreach (var motor in bools) foreach (var integrity in bools)
                {
                    var s = new S1TaskBState { systemIntegrityValid = integrity, powerIsolated = isolated, motorRunning = motor, baseStable = true,
                        frontGuardPlacement = front, frontGuardAligned = true, frontGuardClipped = true, bladePlacement = blade, bladeCompatible = true,
                        bladeKeyAligned = key, spinner = spinner, rearGuardPlacement = rear, rearGuardAligned = true, rearLock = rearLock };
                    foreach (var area in ResearchS1FanObservations.Areas)
                    {
                        string access = ResearchS1FanObservations.Access(s, area);
                        if (access == "accessible") continue;
                        rejectedStates++;
                        string reason = access == "unavailable" ? (integrity ? "unavailable.motor_or_power" : "unavailable.integrity") : access;
                        string key2 = language + "|" + area + "|" + reason;
                        if (!rejectedTexts.TryGetValue(key2, out var set)) rejectedTexts[key2] = set = new HashSet<string>();
                        set.Add(ResearchS1FanObservations.Get(s, area, language));
                    }
                }
                var varying = rejectedTexts.Where(kv => kv.Value.Count != 1).Select(kv => kv.Key + " => " + string.Join(" / ", kv.Value)).ToArray();
                Require(varying.Length == 0, "Rejected sentence varies with hidden state: " + string.Join("; ", varying));

                string json = JsonConvert.SerializeObject(new { result = "DEV_EXAMINE_ACCESS_PASS", cases = rows, port_events = events,
                    rejected_states_checked = rejectedStates, rejected_sentences = rejectedTexts.ToDictionary(kv => kv.Key, kv => kv.Value.Single()) }, Formatting.Indented);
                if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, json);
                return json;
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        static readonly List<object> ExamineRows = new List<object>();
        public static string ExamineReport() => JsonConvert.SerializeObject(new { status = Status, detail = Detail, rows = ExamineRows });

        // Play mode, DEV/TEST Task B active: open the list, choose each area by ray, close. Captures the Game View per page.
        public static string DevExamineTour(string dir, string label) => Start(DevExamineTourRoutine(dir, label), 120);

        static IEnumerator DevExamineTourRoutine(string dir, string label)
        {
            ExamineRows.Clear();
            Directory.CreateDirectory(dir);
            Require(ResearchBuildFlavor.DevReaderPrototype && controller.Record.test_data && controller.Flow.IsTaskActive && controller.Flow.CurrentTask == "B"
                && controller.DevExamineActive, "DEV Examine tour needs the DEV flavour and an active DEV/TEST Task B in Trainer V6.");
            var fan = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>();
            Require(fan && fan.controller == controller, "Task B mechanics bound to this controller.");
            string Snapshot() => JsonConvert.SerializeObject(Strip(fan.Capture()));
            long firstSequence = controller.Logger.LastSequence;
            string before = Snapshot();
            var headTarget = controller.view.mainPanel.transform.position;
            ResearchFlowVisualInput.SetHeadFacing(headTarget);
            yield return Settle();

            yield return RayClick("examine_open");
            yield return Settle();
            var page = MainPage();
            Require(page.title == ResearchStrings.Get("action.examine", controller.Language) && page.body == ResearchStrings.Get("examine.choose", controller.Language),
                "List page shows its title and the choose line: " + JsonConvert.SerializeObject(page));
            var areaButtons = controller.view.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.command.StartsWith("examine_area.", StringComparison.Ordinal)).ToArray();
            Require(areaButtons.Select(a => a.command.Substring(13)).SequenceEqual(ResearchS1FanObservations.Areas), "Three area buttons in front-to-back order.");
            var colors = areaButtons.Select(a => a.GetComponent<Image>()?.color.ToString()).Distinct().ToArray();
            var sizes = areaButtons.Select(a => ((RectTransform)a.transform).rect.size.ToString("F1")).Distinct().ToArray();
            Require(colors.Length == 1 && sizes.Length == 1, "Area buttons share one colour and size: " + string.Join(",", colors) + " / " + string.Join(",", sizes));
            ExamineRows.Add(new { page = "list", page.title, page.body, buttons = areaButtons.Select(a => a.command).ToArray(), colors, sizes,
                state_unchanged = Snapshot() == before, capture = ExamineShot(dir, label + "_0_list") });

            int index = 1;
            foreach (var area in ResearchS1FanObservations.Areas)
            {
                var expectedState = fan.Capture();
                string expected = ResearchS1FanObservations.Get(expectedState, area, controller.Language);
                string expectedId = ResearchS1FanObservations.Id(expectedState, area), expectedAccess = ResearchS1FanObservations.Access(expectedState, area);
                long sequence = controller.Logger.LastSequence;
                yield return RayClick("examine_area." + area);
                yield return Settle();
                page = MainPage();
                Require(page.body == expected, area + " shows its own observation: " + page.body + " / expected " + expected);
                Require(controller.Logger.LastSequence > sequence, area + " wrote at least one event.");
                ExamineRows.Add(new { page = area, page.title, page.body, expected_observation_id = expectedId, expected_access_state = expectedAccess,
                    expected_event = expectedAccess == "accessible" ? "EXAMINE" : "EXAMINE_REJECTED", seq_before = sequence, seq_after = controller.Logger.LastSequence,
                    state_unchanged = Snapshot() == before, capture = ExamineShot(dir, label + "_" + index++ + "_" + area.Substring(2)) });
                Require(Snapshot() == before, "Examining " + area + " left every part state unchanged.");
            }
            yield return RayClick("examine_close");
            yield return Settle();
            page = MainPage();
            ExamineRows.Add(new { page = "closed", page.title, state_unchanged = Snapshot() == before, first_sequence = firstSequence, last_sequence = controller.Logger.LastSequence });
            Require(page.title != ResearchStrings.Get("action.examine", controller.Language), "Back returns to the task panel.");
            File.WriteAllText(Path.Combine(dir, label + "_tour.json"), ExamineReport());
            Step("dev examine tour " + label);
        }

        // Runtime timestamps change every capture; compare part state only.
        static S1TaskBState Strip(S1TaskBState s) { s.capturedAtSeconds = 0; return s; }

        static (string title, string body) MainPage()
        {
            var texts = controller.view.mainPanel.GetComponentsInChildren<TMP_Text>(false);
            string Find(string name) => texts.FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy)?.text;
            return (Find("Title"), Find("Body"));
        }

        static string ExamineShot(string dir, string name)
        {
            string path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
    }
}
#endif
