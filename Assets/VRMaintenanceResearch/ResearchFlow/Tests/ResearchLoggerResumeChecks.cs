#if RESEARCH_LOGGER_RESUME_CHECKS
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

static class ResearchLoggerResumeChecks
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    static HandsLabLogger Open(string root, string id, bool resume = false, bool testData = true)
    {
        var constructor = typeof(HandsLabLogger).GetConstructors().Single();
        Check(constructor.GetParameters().Any(p => p.Name == "runId"), "persisted run identity API exists");
        return (HandsLabLogger)constructor.Invoke(constructor.GetParameters().Select(p =>
            p.Name == "rootPath" ? (object)root : p.Name == "inputSource" ? "None" : p.Name == "runtime" ? "Fixture" :
            p.Name == "runId" ? id : p.Name == "resume" ? resume : p.Name == "phase" ? "Session" :
            p.Name == "taskId" ? "session" : p.Name == "testData" ? testData : p.DefaultValue).ToArray());
    }
    static string Seed(string root, string id)
    {
        using (var logger = Open(root, id))
        {
            Check(logger.State == HandsLabLogState.Active && logger.RunId == id, "provided run ID used unchanged");
            logger.Record("ACTION", "fixture.A", "Done", "Ready", "None");
            logger.Sample(new HandsLabFrame { taskState = "quoted,\"state\"\nnext" });
            return logger.DirectoryPath;
        }
    }
    static void Reject(string root, string id, Action<string> corrupt, string name)
    {
        string directory = Seed(root, id);
        corrupt(directory);
        var paths = Directory.GetFiles(directory);
        var before = paths.Select(File.ReadAllBytes).ToArray();
        using (var resumed = Open(root, id, true)) Check(resumed.State == HandsLabLogState.WriteError, name);
        Check(paths.Select((p, i) => File.ReadAllBytes(p).SequenceEqual(before[i])).All(x => x), name + " preserves all bytes");
    }
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        string directory = Seed(root, "fixture_resume");
        byte[] prefix = File.ReadAllBytes(Path.Combine(directory, "events.jsonl"));
        double previous = File.ReadAllLines(Path.Combine(directory, "events.jsonl")).Select(JObject.Parse).Max(r => (double)r["timestamp"]);
        using (var resumed = Open(root, "fixture_resume", true))
        {
            Check(resumed.State == HandsLabLogState.Active && resumed.Timestamp > previous, "resume has monotonic timestamp offset");
            var record = typeof(HandsLabLogger).GetMethod("Record");
            Check(record.GetParameters().Any(p => p.Name == "phaseOverride") && record.GetParameters().Any(p => p.Name == "taskIdOverride"), "per-event context API exists");
            record.Invoke(resumed, record.GetParameters().Select(p => p.Name == "eventName" ? (object)"EXAMINE" : p.Name == "target" ? "component.A" :
                p.Name == "result" ? "Observed" : p.Name == "taskState" ? new { installed = true } : p.Name == "handedness" ? "None" :
                p.Name == "phaseOverride" ? "Task1Active" : p.Name == "taskIdOverride" ? "A" : p.DefaultValue).ToArray());
            resumed.Sample(new HandsLabFrame());
            Check(resumed.Complete(), "resumed log completes");
        }
        var rows = File.ReadAllLines(Path.Combine(directory, "events.jsonl")).Select(JObject.Parse).ToArray();
        Check(File.ReadAllBytes(Path.Combine(directory, "events.jsonl")).Take(prefix.Length).SequenceEqual(prefix), "resume appends original bytes unchanged");
        Check(rows.Count(r => (string)r["event"] == "RUN_STARTED") == 1 && rows.Count(r => (string)r["event"] == "RUN_RESUMED") == 1, "one start and one explicit resume");
        Check(rows.Zip(rows.Skip(1), (a, b) => (long)b["seq"] > (long)a["seq"] && (double)b["timestamp"] >= (double)a["timestamp"]).All(x => x), "event sequence and timestamps advance across resume");
        Check((long)rows.Single(r => (string)r["event"] == "RUN_RESUMED")["seq"] > 8, "resume accounts for movement sequence numbers");
        var examine = rows.Single(r => (string)r["event"] == "EXAMINE");
        Check((string)examine["phase"] == "Task1Active" && (string)examine["task_id"] == "A", "per-event task and phase persisted");
        using (var complete = Open(root, "fixture_resume", true)) Check(complete.State == HandsLabLogState.WriteError, "completed run cannot resume");
        Reject(root, "partial_events", p => File.AppendAllText(Path.Combine(p, "events.jsonl"), "{\"event\":"), "partial JSONL tail rejected");
        Reject(root, "partial_csv", p => File.AppendAllText(Path.Combine(p, "movement.csv"), "1,broken"), "partial CSV tail rejected");
        Reject(root, "corrupt_json", p => File.AppendAllText(Path.Combine(p, "events.jsonl"), "garbage\n"), "corrupt JSONL rejected");
        Reject(root, "corrupt_csv", p => File.AppendAllText(Path.Combine(p, "movement.csv"), "1,broken\n"), "corrupt CSV rejected");
        Reject(root, "duplicate_sequence", p => { var file = Path.Combine(p, "events.jsonl"); var lines = File.ReadAllLines(file); var row = JObject.Parse(lines[1]); row["seq"] = 1; lines[1] = row.ToString(Newtonsoft.Json.Formatting.None); File.WriteAllLines(file, lines); }, "duplicate sequence rejected");
        Reject(root, "missing_sequence", p => { var file = Path.Combine(p, "events.jsonl"); var lines = File.ReadAllLines(file); File.WriteAllLines(file, lines.Where((line, i) => i != 1)); }, "missing cross-file sequence rejected");
        Reject(root, "reverse_time", p => { var file = Path.Combine(p, "events.jsonl"); var lines = File.ReadAllLines(file); var row = JObject.Parse(lines[1]); row["timestamp"] = -1; lines[1] = row.ToString(Newtonsoft.Json.Formatting.None); File.WriteAllLines(file, lines); }, "invalid timeline rejected");
        Reject(root, "invalid_flag", p => { var file = Path.Combine(p, "events.jsonl"); var lines = File.ReadAllLines(file); var row = JObject.Parse(lines[1]); row["test_data"] = new JObject(); lines[1] = row.ToString(Newtonsoft.Json.Formatting.None); File.WriteAllLines(file, lines); }, "wrong field types rejected");
        Reject(root, "identity", p => { var file = Path.Combine(p, "session_metadata.json"); var m = JObject.Parse(File.ReadAllText(file)); m["run_id"] = "other"; File.WriteAllText(file, m.ToString()); }, "mismatched metadata rejected");
        Seed(root, "provenance"); using (var mismatch = Open(root, "provenance", true, false)) Check(mismatch.State == HandsLabLogState.WriteError, "test provenance cannot change on resume");
        string duplicate = Seed(root, "duplicate"); var original = File.ReadAllBytes(Path.Combine(duplicate, "events.jsonl"));
        using (var existing = Open(root, "duplicate")) Check(existing.State == HandsLabLogState.WriteError, "new run cannot overwrite existing ID");
        Check(File.ReadAllBytes(Path.Combine(duplicate, "events.jsonl")).SequenceEqual(original), "duplicate start preserves original events");
        using (var writer = Open(root, "exclusive")) using (var second = Open(root, "exclusive", true)) Check(second.State == HandsLabLogState.WriteError, "concurrent writer refused");
        Seed(root, "culture"); var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR"); using (var resumed = Open(root, "culture", true)) Check(resumed.State == HandsLabLogState.Active, "resume numeric parsing is culture independent"); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
        bool invalid = false; try { using var logger = Open(root, "../escape"); } catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is ArgumentException) { invalid = true; }
        Check(invalid, "run ID cannot escape its registry log directory");
        Console.WriteLine("RESEARCH_LOGGER_RESUME_PASS checks=" + checks); return 0;
    }
}
#endif
