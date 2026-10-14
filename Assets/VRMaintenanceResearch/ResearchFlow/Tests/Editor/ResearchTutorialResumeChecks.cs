#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTutorialResumeChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Require(bool valid, string detail) { if (!valid) throw new InvalidOperationException(detail); }
        static bool Restore(ResearchSessionFlowController controller) => (bool)typeof(ResearchSessionFlowController)
            .GetMethod("RestoreTutorialCredits", Hidden).Invoke(controller, null);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });
        static int Credits(ResearchTutorialAcceptanceChecks.Fixture fixture) => fixture.Events().Count(row => (string)row["event"] == "TUTORIAL_SKILL_CREDIT");
        static byte[] Raw(ResearchTutorialAcceptanceChecks.Fixture fixture)
        {
            using (var stream = new FileStream(fixture.logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
        }
        static void RetainedRaw(byte[] before, ResearchTutorialAcceptanceChecks.Fixture fixture) =>
            Require(Raw(fixture).Take(before.Length).SequenceEqual(before), "Existing raw evidence was not retained byte-for-byte.");

        public static string Run(string evidenceDirectory)
        {
            evidenceDirectory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(evidenceDirectory);
            var results = new List<object>(); int passed = 0, failed = 0;
            void Case(string name, int group, Action<ResearchTutorialAcceptanceChecks.Fixture> action)
            {
                try
                {
                    using (var fixture = new ResearchTutorialAcceptanceChecks.Fixture(Path.Combine(evidenceDirectory, name), group, true)) action(fixture);
                    results.Add(new { name, passed = true }); passed++;
                }
                catch (Exception exception)
                {
                    while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
                    results.Add(new { name, passed = false, detail = exception.Message, exception = exception.ToString() }); failed++;
                }
            }
            Case("same_run", 2, f =>
            {
                f.Present(); f.Held(true); f.Pointer(PointerEventType.Select);
                f.item.transform.position += Vector3.up * .12f; f.Tick();
                Require(f.facts.SetEquals(new[] { "grab", "move" }) && Credits(f) == 2, "Native successful grab/lift did not produce exactly two durable micro-credits.");
                var raw = Raw(f); f.RestartSameRun();
                Require(Restore(f.controller), "Same verified run/session could not restore its own durable credits: " + f.controller.Error);
                Require(f.facts.SetEquals(new[] { "grab", "move" }) && f.controller.TutorialFact == "rotate" && f.controller.Flow.TutorialCompleted == 2,
                    "Resume requested completed handling skills again or advanced an incomplete group.");
                Require(Credits(f) == 2, "Restoring recorded evidence cannot create new skill credits."); RetainedRaw(raw, f);
            });
            Case("disjoint_run", 2, f =>
            {
                f.Present(); f.Held(true); f.Pointer(PointerEventType.Select); Require(Credits(f) == 1, "Fixture native grab credit missing.");
                var raw = Raw(f); f.RestartSameRun();
                var id = f.controller.Registry.CreateNext(ParticipantGroup.Test).participant_id;
                var other = f.controller.Registry.StartOrResume(id, ParticipantLanguage.Thai, ExperimentInputMode.Controller);
                Set(f.controller, "Record", other);
                Require(!Restore(f.controller) && f.facts.Count == 0 && f.controller.Flow.TutorialCompleted == 2,
                    "A new TEST session imported another DEV run's micro-skill credits.");
                Require(f.controller.Error.Contains("identity mismatch"), "Mismatched provenance must fail closed with an explicit diagnostic.");
                Require(Credits(f) == 1, "Rejected restore invented or rewrote raw micro-credit."); RetainedRaw(raw, f);
            });
            Case("save_gap", 0, f =>
            {
                f.Present(); var teleport = new LocomotionEvent(731, Vector3.forward, LocomotionEvent.TranslationType.Absolute);
                f.Requested(teleport); f.Completed(teleport); f.Present();
                var snap = new LocomotionEvent(731, Quaternion.Euler(0, 45, 0), LocomotionEvent.RotationType.Relative);
                f.Requested(snap); f.Completed(snap); f.Present();
                // Only this fixture's native registry lock is held. The actual Save failure leaves a stale saved group after durable credit.
                using (var lease = new FileStream(Path.Combine(f.controller.Registry.DirectoryPath, "registry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                { f.head.localPosition += Vector3.right * .2f; f.Tick(); }
                Require(Credits(f) == 3 && f.controller.Record.tutorial_completed == 0 && f.controller.Flow.TutorialCompleted == 1,
                    "Fixture did not reproduce durable-all-skills-before-persisted-group window.");
                var raw = Raw(f); f.RestartSameRun();
                Require(f.controller.Flow.TutorialCompleted == 0 && Restore(f.controller), "Verified complete group could not reconcile after the save gap: " + f.controller.Error);
                Require(f.controller.Flow.TutorialCompleted == 1 && f.controller.Record.tutorial_completed == 1 && f.facts.Count == 0,
                    "Verified all-skills group was not finalized through the existing flow/persistence path.");
                Require(Credits(f) == 3 && f.Events().Count(row => (string)row["event"] == "TUTORIAL_STEP_RECONCILED") == 1,
                    "Crash reconciliation invented credit or finalized more than once."); RetainedRaw(raw, f);
            });
            string summary = "TUTORIAL_RESUME_" + (failed == 0 ? "PASS" : "FAIL") + " passed=" + passed + " failed=" + failed;
            File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(new { summary, passed, failed, results, hardware_verified = false,
                scope = "Actual Unity controller/native SDK callback + real append-only logger/registry. Explicit fixture restart resets in-memory fields; not wearer or real app process-kill proof." }, Formatting.Indented));
            return summary;
        }
    }
}
#endif
