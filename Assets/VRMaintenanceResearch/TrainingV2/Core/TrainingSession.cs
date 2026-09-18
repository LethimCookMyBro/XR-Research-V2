using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRTrainingV2
{
    public enum TrainingState { Ready, Running, Paused, Passed, Aborted, WriteError }

    [Serializable]
    public sealed class TrainingSnapshot
    {
        public bool held, insideTarget, supported;
        public float positionErrorM, angleErrorDeg, speedMps;
        public string holder = "";
        public float[] position = new float[3];
        public float[] rotation = { 0, 0, 0, 1 };
    }

    [Serializable]
    public sealed class TrainingPose
    {
        public float[] position = new float[3];
        public float[] rotation = { 0, 0, 0, 1 };
        public bool trackingValid;
    }

    [Serializable]
    public sealed class TrainingRules
    {
        // Prototype thresholds, not research acceptance criteria. Caller tests full containment and physical support.
        public float positionToleranceM = 0.045f;
        public float angleToleranceDeg = 12f;
        public float maxSpeedMps = 0.04f;

        public string Validate(TrainingSnapshot s)
        {
            if (s == null || !Finite(s.positionErrorM) || !Finite(s.angleErrorDeg) || !Finite(s.speedMps)
                || !Finite(positionToleranceM) || positionToleranceM <= 0 || !Finite(angleToleranceDeg)
                || angleToleranceDeg <= 0 || !Finite(maxSpeedMps) || maxSpeedMps < 0
                || s.positionErrorM < 0 || s.angleErrorDeg < 0 || s.speedMps < 0
                || s.position == null || s.position.Length != 3 || !s.position.All(Finite)
                || s.rotation == null || s.rotation.Length != 4 || !s.rotation.All(Finite)
                || Math.Abs(s.rotation.Sum(v => v * v) - 1f) > 0.05f) return "invalid_snapshot";
            if (s.held) return "still_held";
            if (!s.insideTarget) return "target_empty_or_outside";
            if (!s.supported) return "not_supported";
            if (s.positionErrorM > positionToleranceM) return "position_mismatch";
            if (s.angleErrorDeg > angleToleranceDeg) return "orientation_mismatch";
            if (s.speedMps > maxSpeedMps) return "not_settled";
            return "pass";
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    public sealed class InspectResult
    {
        public bool Accepted, Passed, SystemError;
        public string Reason, ActionId, DisplayFeedback;
        public int InspectCount, RetryCount;
    }

    /// <summary>V2-owned, main-thread logger. Each action synchronously flushes before state advances.
    /// A session is independent of trials. No Unity clock, participant IDs, Task A/B, or shared research lifecycle.</summary>
    public sealed class TrainingSession : IDisposable
    {
        public const string TaskId = "training_orientation";
        public const string SchemaVersion = "training.events.1";
        public const string ContentVersion = "training.orientation.prototype.1";
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly string inputSource;
        readonly double sampleRateHz;
        readonly DateTime startedUtc = DateTime.UtcNow;
        readonly HashSet<string> inspectedActions = new HashSet<string>();
        StreamWriter eventsWriter, movementWriter;
        long sequence, movementSequence;
        int trialNumber;
        int retryCount;
        bool hasFailedInspect;
        double trialStart, pauseStart, pausedSeconds;
        bool disposed;
        public TrainingRules Rules { get; } = new TrainingRules();
        public TrainingState State { get; private set; } = TrainingState.Ready;
        public string SessionId { get; } = "test_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
        public string TrialId { get; private set; } = "";
        public string DirectoryPath { get; private set; }
        public string EventsPath => Path.Combine(DirectoryPath, "events.jsonl");
        public string MovementPath => Path.Combine(DirectoryPath, "movement.csv");
        public string MetadataPath => Path.Combine(DirectoryPath, "session_metadata.json");
        public string LastError { get; private set; } = "";
        public int InspectCount { get; private set; }
        public int RetryCount => retryCount;
        public double Timestamp => clock.Elapsed.TotalSeconds;
        public double PausedSeconds => pausedSeconds + (State == TrainingState.Paused ? Timestamp - pauseStart : 0);
        public bool DataComplete => State != TrainingState.WriteError && (State == TrainingState.Passed || State == TrainingState.Aborted);
        public event Action<string> WriteFailed;

        public TrainingSession(string rootPath, string inputSource, double sampleRateHz = 10)
        {
            if (inputSource != "Simulator" && inputSource != "Device" && inputSource != "Tester") throw new ArgumentException("inputSource must be Simulator, Device or Tester");
            if (double.IsNaN(sampleRateHz) || double.IsInfinity(sampleRateHz) || sampleRateHz <= 0 || sampleRateHz > 120) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            this.inputSource = inputSource;
            this.sampleRateHz = sampleRateHz;
            DirectoryPath = Path.Combine(rootPath, SessionId);
            TryWrite(() =>
            {
                Directory.CreateDirectory(DirectoryPath);
                eventsWriter = NewWriter(EventsPath);
                movementWriter = NewWriter(MovementPath);
                movementWriter.WriteLine("timestamp,session_id,trial_id,phase,task_id,seq,task_state,input_source," + PoseHeader("head") + "," + PoseHeader("left") + "," + PoseHeader("right") + "," + PoseHeader("origin"));
                movementWriter.Flush();
                using (var metadata = NewWriter(MetadataPath))
                    metadata.Write(JsonConvert.SerializeObject(new { session_id = SessionId, phase = "Training", task_id = TaskId, schema_version = SchemaVersion,
                        content_version = ContentVersion, test_data = true, input_source = inputSource, started_utc = startedUtc.ToString("O"),
                        clock = "Stopwatch monotonic elapsed seconds shared by event/movement", movement_sample_rate_hz = sampleRateHz,
                        units = "position metres; quaternion xyzw; time seconds; angle degrees", coordinates = "Unity world left-handed; XR Origin pose included",
                        head_direction_is_not_eye_gaze = true, language = "th", japanese_status = "draft_unverified", hardware_verified = false }, Formatting.Indented));
            });
            if (State != TrainingState.WriteError) Record("SESSION_STARTED", "training", "test_data", null, null, "System");
        }

        static StreamWriter NewWriter(string path) => new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));

        public bool StartTrial(TrainingSnapshot snapshot, string source = "User")
        {
            if (disposed || State == TrainingState.WriteError || State == TrainingState.Running || State == TrainingState.Paused) return false;
            string prior = TrialId;
            TrialId = "trial_" + (++trialNumber).ToString("D3", CultureInfo.InvariantCulture);
            trialStart = Timestamp; pausedSeconds = 0; InspectCount = 0; retryCount = 0; hasFailedInspect = false;
            if (!WriteEvent("TRIAL_STARTED", "training.block", "Started", snapshot, snapshot, source, NewActionId(), TrainingState.Running,
                new { previous_trial_id = prior, rules = Rules })) return false;
            inspectedActions.Clear();
            State = TrainingState.Running;
            return true;
        }

        public InspectResult Inspect(TrainingSnapshot snapshot, string displayFeedback = null, string actionId = null, string source = "User")
        {
            var result = new InspectResult { Accepted = false, Passed = false, Reason = "state_blocked", ActionId = actionId ?? NewActionId(), InspectCount = InspectCount, RetryCount = RetryCount };
            if (disposed || State != TrainingState.Running) return result;
            if (inspectedActions.Contains(result.ActionId)) { result.Reason = "duplicate_action"; return result; }
            result.Reason = Rules.Validate(snapshot); result.Passed = result.Reason == "pass";
            result.SystemError = result.Reason == "invalid_snapshot";
            result.DisplayFeedback = displayFeedback ?? FeedbackThai(result.Reason);
            result.InspectCount = InspectCount + 1; result.RetryCount = retryCount + (hasFailedInspect ? 1 : 0);
            result.Accepted = true;
            if (!WriteEvent("INSPECT", "training.target", result.SystemError ? "Error" : result.Passed ? "Pass" : "Fail", snapshot, snapshot, source, result.ActionId, State, result)) { result.Accepted = false; return result; }
            inspectedActions.Add(result.ActionId);
            InspectCount++; retryCount = result.RetryCount;
            if (result.SystemError)
            {
                result.Accepted = WriteEvent("SYSTEM_ERROR", "training_snapshot", result.Reason, snapshot, snapshot, "System", result.ActionId, State, null);
                return result;
            }
            if (!result.Passed)
            {
                hasFailedInspect = true;
                result.Accepted = WriteEvent("FAILURE", "training.target", result.Reason, snapshot, snapshot, "System", result.ActionId, State, new { inspect_count = InspectCount, retry_count = RetryCount });
                return result;
            }
            result.Accepted = false;
            if (WriteEvent("TRIAL_COMPLETED", "training", "Pass", snapshot, snapshot, "System", result.ActionId, TrainingState.Passed,
                new { wall_seconds = Timestamp - trialStart, pause_seconds = PausedSeconds, inspect_count = InspectCount, retry_count = RetryCount }))
            { State = TrainingState.Passed; result.Accepted = true; }
            return result;
        }

        public static string FeedbackThai(string reason)
        {
            switch (reason)
            {
                case "pass": return "ผ่านแล้ว วางและหันทิศทางถูกต้อง รอบฝึกนี้สิ้นสุดแล้ว";
                case "still_held": return "ยังถือชิ้นงานอยู่ กรุณาปล่อยลงบนจุดวางก่อนตรวจ";
                case "target_empty_or_outside": return "จุดวางยังว่างหรือชิ้นงานอยู่นอกพื้นที่ กรุณาวางให้ครบภายในจุดวาง";
                case "not_supported": return "ชิ้นงานยังไม่วางบนฐาน กรุณาปล่อยบนจุดวาง";
                case "orientation_mismatch": return "ทิศทางยังไม่ตรง หมุนลูกศรให้ตรงกับลูกศรบนจุดวาง";
                case "position_mismatch": return "ตำแหน่งยังไม่ตรง กรุณาวางให้ตรงกึ่งกลางจุดวาง";
                case "not_settled": return "ชิ้นงานยังเคลื่อนที่ กรุณารอให้หยุดก่อนตรวจอีกครั้ง";
                default: return "ข้อมูลสภาพชิ้นงานไม่พร้อม กรุณาลองตรวจอีกครั้ง";
            }
        }

        public bool Pause(TrainingSnapshot snapshot, string source = "User")
        {
            if (State != TrainingState.Running || disposed) return false;
            pauseStart = Timestamp;
            if (!WriteEvent("PAUSE", "training", "Paused", snapshot, snapshot, source, NewActionId(), TrainingState.Paused, null)) return false;
            State = TrainingState.Paused; return true;
        }
        public bool Resume(TrainingSnapshot snapshot, string source = "User")
        {
            if (State != TrainingState.Paused || disposed) return false;
            double interval = Timestamp - pauseStart;
            if (!WriteEvent("RESUME", "training", "Running", snapshot, snapshot, source, NewActionId(), TrainingState.Running, new { pause_seconds = interval })) return false;
            pausedSeconds += interval; State = TrainingState.Running; return true;
        }
        public bool Abort(TrainingSnapshot snapshot, string source = "User")
        {
            if (disposed || (State != TrainingState.Running && State != TrainingState.Paused)) return false;
            double pause = PausedSeconds;
            if (!WriteEvent("TRIAL_ABORTED", "training", "Aborted", snapshot, snapshot, source, NewActionId(), TrainingState.Aborted,
                new { wall_seconds = Timestamp - trialStart, pause_seconds = pause })) return false;
            pausedSeconds = pause; State = TrainingState.Aborted; return true;
        }

        public bool Record(string eventName, string target, string result, TrainingSnapshot before, TrainingSnapshot after, string source = "User", string actionId = null)
        {
            if (disposed || State == TrainingState.WriteError) return false;
            if (source == "User" && State != TrainingState.Running && !eventName.StartsWith("PANEL_", StringComparison.Ordinal)) return false;
            return WriteEvent(eventName, target, result, before, after, source, actionId ?? NewActionId(), State, null);
        }
        static string NewActionId() => Guid.NewGuid().ToString("N");
        bool WriteEvent(string eventName, string target, string result, TrainingSnapshot before, TrainingSnapshot after, string source, string actionId, TrainingState state, object detail)
        {
            if (source != "User" && source != "System" && source != "Tester") throw new ArgumentException("source must be User, System or Tester");
            // Serialize now; no references or deferred mutable objects are kept in a buffer.
            return TryWrite(() =>
            {
                var row = new { timestamp = Timestamp, @event = eventName, target, result, task_state = state.ToString(), session_id = SessionId, trial_id = TrialId,
                    phase = "Training", task_id = TaskId, seq = sequence + 1, action_id = actionId, schema_version = SchemaVersion, content_version = ContentVersion,
                    input_source = inputSource, source, test_data = true, before = before == null ? null : JToken.FromObject(before), after = after == null ? null : JToken.FromObject(after), detail };
                eventsWriter.WriteLine(JsonConvert.SerializeObject(row)); eventsWriter.Flush(); sequence++;
            });
        }

        public bool SampleMovement(TrainingPose head, TrainingPose left, TrainingPose right, TrainingPose origin)
        {
            if (disposed || State == TrainingState.WriteError) return false;
            return TryWrite(() =>
            {
                string prefix = string.Join(",", Timestamp.ToString("F6", CultureInfo.InvariantCulture), SessionId, TrialId, "Training", TaskId,
                    (++movementSequence).ToString(CultureInfo.InvariantCulture), State.ToString(), inputSource);
                movementWriter.WriteLine(prefix + "," + PoseRow(head) + "," + PoseRow(left) + "," + PoseRow(right) + "," + PoseRow(origin)); movementWriter.Flush();
            });
        }
        static string PoseHeader(string p) => p + "_tracking_valid," + string.Join(",", new[] { "px", "py", "pz", "qx", "qy", "qz", "qw" }.Select(s => p + "_" + s));
        static string PoseRow(TrainingPose p)
        {
            if (p == null) return "false,0,0,0,0,0,0,1";
            if (p.position == null || p.position.Length != 3 || p.rotation == null || p.rotation.Length != 4) throw new ArgumentException("Pose must have position[3], quaternion[4]");
            return (p.trackingValid ? "true," : "false,") + string.Join(",", p.position.Concat(p.rotation).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        }
        bool TryWrite(Action write)
        {
            if (State == TrainingState.WriteError || disposed) return false;
            try { write(); return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            { LastError = ex.GetType().Name + ": " + ex.Message; State = TrainingState.WriteError; WriteFailed?.Invoke(LastError); return false; }
        }
        public void Dispose()
        {
            if (disposed) return;
            if (State == TrainingState.Running || State == TrainingState.Paused) Abort(null, "System");
            if (State != TrainingState.WriteError) Record("SESSION_CLOSED", "training", "Closed", null, null, "System");
            TryWrite(() => { eventsWriter?.Dispose(); movementWriter?.Dispose(); });
            if (State == TrainingState.WriteError) { try { eventsWriter?.Dispose(); } catch (IOException) { } try { movementWriter?.Dispose(); } catch (IOException) { } }
            disposed = true;
        }
    }
}
