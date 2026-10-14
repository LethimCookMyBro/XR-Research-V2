using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VRMaintenanceResearch.ResearchFlow
{
    [Serializable]
    public sealed class ParticipantRecord
    {
        [JsonProperty(Required = Required.Always)] public string participant_id, created_at, session_id = "", run_id = "";
        public string started_at = "", ended_at = "";
        [JsonProperty(Required = Required.Always)] public ParticipantGroup group;
        [JsonProperty(Required = Required.Always)] public ParticipantLanguage language;
        [JsonProperty(Required = Required.Always)] public RegistryState session_state;
        [JsonProperty(Required = Required.Always)] public TaskOrder task_order;
        [JsonProperty(Required = Required.Always)] public ExperimentInputMode input_mode;
        public string training_status = "Pending", completion_state = "Pending";
        public bool test_data, task_order_overridden, language_overridden;
        // S1 Task A option layout (P/Q); null on records created before layouts were assigned.
        public string task_a_layout;
        public bool task_a_layout_overridden;
        [JsonProperty(Required = Required.Always)] public ResearchFlowState flow_state;
        [JsonProperty(Required = Required.Always)] public int tutorial_completed;
        public bool paused;
        public bool? inspect_passed;
        [JsonProperty(Required = Required.Always)] public long revision;
        // Written only once a session is ended by the researcher, so untouched registries stay readable by older builds.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string termination_reason;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public List<TerminationAmendment> termination_amendments;
    }

    /// <summary>A later researcher correction of the ending category; the original reason and every change stay on record.</summary>
    [Serializable]
    public sealed class TerminationAmendment
    {
        [JsonProperty(Required = Required.Always)] public string amended_at, from_reason, to_reason, source;
    }

    /// <summary>One local registry, serialized under an OS file lock. Never silently resets corrupt data.</summary>
    public sealed class ResearchParticipantRegistry
    {
        sealed class Document
        {
            [JsonProperty(Required = Required.Always)] public int schema = 1;
            [JsonProperty(Required = Required.Always)] public List<ParticipantRecord> participants = new List<ParticipantRecord>();
        }
        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented, MissingMemberHandling = MissingMemberHandling.Error,
            Converters = { new StringEnumConverter() }
        };
        public string DirectoryPath { get; }
        public string RegistryPath => Path.Combine(DirectoryPath, "registry.json");
        public ResearchParticipantRegistry(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("A registry root is required", nameof(rootPath));
            DirectoryPath = Path.GetFullPath(rootPath);
            Directory.CreateDirectory(DirectoryPath);
        }
        public static bool IsTechnical(ParticipantGroup group) => group == ParticipantGroup.Dev || group == ParticipantGroup.Test || group == ParticipantGroup.Pilot;
        public static string Prefix(ParticipantGroup group)
        {
            switch (group) { case ParticipantGroup.Thai: return "TH"; case ParticipantGroup.Japanese: return "JP";
                case ParticipantGroup.Dev: return "DEV"; case ParticipantGroup.Test: return "TEST"; case ParticipantGroup.Pilot: return "PILOT";
                default: throw new ArgumentOutOfRangeException(nameof(group)); }
        }
        public IReadOnlyList<ParticipantRecord> List(ParticipantGroup group)
        {
            Prefix(group);
            using (AcquireLock()) return Read().participants.Where(p => p.group == group).OrderBy(p => Number(p)).ToArray();
        }
        public ParticipantRecord CreateNext(ParticipantGroup group)
        {
            var prefix = Prefix(group);
            using (AcquireLock())
            {
                var doc = Read();
                int number = checked(doc.participants.Where(p => p.group == group).Select(Number).DefaultIfEmpty(0).Max() + 1);
                var record = new ParticipantRecord { participant_id = prefix + number.ToString(IsTechnical(group) ? "D3" : "D2", CultureInfo.InvariantCulture),
                    group = group, language = group == ParticipantGroup.Japanese ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai,
                    created_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), test_data = IsTechnical(group) };
                doc.participants.Add(record); Save(doc); return record;
            }
        }
        // What Start would assign right now, so researcher surfaces show the Auto cell before it is committed.
        public (TaskOrder order, string layout) PreviewAssignment(ParticipantGroup group, TaskOrder? orderOverride = null, string layoutOverride = null)
        {
            using (AcquireLock()) return Balance(Read(), group, orderOverride, layoutOverride);
        }

        // Order is balanced within the group; layout within the same group and order cell (AB_P/AB_Q/BA_P/BA_Q).
        static (TaskOrder, string) Balance(Document doc, ParticipantGroup group, TaskOrder? orderOverride, string layoutOverride)
        {
            var assigned = doc.participants.Where(p => p.group == group && p.session_state != RegistryState.Available).ToArray();
            int ab = assigned.Count(p => p.task_order == TaskOrder.AB), ba = assigned.Length - ab;
            var order = orderOverride ?? (ab <= ba ? TaskOrder.AB : TaskOrder.BA);
            var cell = assigned.Where(p => p.task_order == order).ToArray();
            return (order, layoutOverride ?? (cell.Count(p => p.task_a_layout == "P") <= cell.Count(p => p.task_a_layout == "Q") ? "P" : "Q"));
        }

        public ParticipantRecord StartOrResume(string participantId, ParticipantLanguage language, ExperimentInputMode inputMode,
            TaskOrder? orderOverride = null, bool technicalOverride = false, string layoutOverride = null)
        {
            if (!Enum.IsDefined(typeof(ParticipantLanguage), language) || !Enum.IsDefined(typeof(ExperimentInputMode), inputMode)
                || (orderOverride.HasValue && !Enum.IsDefined(typeof(TaskOrder), orderOverride.Value))
                || (layoutOverride != null && layoutOverride != "P" && layoutOverride != "Q")) throw new ArgumentException("Invalid session settings");
            using (AcquireLock())
            {
                var doc = Read(); var record = Find(doc, participantId);
                bool technical = IsTechnical(record.group);
                var effectiveInput = record.session_state == RegistryState.InProgress ? record.input_mode : inputMode;
                if (!technical && effectiveInput != ExperimentInputMode.Controller)
                    throw new InvalidOperationException("Study sessions require Controller input; incompatible saved sessions remain unchanged.");
                if (record.session_state == RegistryState.InProgress) return record;
                if (record.session_state != RegistryState.Available) throw new InvalidOperationException("Participant ID is locked: " + participantId);
                var defaultLanguage = record.group == ParticipantGroup.Japanese ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai;
                if (!technical && language != defaultLanguage && !technicalOverride) throw new InvalidOperationException("Study language override requires explicit technical override");
                (record.task_order, record.task_a_layout) = Balance(doc, record.group, orderOverride, layoutOverride);
                record.task_order_overridden = orderOverride.HasValue;
                record.task_a_layout_overridden = layoutOverride != null;
                record.language_overridden = language != defaultLanguage;
                record.language = language; record.input_mode = inputMode;
                record.test_data = technical || technicalOverride;
                record.session_id = Guid.NewGuid().ToString("N"); record.run_id = Guid.NewGuid().ToString("N");
                record.started_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                record.session_state = RegistryState.InProgress; record.flow_state = ResearchFlowState.TutorialWelcome;
                record.revision++; Save(doc); return record;
            }
        }
        public ParticipantRecord SaveProgress(ParticipantRecord expected, ResearchSessionFlow flow)
        {
            if (expected == null || flow == null) throw new ArgumentNullException();
            using (AcquireLock())
            {
                var doc = Read(); var record = Find(doc, expected.participant_id);
                MatchRevision(record, expected);
                if (record.session_state != RegistryState.InProgress || record.task_order != flow.Order) throw new InvalidOperationException("Session is locked or task order differs");
                record.flow_state = flow.State; record.tutorial_completed = flow.TutorialCompleted; record.paused = flow.Paused; record.inspect_passed = flow.InspectPassed;
                record.training_status = flow.TutorialCompleted == 5 ? "Passed" : "Pending";
                if (flow.State >= ResearchFlowState.ParticipantComplete) { record.session_state = RegistryState.Completed; record.completion_state = "Completed"; record.ended_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
                record.revision++; Save(doc); return record;
            }
        }
        public static readonly string[] ResearcherTerminationReasons = { "withdrawal", "safety_stop", "technical_failure", "other" };

        public ParticipantRecord AmendTermination(string participantId, string reason, string source)
        {
            if (Array.IndexOf(ResearcherTerminationReasons, reason) < 0) throw new ArgumentException("Termination reason must be one of " + string.Join(", ", ResearcherTerminationReasons));
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Amendment source required");
            using (AcquireLock())
            {
                var doc = Read(); var record = Find(doc, participantId);
                if (record.session_state != RegistryState.Aborted && record.session_state != RegistryState.Invalid)
                    throw new InvalidOperationException("Only an aborted or invalidated session has a termination reason to amend");
                string current = record.termination_amendments?.LastOrDefault()?.to_reason ?? record.termination_reason ?? "other";
                if (current == reason) return record;
                (record.termination_amendments = record.termination_amendments ?? new List<TerminationAmendment>()).Add(new TerminationAmendment
                    { amended_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), from_reason = current, to_reason = reason, source = source });
                record.revision++; Save(doc); return record;
            }
        }

        public ParticipantRecord Finish(ParticipantRecord expected, RegistryState state, string terminationReason = null)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (state != RegistryState.Aborted && state != RegistryState.Invalid) throw new ArgumentException("Completion is reached through the session flow");
            using (AcquireLock())
            {
                var doc = Read(); var record = Find(doc, expected.participant_id); MatchRevision(record, expected);
                if (record.session_state != RegistryState.InProgress) throw new InvalidOperationException("Session is not active");
                record.session_state = state; record.completion_state = state.ToString(); record.ended_at = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                record.termination_reason = terminationReason; record.revision++; Save(doc); return record;
            }
        }
        static void MatchRevision(ParticipantRecord actual, ParticipantRecord expected)
        {
            if (actual.session_id != expected.session_id || actual.run_id != expected.run_id || actual.revision != expected.revision)
                throw new InvalidOperationException("Stale session snapshot; reload before writing");
        }
        static ParticipantRecord Find(Document doc, string id) => doc.participants.SingleOrDefault(p => p.participant_id == id) ?? throw new ArgumentException("Unknown participant ID");
        static int Number(ParticipantRecord p) => int.Parse(p.participant_id.Substring(Prefix(p.group).Length), CultureInfo.InvariantCulture);
        FileStream AcquireLock()
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { return new FileStream(Path.Combine(DirectoryPath, "registry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (started.ElapsedMilliseconds < 2000) { Thread.Sleep(10); }
            }
        }
        Document Read()
        {
            if (!File.Exists(RegistryPath))
            {
                if (File.Exists(RegistryPath + ".backup")) throw new InvalidDataException("Registry missing while backup exists; researcher recovery required");
                return new Document();
            }
            try { var doc = JsonConvert.DeserializeObject<Document>(File.ReadAllText(RegistryPath, Encoding.UTF8), Json); Validate(doc); return doc; }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is FormatException || e is OverflowException)
            { throw new InvalidDataException("Registry is corrupt; original data preserved", e); }
        }
        static void Validate(Document doc)
        {
            if (doc == null || doc.schema != 1 || doc.participants == null) throw new InvalidDataException("Unsupported or corrupt registry");
            var ids = new HashSet<string>(); var sessions = new HashSet<string>(); var runs = new HashSet<string>();
            foreach (var p in doc.participants)
            {
                if (p == null || string.IsNullOrEmpty(p.participant_id) || !p.participant_id.StartsWith(Prefix(p.group), StringComparison.Ordinal)
                    || Number(p) < 1 || p.participant_id != Prefix(p.group) + Number(p).ToString(IsTechnical(p.group) ? "D3" : "D2", CultureInfo.InvariantCulture)
                    || !ids.Add(p.participant_id) || !Enum.IsDefined(typeof(RegistryState), p.session_state)
                    || !Enum.IsDefined(typeof(ParticipantLanguage), p.language) || !Enum.IsDefined(typeof(TaskOrder), p.task_order)
                    || !Enum.IsDefined(typeof(ExperimentInputMode), p.input_mode) || p.revision < 0
                    || !DateTime.TryParse(p.created_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) throw new InvalidDataException("Invalid participant record");
                new ResearchSessionFlow(p.task_order, p.flow_state, p.tutorial_completed, p.paused, p.inspect_passed);
                if (p.session_state == RegistryState.Available && (p.session_id != "" || p.run_id != "" || p.flow_state != ResearchFlowState.ResearcherSetup || p.revision != 0))
                    throw new InvalidDataException("Available participant contains session reservation data");
                if (p.session_state == RegistryState.Completed && p.flow_state < ResearchFlowState.ParticipantComplete)
                    throw new InvalidDataException("Completed participant has incomplete flow");
                if (p.session_state != RegistryState.Available && (!Guid.TryParseExact(p.session_id, "N", out _) || !Guid.TryParseExact(p.run_id, "N", out _)
                    || !sessions.Add(p.session_id) || !runs.Add(p.run_id))) throw new InvalidDataException("Invalid or duplicate session identity");
            }
        }
        void Save(Document doc)
        {
            Validate(doc);
            string temp = RegistryPath + ".pending-" + Guid.NewGuid().ToString("N");
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(doc, Json));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(RegistryPath)) File.Replace(temp, RegistryPath, RegistryPath + ".backup");
            else File.Move(temp, RegistryPath);
        }
    }
}

