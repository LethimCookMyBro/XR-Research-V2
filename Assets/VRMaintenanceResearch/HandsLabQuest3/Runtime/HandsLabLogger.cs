using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRMaintenanceResearch.HandsLabQuest3.Logging
{
    /// <summary>Positive device observations determine source; disappearance never implies hardware.</summary>
    public sealed class ControllerSourceAttribution
    {
        public string LastObserved { get; private set; } = "None";
        public string Observe(bool simulatedPresent, bool physicalPresent)
        {
            if(simulatedPresent) LastObserved="SimulatedController";
            else if(physicalPresent) LastObserved="Controller";
            return LastObserved;
        }
    }
    [Serializable]
    public sealed class PoseSample
    {
        public float[] position, rotation;
        public bool trackingValid;
        // Timestamp of the actual provider update, not the time a cached transform was read.
        public double sampleTimestamp = double.NaN;
        public string provider = "", reason = "", inputSource = "";
    }

    [Serializable]
    public sealed class HandsLabFrame
    {
        public PoseSample head, leftProvider, rightProvider, leftVisual, rightVisual, origin;
        public string taskState = "Idle", inputSource = "";
    }

    // Lifecycle status is independent of the active controller/hand input mode.
    public enum HandsLabLogState { Active, Completed, Incomplete, WriteError }

    /// <summary>Main-thread technical-test logger. Files are unique; every action is flushed
    /// synchronously. Provider-reported poses and synthetic visuals are never interchangeable.</summary>
    public sealed class HandsLabLogger : IDisposable
    {
        public const string SchemaVersion = "handslab.events.1";
        public const string ContentVersion = "handslab.quest3.technical.1";
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly string inputSource, runtime, executionEnvironment;
        StreamWriter eventsWriter, movementWriter;
        long sequence, frameNumber;
        bool disposed;
        public double MaxPoseAgeSeconds { get; }
        public HandsLabLogState State { get; private set; } = HandsLabLogState.Active;
        public string RunId { get; } = "handslab_test_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
        public string DirectoryPath { get; }
        public string EventsPath => Path.Combine(DirectoryPath, "events.jsonl");
        public string MovementPath => Path.Combine(DirectoryPath, "movement.csv");
        public string MetadataPath => Path.Combine(DirectoryPath, "session_metadata.json");
        public string LastError { get; private set; } = "";
        public double Timestamp => clock.Elapsed.TotalSeconds;
        public bool DataComplete => State == HandsLabLogState.Completed;
        public event Action<string> WriteFailed;

        public HandsLabLogger(string rootPath, string inputSource, string runtime, double sampleRateHz = 20, string executionEnvironment = "Unknown", bool hardwareVerified = false, double maxPoseAgeSeconds = .25)
        {
            if(string.IsNullOrWhiteSpace(inputSource) || string.IsNullOrWhiteSpace(runtime))throw new ArgumentException("Explicit input source and runtime are required");
            CheckInputSource(inputSource);
            if(!Finite(sampleRateHz) || sampleRateHz<=0 || sampleRateHz>120)throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if(!Finite(maxPoseAgeSeconds)||maxPoseAgeSeconds<=0)throw new ArgumentOutOfRangeException(nameof(maxPoseAgeSeconds));
            this.inputSource=inputSource;this.runtime=runtime;this.executionEnvironment=executionEnvironment;MaxPoseAgeSeconds=maxPoseAgeSeconds;
            DirectoryPath=Path.Combine(rootPath,RunId);
            Write(() =>
            {
                Directory.CreateDirectory(DirectoryPath);
                eventsWriter=Create(EventsPath);movementWriter=Create(MovementPath);
                movementWriter.WriteLine("timestamp,run_id,phase,seq,frame_id,task_state,handedness,input_source,runtime,execution_environment,source,target,pose_kind,provider,tracking_valid,reason,sample_timestamp,sample_age_seconds,px,py,pz,qx,qy,qz,qw");
                movementWriter.Flush();
                using(var metadata=Create(MetadataPath))metadata.Write(JsonConvert.SerializeObject(new {
                    run_id=RunId,phase="HandsLab",technical_test=true,test_data=true,hardware_verified=hardwareVerified,
                    schema_version=SchemaVersion,content_version=ContentVersion,initial_input_source=inputSource,runtime,execution_environment=executionEnvironment,
                    started_utc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),clock="Stopwatch monotonic seconds shared by events and movement",
                    seq_scope="global across events.jsonl and movement.csv",movement_configured_max_hz=sampleRateHz,
                    movement_rate_policy="caller sampling; use CSV timestamps to measure actual rate",units="metres, quaternion xyzw, seconds",
                    coordinates="Unity world left-handed; provider tracking-space positions/rotations transformed to world using XR Origin camera floor offset (fallback XR Origin); XR Origin separately included",head_direction_is_not_eye_gaze=true,
                    provider_pose_policy="ProviderReported is processed provider pose in Unity world, not raw sensor data; synthetic visual rows stay separate and never fill missing provider values",
                    invalid_pose_policy="null in normalized snapshots and blank CSV coordinates; validity, reason and observation timestamp retained",
                    maximum_pose_age_seconds=MaxPoseAgeSeconds
                },Formatting.Indented));
            });
            if(State==HandsLabLogState.Active)Record("RUN_STARTED","handslab","technical_test","Idle","None",null,null,"System");
        }

        static StreamWriter Create(string path) => new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
        public string NewActionId() => Guid.NewGuid().ToString("N");

        public bool Record(string eventName,string target,string result,string taskState,string handedness,object before=null,object after=null,string source="User",string reason=null,string actionId=null,string inputSourceOverride=null,bool? trackingValid=null)
        {
            if(State!=HandsLabLogState.Active || disposed)return false;
            if(string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(target))throw new ArgumentException("Event and stable target ID are required");
            CheckSource(source);
            string selectedSource=inputSourceOverride??inputSource;CheckInputSource(selectedSource);
            return Write(() =>
            {
                // Copies are materialized at this call, before any caller can mutate a later frame.
                var row=new { timestamp=Timestamp,run_id=RunId,phase="HandsLab",seq=sequence+1,@event=eventName,target,result,task_state=taskState,
                    handedness,input_source=selectedSource,runtime,execution_environment=executionEnvironment,source,tracking_valid=trackingValid,reason=reason??"",action_id=actionId??NewActionId(),
                    schema_version=SchemaVersion,content_version=ContentVersion,technical_test=true,test_data=true,
                    before=before==null?null:JToken.FromObject(before),after=after==null?null:JToken.FromObject(after) };
                eventsWriter.WriteLine(JsonConvert.SerializeObject(row));eventsWriter.Flush();sequence++;
            });
        }

        /// <summary>Copied, normalized pose for before/after event snapshots. Invalid positions and
        /// rotations become null rather than plausible zeros or last-known tracking.</summary>
        public JObject SnapshotPose(PoseSample pose) => Normalize(pose,Timestamp);
        JObject Normalize(PoseSample pose,double now)
        {
            double age=pose==null?double.NaN:now-pose.sampleTimestamp;
            string reason=pose==null?"provider_pose_unavailable":pose.reason??"";
            bool valid=pose!=null && pose.trackingValid;
            if(pose!=null && !pose.trackingValid && reason.Length==0)reason="tracking_invalid";
            if(valid && (string.IsNullOrWhiteSpace(pose.provider) || !Finite(pose.sampleTimestamp))) {valid=false;reason="provider_timestamp_unavailable";}
            if(valid && (!Finite(MaxPoseAgeSeconds) || MaxPoseAgeSeconds<=0 || age<-.001 || age>MaxPoseAgeSeconds)) {valid=false;reason=age<0?"future_sample":"stale_provider_sample";}
            if(valid && (pose.position==null || pose.position.Length!=3 || !pose.position.All(v=>Finite(v)) || pose.rotation==null || pose.rotation.Length!=4
                || !pose.rotation.All(v=>Finite(v)) || Math.Abs(pose.rotation.Sum(v=>v*v)-1f)>.05f)) {valid=false;reason="invalid_pose_values";}
            return JObject.FromObject(new { tracking_valid=valid,provider=pose?.provider??"",reason,
                sample_timestamp=pose!=null && Finite(pose.sampleTimestamp)?(double?)pose.sampleTimestamp:null,
                sample_age_seconds=Finite(age)?(double?)age:null,
                position=valid?(float[])pose.position.Clone():null,rotation=valid?(float[])pose.rotation.Clone():null });
        }

        public bool Sample(HandsLabFrame frame,string source="System")
        {
            if(frame==null)throw new ArgumentNullException(nameof(frame));
            if(State!=HandsLabLogState.Active || disposed)return false;
            CheckSource(source);
            return Write(() =>
            {
                double timestamp=Timestamp;string frameId="frame_"+(++frameNumber).ToString("D8",CultureInfo.InvariantCulture);
                string selectedSource=string.IsNullOrWhiteSpace(frame.inputSource)?inputSource:frame.inputSource;
                CheckInputSource(selectedSource);
                void Pose(PoseSample value,string target,string hand,string kind)
                {
                    var p=Normalize(value,timestamp);var position=p["position"] as JArray;var rotation=p["rotation"] as JArray;
                    string poseSource=string.IsNullOrWhiteSpace(value?.inputSource)?selectedSource:value.inputSource;CheckInputSource(poseSource);
                    object[] row={timestamp,RunId,"HandsLab",sequence+1,frameId,frame.taskState,hand,poseSource,runtime,executionEnvironment,source,target,kind,(string)p["provider"],(bool)p["tracking_valid"],(string)p["reason"],
                        (double?)p["sample_timestamp"],(double?)p["sample_age_seconds"],position?[0],position?[1],position?[2],rotation?[0],rotation?[1],rotation?[2],rotation?[3]};
                    movementWriter.WriteLine(string.Join(",",row.Select(Csv)));sequence++;
                }
                Pose(frame.head,"handslab.head","None","ProviderReported");
                Pose(frame.leftProvider,"handslab.left.provider","Left","ProviderReported");
                Pose(frame.rightProvider,"handslab.right.provider","Right","ProviderReported");
                Pose(frame.leftVisual,"handslab.left.visual","Left","SyntheticVisual");
                Pose(frame.rightVisual,"handslab.right.visual","Right","SyntheticVisual");
                Pose(frame.origin,"handslab.origin","None","WorldReference");
                movementWriter.Flush();
            });
        }

        public bool Complete(string reason="technical_run_finished",string source="System",string inputSourceOverride=null)
        {
            if(State!=HandsLabLogState.Active || disposed)return false;
            if(!Write(()=>{movementWriter.Flush();eventsWriter.Flush();}))return false;
            if(!Record("RUN_COMPLETED","handslab","Completed","Completed","None",null,null,source,reason,inputSourceOverride:inputSourceOverride))return false;
            if(!Write(CloseWriters))return false;
            State=HandsLabLogState.Completed;disposed=true;return true;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static string Csv(object value)
        {
            string s=value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);
            return s.IndexOfAny(new[]{',','"','\n','\r'})<0?s:"\""+s.Replace("\"","\"\"")+"\"";
        }
        static void CheckSource(string source) {if(source!="User"&&source!="System"&&source!="Tester")throw new ArgumentException("source must be User, System or Tester");}
        static void CheckInputSource(string value) {if(value!="Controller"&&value!="HandTracking"&&value!="None"&&value!="SimulatedController")throw new ArgumentException("input_source must be Controller, HandTracking, None or SimulatedController");}
        bool Write(Action action)
        {
            if(State!=HandsLabLogState.Active || disposed)return false;
            try {action();return true;}
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ObjectDisposedException || ex is JsonException)
            {LastError=ex.GetType().Name+": "+ex.Message;State=HandsLabLogState.WriteError;WriteFailed?.Invoke(LastError);return false;}
        }
        void CloseWriters(){eventsWriter?.Dispose();movementWriter?.Dispose();}
        public void Dispose()
        {
            if(disposed)return;
            if(State==HandsLabLogState.Active)
            {
                Record("RUN_CLOSED_INCOMPLETE","handslab","Incomplete","Closed","None",null,null,"System","No explicit successful completion");
                if(Write(CloseWriters))State=HandsLabLogState.Incomplete;
            }
            if(State==HandsLabLogState.WriteError){CloseAfterFailure(eventsWriter);CloseAfterFailure(movementWriter);}
            disposed=true;
        }
        static void CloseAfterFailure(StreamWriter writer)
        {
            try{writer?.Dispose();}
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ObjectDisposedException){}
        }
    }
}
