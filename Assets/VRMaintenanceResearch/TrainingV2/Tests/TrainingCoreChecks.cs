#if TRAINING_CORE_SELF_TEST
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRTrainingV2;

static class TrainingCoreChecks
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static TrainingSnapshot Good() => new TrainingSnapshot { insideTarget = true, supported = true, position = new float[] { 0, 1, 0 }, rotation = new float[] { 0, 0, 0, 1 } };
    static string ReadShared(string path) { using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return reader.ReadToEnd(); }
    static int Main(string[] args)
    {
        try
        {
            if (args[0] == "BASELINE")
            {
                string source = File.ReadAllText(Path.Combine(args[1], "VRSessionManager.cs"));
                var start = source.IndexOf("public void OnInspectButtonPressed()");
                var end = source.IndexOf("private bool ValidateCurrentTaskSockets", start);
                Check(source.Substring(start, end - start).Contains("StopSession();"), "legacy source unconditionally stops session after Inspect");
                Check(source.Contains("expectedGrab.gameObject.name == expectedPart"), "legacy empty socket hierarchy fallback present");
                bool hasSelection = false, expectedExists = true;
                bool legacyPass = hasSelection || expectedExists;
                Check(legacyPass, "isolated legacy predicate reproduces empty target false positive (not Unity runtime)");
                string logger = File.ReadAllText(Path.Combine(args[1], "VRDataLogger.cs"));
                Check(logger.Contains("_ = WriteDataAsync();") && logger.Contains("while (_eventBuffer.TryDequeue(out _))"), "legacy fire-and-forget plus next-start buffer clear source risk");
                Console.WriteLine("BASELINE: 4/4 diagnostic checks; legacy defects reproduced in isolated predicate/source, runtime NOT claimed");
                return 0;
            }
            Directory.CreateDirectory(args[1]);
            using (var s = new TrainingSession(args[1], "Tester", 10))
            {
                var snap = Good();
                Check(s.StartTrial(snap, "Tester"), "start trial");
                string firstTrial = s.TrialId;
                snap.insideTarget = false;
                var empty = s.Inspect(snap, actionId: "press-1", source: "Tester");
                Check(empty.Accepted && !empty.Passed && s.State == TrainingState.Running, "empty target fails and same trial continues");
                Check(!s.Inspect(snap, actionId: "press-1", source: "Tester").Accepted && s.InspectCount == 1, "same input action ID is inspected once");
                snap.insideTarget = true; snap.held = true;
                Check(!s.Inspect(snap, actionId: "press-2", source: "Tester").Passed, "held object fails");
                snap.held = false;
                var passed = s.Inspect(snap, actionId: "press-3", source: "Tester");
                Check(passed.Passed && passed.InspectCount == 3 && passed.RetryCount == 2, "Fail Fail Pass = three Inspect two retry");
                Check(!s.Inspect(snap, source: "Tester").Accepted && s.State == TrainingState.Passed, "completion exactly once; session remains open");
                Check(s.StartTrial(snap, "Tester") && s.TrialId != firstTrial, "explicit new trial retains prior data");
                snap.angleErrorDeg = 90;
                Check(!s.Inspect(snap, source: "Tester").Passed, "wrong orientation fails");
                snap.angleErrorDeg = 0; snap.supported = false;
                Check(!s.Inspect(snap, source: "Tester").Passed, "nearby unsupported object fails");
                snap.supported = true; snap.positionErrorM = float.NaN;
                Check(!s.Inspect(snap, source: "Tester").Passed, "NaN pose evidence fails closed");
                snap.positionErrorM = 0;
                Check(s.Pause(snap, "Tester"), "pause transition");
                double beforePause = s.Timestamp;
                System.Threading.Thread.Sleep(20);
                Check(s.Timestamp > beforePause && !s.Inspect(snap, source: "Tester").Accepted, "pause clock continues and Inspect blocked");
                Check(!s.Record("GRAB", "training_object", "Selected", snap, snap), "paused user task mutation blocked");
                Check(s.Resume(snap, "Tester"), "resume transition");
                s.Record("EXAMINE", "training_object", "neutral information", snap, snap, "Tester");
                snap.position[0] = 100;
                var pose = new TrainingPose { position = new float[] { 1, 2, 3 }, rotation = new float[] { 0, 0, 0, 1 }, trackingValid = true };
                Check(s.SampleMovement(pose, pose, pose, pose), "movement writes shared session clock");
                Check(s.Abort(snap, "Tester"), "abort distinct from failure");
                var events = ReadShared(s.EventsPath).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
                Check(events.Count(e => (string)e["event"] == "INSPECT" && (string)e["trial_id"] == firstTrial) == 3, "disk Inspect count");
                Check(events.Count(e => (string)e["event"] == "TRIAL_COMPLETED" && (string)e["trial_id"] == firstTrial) == 1, "disk completion count");
                Check(events.Where(e => (string)e["event"] == "FAILURE").All(f => events.Any(i => (string)i["event"] == "INSPECT" && (string)i["action_id"] == (string)f["action_id"])), "failure action IDs link one Inspect");
                Check(events.All(e => (string)e["task_id"] == "training_orientation" && (string)e["phase"] == "Training"), "task context never lost");
                Check((float)events.First(e => (string)e["event"] == "EXAMINE")["before"]["position"][0] == 0, "snapshot immutable on disk after original mutation");
                Check(events.Select(e => (long)e["seq"]).SequenceEqual(Enumerable.Range(1, events.Length).Select(i => (long)i)), "event sequence reconstructable");
                Check(ReadShared(s.MovementPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2 && File.ReadAllText(s.MetadataPath).Contains("test_data"), "movement and test-data metadata reopened");
                var movement = ReadShared(s.MovementPath).Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].Split(',');
                var movementTime = double.Parse(movement[0], System.Globalization.CultureInfo.InvariantCulture);
                Check(movementTime >= (double)events.First(e => (string)e["event"] == "EXAMINE")["timestamp"] && movementTime <= (double)events.Last()["timestamp"], "movement timestamp joins event sequence");
                Check((string)events.First(e => (string)e["event"] == "INSPECT")["detail"]["DisplayFeedback"] == empty.DisplayFeedback, "Inspect persists exact displayed feedback");
                Console.WriteLine("LOG_DIRECTORY=" + s.DirectoryPath);
            }
            using (var firstPass = new TrainingSession(args[1], "Tester"))
            {
                firstPass.StartTrial(Good(), "Tester");
                Check(firstPass.Inspect(Good(), source: "Tester").Passed && firstPass.RetryCount == 0, "correct first Inspect passes without forced failure");
            }
            using (var dataError = new TrainingSession(args[1], "Tester"))
            {
                dataError.StartTrial(Good(), "Tester");
                var brokenSnapshot = Good(); brokenSnapshot.angleErrorDeg = float.NaN;
                var errorResult = dataError.Inspect(brokenSnapshot, source: "Tester");
                Check(errorResult.SystemError && errorResult.Accepted && !errorResult.Passed, "invalid snapshot classified system error not participant failure");
                Check(dataError.Inspect(Good(), source: "Tester").Passed && dataError.RetryCount == 0, "system error alone does not establish failed-inspect retry");
                Check(!ReadShared(dataError.EventsPath).Contains("\"event\":\"FAILURE\""), "system error has no FAILURE event");
            }
            var rules = new TrainingRules();
            var invalid = Good(); invalid.rotation = new float[4];
            Check(rules.Validate(invalid) == "invalid_snapshot", "invalid quaternion rejected");
            invalid = Good(); invalid.positionErrorM = 0.046f;
            Check(rules.Validate(invalid) == "position_mismatch", "outside prototype positional tolerance fails");
            invalid = Good(); invalid.speedMps = 0.05f;
            Check(rules.Validate(invalid) == "not_settled", "moving object fails");
            string faultRoot = Path.Combine(args[1], "blocked_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(faultRoot, "test fixture: file blocks directory creation");
            using (var broken = new TrainingSession(faultRoot, "Tester"))
                Check(broken.State == TrainingState.WriteError && !broken.StartTrial(Good()) && !broken.DataComplete, "real filesystem write error blocks completion/restart");
            Console.WriteLine("MODIFIED: " + checks + "/" + checks + " logic/disk checks passed; NOT input-driven runtime evidence");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
#endif
