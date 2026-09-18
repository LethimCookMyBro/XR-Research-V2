#if HANDSLAB_LOGGER_SELF_TEST
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

static class HandsLabLoggerChecks
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static PoseSample Fresh(HandsLabLogger log,string provider="TestProvider") => new PoseSample {trackingValid=true,sampleTimestamp=log.Timestamp,provider=provider,position=new[]{1f,2f,3f},rotation=new[]{0f,0f,0f,1f}};
    static string ReadShared(string path){using var r=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite));return r.ReadToEnd();}
    static string[] Lines(string text)=>text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
    static int Main(string[] args)
    {
        try
        {
            var leftSource=new ControllerSourceAttribution();var rightSource=new ControllerSourceAttribution();
        Check(leftSource.Observe(false,false)=="None","missing controller never implies hardware");
        Check(leftSource.Observe(true,false)=="SimulatedController","positive simulated controller attribution");
        Check(leftSource.Observe(false,false)=="SimulatedController","simulator removal retains source during shutdown");
        Check(rightSource.Observe(false,true)=="Controller"&&leftSource.LastObserved=="SimulatedController","source retention isolated per hand");
        Check(rightSource.Observe(false,false)=="Controller","hardware removal retains hardware source");
        Check(leftSource.Observe(false,true)=="Controller","fresh physical observation permits real source transition");
        Check(leftSource.Observe(true,true)=="SimulatedController","synthetic observation takes priority consistent with provider selection");
        string root=args[0];Directory.CreateDirectory(root);
            string firstDirectory;
            using(var log=new HandsLabLogger(root,"SimulatedController","TestRuntime",20,"Editor"))
            {
                firstDirectory=log.DirectoryPath;
                Check(log.State==HandsLabLogState.Active&&!log.DataComplete,"new technical run starts incomplete");
                var pose=Fresh(log);var before=log.SnapshotPose(pose);string action=log.NewActionId();
                Check(log.Record("MODE_CHANGED","handslab.mode","HandTracking","Active","Both",before,before,"Tester","provider available",action,"HandTracking"),"per-event mode source override");
                pose.position[0]=900;before["position"][0]=700;
                Check(log.Record("SYSTEM_RELEASE","handslab.left","Released","Active","Left",null,null,"System","tracking lost",action,"None",false),"system reason and correlated action ID");
                var invalid=Fresh(log);invalid.trackingValid=false;invalid.reason="provider tracking lost";
                var stale=Fresh(log);stale.sampleTimestamp=log.Timestamp-1;stale.inputSource="Controller";
                var synthetic=Fresh(log,"SyntheticControllerDriven");
                var frame=new HandsLabFrame {head=Fresh(log),leftProvider=invalid,rightProvider=stale,leftVisual=synthetic,rightVisual=null,origin=Fresh(log,"XR Origin transform"),taskState="Active",inputSource="HandTracking"};
                Check(log.Sample(frame,"Tester"),"movement batch on shared clock");
                synthetic.position[0]=800;
                var missing=log.SnapshotPose(null);Check(!(bool)missing["tracking_valid"]&&missing["position"].Type==JTokenType.Null,"missing provider yields null not zero pose");
                var zeroDefault=new PoseSample {trackingValid=true,provider="Test",position=new float[3],rotation=new[]{0f,0f,0f,1f}};
                Check(!(bool)log.SnapshotPose(zeroDefault)["tracking_valid"],"missing provider update timestamp rejected");
                var future=Fresh(log);future.sampleTimestamp=log.Timestamp+1;
                Check((string)log.SnapshotPose(future)["reason"]=="future_sample","future provider timestamps rejected");
                var invalidQ=Fresh(log);invalidQ.rotation=new float[4];
                Check((string)log.SnapshotPose(invalidQ)["reason"]=="invalid_pose_values","invalid quaternion rejected");
                var nan=Fresh(log);nan.position[0]=float.NaN;
                Check(!(bool)log.SnapshotPose(nan)["tracking_valid"],"nonfinite position rejected");
                var events=Lines(ReadShared(log.EventsPath)).Select(JObject.Parse).ToArray();
                Check((float)events.Single(e=>(string)e["event"]=="MODE_CHANGED")["before"]["position"][0]==1,"event snapshots deep copied at call time");
                Check(events.Count(e=>(string)e["action_id"]==action)==2,"action ID correlates mode change and system release");
                Check(!(bool)events.Single(e=>(string)e["event"]=="SYSTEM_RELEASE")["tracking_valid"],"event reports invalid tracking explicitly");
                Check((string)events.Single(e=>(string)e["event"]=="MODE_CHANGED")["input_source"]=="HandTracking"&&
                    (string)events.Single(e=>(string)e["event"]=="SYSTEM_RELEASE")["input_source"]=="None","source changes retained within same run");
                var movement=Lines(ReadShared(log.MovementPath));var header=movement[0].Split(',');
                var rows=movement.Skip(1).Select(line=>line.Split(',')).ToArray();
                int col(string key)=>Array.IndexOf(header,key);
                Check(rows.Length==6&&rows.All(r=>r.Length==header.Length),"six independent pose rows with consistent schema");
                var lost=rows.Single(r=>r[col("target")]=="handslab.left.provider");
                var old=rows.Single(r=>r[col("target")]=="handslab.right.provider");
                var visual=rows.Single(r=>r[col("target")]=="handslab.left.visual");
                Check(lost[col("tracking_valid")]=="False"&&lost.Skip(col("px")).All(string.IsNullOrEmpty),"tracking-lost CSV coordinates blank, no stale-as-valid");
                Check(old[col("reason")]=="stale_provider_sample"&&old.Skip(col("px")).All(string.IsNullOrEmpty),"stale provider CSV coordinates blank");
                Check(old[col("input_source")]=="Controller"&&lost[col("input_source")]=="HandTracking","mixed per-hand mode source retained within a frame");
                Check(visual[col("pose_kind")]=="SyntheticVisual"&&visual[col("px")]=="1","synthetic visual separated and immutable on disk");
                Check(rows.All(r=>r[col("execution_environment")]=="Editor"&&r[col("source")]=="Tester"),"execution environment and tester source distinguished");
                double firstEvent=(double)events.First()["timestamp"],movementTime=double.Parse(rows[0][col("timestamp")],System.Globalization.CultureInfo.InvariantCulture);
                Check(movementTime>=firstEvent&&movementTime<=log.Timestamp,"event and movement use common monotonic clock");
                var allSequence=events.Select(e=>(long)e["seq"]).Concat(rows.Select(r=>long.Parse(r[col("seq")]))).OrderBy(x=>x).ToArray();
                Check(allSequence.SequenceEqual(Enumerable.Range(1,allSequence.Length).Select(i=>(long)i)),"global sequence reconstructs both files");
                var metadata=JObject.Parse(File.ReadAllText(log.MetadataPath));
                Check((string)metadata["phase"]=="HandsLab"&&(bool)metadata["technical_test"]&&!(bool)metadata["hardware_verified"],"metadata is technical test, hardware unverified by default");
                Check(log.Complete(source:"Tester",inputSourceOverride:"None")&&log.DataComplete,"explicit completion after synchronous flush");
                Check(!log.Complete()&&!log.Sample(frame),"duplicate completion and post-close movement rejected");
                Check(Lines(File.ReadAllText(log.EventsPath)).Count(x=>x.Contains("RUN_COMPLETED"))==1,"one completion on reopened disk");
                Console.WriteLine("LOG_DIRECTORY="+log.DirectoryPath);
            }
            using(var second=new HandsLabLogger(root,"None","TestRuntime",20,"Editor"))
            {
                Check(second.DirectoryPath!=firstDirectory,"new run does not overwrite prior files");
                second.Dispose();Check(second.State==HandsLabLogState.Incomplete&&!second.DataComplete,"ordinary disposal does not invent success");
            }
            string blocked=Path.Combine(root,"blocked_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocked,"file blocks directory fixture");
            using(var broken=new HandsLabLogger(blocked,"None","TestRuntime"))
                Check(broken.State==HandsLabLogState.WriteError&&!broken.Complete()&&!broken.DataComplete,"actual filesystem create error blocks completion");
            using(var interrupted=new HandsLabLogger(root,"None","TestRuntime"))
            {
                int errors=0;interrupted.WriteFailed+=_=>{errors++;Check(!interrupted.Record("REENTRANT","handslab","blocked","Error","None"),"write-error callback cannot recursively record");};
                var writer=(StreamWriter)typeof(HandsLabLogger).GetField("movementWriter",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(interrupted);
                writer.BaseStream.Dispose(); // Isolated injected stream interruption, not hardware/disk-full evidence.
                Check(!interrupted.Sample(new HandsLabFrame())&&interrupted.State==HandsLabLogState.WriteError&&errors==1,"mid-run stream interruption reports once");
                Check(!interrupted.Complete()&&!interrupted.DataComplete,"interrupted run cannot report complete");
            }
            Console.WriteLine("HANDSLAB_LOGGER_PASS checks="+checks+"; pure logic/disk only, not XR hardware evidence");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
#endif
