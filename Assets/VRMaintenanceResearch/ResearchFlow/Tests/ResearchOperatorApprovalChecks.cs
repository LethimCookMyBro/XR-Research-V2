#if RESEARCH_OPERATOR_APPROVAL_CHECKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.ResearchFlow;

static class ResearchOperatorApprovalChecks
{
    static int checks, cases;
    static readonly List<string> failures = new List<string>();
    static string root;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value) { failures.Add(name); Console.WriteLine("FAIL " + name); }
    }
    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static bool Readiness(string directory, ParticipantRecord record, bool useDirectoryOverride)
    {
#if RESEARCH_OPERATOR_READINESS_SLICE
        UnityEngine.Application.persistentDataPath = useDirectoryOverride ? Path.Combine(root, "unused-default") : Path.GetDirectoryName(directory);
        var adapter = new ResearchOperatorApprovalReadinessAdapter();
        return useDirectoryOverride ? adapter.Query(record, directory) : adapter.HasStudyApproval(record);
#else
        try { ResearchOperatorGuard.Demand("start", record, ResearchOperatorGuard.LoadAuthorization(directory, record, out _)); return true; }
        catch { return false; }
#endif
    }
    static bool Execution(string directory, ParticipantRecord record)
    {
        try
        {
#if RESEARCH_OPERATOR_READINESS_SLICE
            UnityEngine.Application.persistentDataPath = Path.GetDirectoryName(directory);
            var authorization = new ResearchOperatorApprovalReadinessAdapter().ExecutionLoad(record, out _);
#else
            var authorization = ResearchOperatorGuard.LoadAuthorization(directory, record, out _);
#endif
            ResearchOperatorGuard.Demand("start", record, authorization);
            return true;
        }
        catch { return false; }
    }
    static void Run(string name, bool allowed,
        Action<ParticipantRecord, ResearchOperatorAuthorization, ResearchOperatorSessionLink> mutate = null,
        Action<string> files = null, bool saved = true, bool useDirectoryOverride = false)
    {
        string directory = Path.Combine(root, "case" + (cases++).ToString("D2"), "OperatorControl");
        Directory.CreateDirectory(directory);
        var record = new ParticipantRecord { participant_id = "TH_UNIT_FIXTURE", group = ParticipantGroup.Thai, test_data = false,
            session_state = saved ? RegistryState.InProgress : RegistryState.Available, revision = 7,
            session_id = saved ? "UNIT_SESSION" : "", run_id = saved ? "UNIT_RUN" : "" };
        var authorization = new ResearchOperatorAuthorization { schema = "research.operator-authorization.1", operator_id = "UNIT_ONLY_NOT_APPROVAL",
            participant_id = record.participant_id, record_revision = 7, link_status = "pre_session", session_id = "",
            consent_approved = true, content_approved = true, language_approved = true,
            consent_approval_ref = "UNIT_ONLY_NOT_APPROVAL", content_approval_ref = "UNIT_ONLY_NOT_APPROVAL", language_approval_ref = "UNIT_ONLY_NOT_APPROVAL" };
        var link = new ResearchOperatorSessionLink { operator_id = authorization.operator_id, participant_id = record.participant_id,
            session_id = "UNIT_SESSION", run_id = "UNIT_RUN", linked_at = "2026-10-23T00:00:00Z" };
        mutate?.Invoke(record, authorization, link);
        string authPath = Path.Combine(directory, "research-operator-authorization.json");
        string linkPath = Path.Combine(directory, "research-operator-session-link.json");
        File.WriteAllText(authPath, JsonConvert.SerializeObject(authorization), new UTF8Encoding(false));
        link.authorization_sha256 = Hash(File.ReadAllBytes(authPath));
        File.WriteAllText(linkPath, JsonConvert.SerializeObject(link), new UTF8Encoding(false));
        files?.Invoke(directory);
        var before = Directory.GetFiles(directory).ToDictionary(p => p, p => Hash(File.ReadAllBytes(p)));
        bool readiness = Readiness(directory, record, useDirectoryOverride), execution = Execution(directory, record);
        Check(readiness == allowed, name + " readiness expected=" + allowed + " actual=" + readiness);
        Check(execution == allowed, name + " execution expected=" + allowed + " actual=" + execution);
        Check(before.Count == Directory.GetFiles(directory).Length && before.All(p => File.Exists(p.Key) && Hash(File.ReadAllBytes(p.Key)) == p.Value), name + " input files unchanged");
        Console.WriteLine("CASE " + name + " readiness=" + readiness + " execution=" + execution + " expected=" + allowed);
    }
    static void ChangeJson(string directory, string filename, Action<JObject> change)
    {
        string path = Path.Combine(directory, filename);
        var value = JObject.Parse(File.ReadAllText(path)); change(value);
        File.WriteAllText(path, value.ToString(Formatting.None), new UTF8Encoding(false));
    }
    static void Main(string[] args)
    {
        root = Path.GetFullPath(args.Single());
        Directory.CreateDirectory(root);
        const string authFile = "research-operator-authorization.json", linkFile = "research-operator-session-link.json";
        Run("available exact pre-session revision", true, saved: false);
        Run("saved exact hash-bound session and run link", true);
        Run("explicit operator directory overrides unrelated default", true, useDirectoryOverride: true);
        Run("saved missing link", false, files: d => File.Delete(Path.Combine(d, linkFile)));
        Run("saved wrong authorization hash", false, files: d => ChangeJson(d, linkFile, j => j["authorization_sha256"] = new string('0', 64)));
        Run("saved foreign operator", false, (r,a,l) => l.operator_id = "FOREIGN_UNIT_OPERATOR");
        Run("saved foreign participant", false, (r,a,l) => l.participant_id = "TH_FOREIGN_UNIT_FIXTURE");
        Run("saved foreign session", false, (r,a,l) => l.session_id = "FOREIGN_UNIT_SESSION");
        Run("saved foreign run", false, (r,a,l) => l.run_id = "FOREIGN_UNIT_RUN");
        Run("saved unknown link schema", false, (r,a,l) => l.schema = "research.operator-session-link.999");
        Run("saved empty linked session", false, (r,a,l) => l.session_id = "");
        Run("saved empty linked run", false, (r,a,l) => l.run_id = "");
        Run("foreign authorization participant", false, (r,a,l) => a.participant_id = "TH_FOREIGN_UNIT_FIXTURE");
        Run("missing operator", false, (r,a,l) => { a.operator_id = " "; l.operator_id = " "; });
        Run("unknown authorization schema", false, (r,a,l) => a.schema = "research.operator-authorization.999");
        Run("missing consent acknowledgment", false, (r,a,l) => a.consent_approved = null);
        Run("missing consent reference", false, (r,a,l) => a.consent_approval_ref = "");
        Run("content not approved", false, (r,a,l) => a.content_approved = false);
        Run("missing content reference", false, (r,a,l) => a.content_approval_ref = " ");
        Run("language not approved", false, (r,a,l) => a.language_approved = false);
        Run("missing language reference", false, (r,a,l) => a.language_approval_ref = null);
        Run("available wrong record revision", false, (r,a,l) => a.record_revision = 6, saved: false);
        Run("available requires pre-session not linked", false, (r,a,l) => { a.link_status = "linked"; a.session_id = "UNIT_SESSION"; }, saved: false);
        // S1_OWNER_OPEN_STUDY_20261029: with no authorization file on the device the owner opened Study start; a present file is still validated.
        Run("missing authorization", ResearchUIPolicy.StudyOpenWithoutDeviceApprovals, files: d => File.Delete(Path.Combine(d, authFile)));
        Run("approval string is not JSON boolean", false, files: d => ChangeJson(d, authFile, j => j["consent_approved"] = "true"));
        Run("oversized authorization rejected before parsing", false, files: d => File.AppendAllText(Path.Combine(d, authFile), new string(' ', 16384)), saved: false);
        Run("oversized link rejected before parsing", false, files: d => File.AppendAllText(Path.Combine(d, linkFile), new string(' ', 16384)));
        Run("malformed authorization", false, files: d => File.WriteAllText(Path.Combine(d, authFile), "{"));
        Run("malformed link", false, files: d => File.WriteAllText(Path.Combine(d, linkFile), "{"));
        Run("invalid UTF8 authorization", false, files: d => File.WriteAllBytes(Path.Combine(d, authFile), new byte[] { 0xff, 0xff }));
        Run("unknown expiry field in authorization is rejected", false, files: d => ChangeJson(d, authFile, j => j["expires_at"] = "2000-01-01T00:00:00Z"));
        Run("stale prior-run link rejected", false, (r,a,l) => { l.linked_at = "2000-01-01T00:00:00Z"; l.run_id = "STALE_UNIT_RUN"; });
        Run("age alone does not expire matching schema-v1 link", true, (r,a,l) => l.linked_at = "2000-01-01T00:00:00Z");
        Run("legacy directly-linked authorization still requires exact session", true, (r,a,l) => { a.link_status = "linked"; a.session_id = "UNIT_SESSION"; }, d => File.Delete(Path.Combine(d, linkFile)));
        Run("direct-linked foreign session rejected", false, (r,a,l) => { a.link_status = "linked"; a.session_id = "FOREIGN_UNIT_SESSION"; });
        foreach (var group in new[] { ParticipantGroup.Dev, ParticipantGroup.Test, ParticipantGroup.Pilot })
            Run(group + " test-data contract unchanged without documents", true, (r,a,l) => { r.group = group; r.test_data = true; }, d => { File.Delete(Path.Combine(d, authFile)); File.Delete(Path.Combine(d, linkFile)); });
        var summary = new { status = failures.Count == 0 ? "PASS" : "FAIL", cases, checks, failures,
            scope = "Pure core and optional exact readiness source-slice checks; UNIT_ONLY_NOT_APPROVAL; no participant registry or Unity/native execution",
            time_expiry = "NOT_MODELED_SCHEMA_V1; age alone does not expire a matching link", hardware_verified = false, study_approved = false };
        File.WriteAllText(Path.Combine(root, "result.json"), JsonConvert.SerializeObject(summary, Formatting.Indented), new UTF8Encoding(false));
        Console.WriteLine("OPERATOR_APPROVAL_" + summary.status + " cases=" + cases + " checks=" + checks + " failures=" + failures.Count);
        Environment.ExitCode = failures.Count == 0 ? 0 : 1;
    }
}
#endif
