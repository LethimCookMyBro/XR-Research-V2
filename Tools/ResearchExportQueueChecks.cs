using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.ResearchFlow;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

static class ResearchExportQueueChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static byte[] ReadShared(string path){using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}
    public static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        var record=new ParticipantRecord{participant_id="DEV900",created_at="2026-10-02T00:00:00Z",group=ParticipantGroup.Dev,session_id="fixture_session_runtime",run_id="fixture_run_runtime",test_data=true};
        using(var logger=new HandsLabLogger(Path.Combine(root,"Logs"),"SimulatedController","Fixture",phase:"ResearchSession",taskId:"session",runId:record.run_id))
        {
            logger.Record("TASK_STARTED","task.A","Started",new{components=new{one="original"}},"None",phaseOverride:"Experiment",taskIdOverride:"A");
            string first=ResearchExportQueue.Snapshot(root,logger,record,"task_A_complete","task_finalized",JObject.FromObject(new{fixture=true,simulated_presence=false,hardware_verified=false}));
            string repeated=ResearchExportQueue.Snapshot(root,logger,record,"task_A_complete","task_finalized",JObject.FromObject(new{fixture=true,simulated_presence=false,hardware_verified=false}));
            Check(first==repeated,"Repeated identical snapshot created duplicate");
            Check(ReadShared(logger.EventsPath).SequenceEqual(File.ReadAllBytes(Path.Combine(first,"raw/events.jsonl"))),"Raw events changed");
            Check(ReadShared(logger.MovementPath).SequenceEqual(File.ReadAllBytes(Path.Combine(first,"raw/movement.csv"))),"Raw movement changed");
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(first,"manifest.json")));
            Check((string)manifest["lifecycle"]=="task_finalized","Task-finalized snapshot mislabeled");
            Check((string)manifest["data_completeness"]=="pending_host_QC","Header-only output marked complete");
            Check(File.Exists(Path.Combine(first,"COMPLETE.json")),"Missing completion marker");
            string queue=Directory.GetFiles(Path.Combine(root,"ExportQueue"),"*.json").Single();
            Check((string)JObject.Parse(File.ReadAllText(queue))["status"]=="queued","Not visibly queued");
            bool rejected=false;record.run_id="wrong_run";
            try{ResearchExportQueue.Snapshot(root,logger,record,"wrong_identity","partial");}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Wrong run accepted");record.run_id=logger.RunId;
            rejected=false;try{ResearchExportQueue.Snapshot(root,logger,record,"premature","finalized");}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Active logger labeled finalized");
            Check(logger.Complete(),"Logger failed completion");
            string final=ResearchExportQueue.Snapshot(root,logger,record,"session_complete","finalized");
            Check(final!=first,"Finalized snapshot reused partial bytes");
            Check(Directory.GetFiles(Path.Combine(root,"ExportQueue"),"*.json").Length==2,"Queue not immutable/deduplicated");
            // Missing source bytes must retain staging diagnostics and never publish a COMPLETE export.
            File.Move(logger.MovementPath,logger.MovementPath+".preserved");
            rejected=false;try{ResearchExportQueue.Snapshot(root,logger,record,"missing_source","finalized");}catch(IOException){rejected=true;}
            Check(rejected,"Missing source accepted");
            using(var large=new HandsLabLogger(Path.Combine(root,"Logs"),"SimulatedController","Fixture",phase:"ResearchSession",taskId:"session",runId:"fixture_large"))
            {
                Check(large.Complete(),"Large fixture logger closure");record.run_id=large.RunId;
                using(var data=new FileStream(large.MovementPath,FileMode.Append,FileAccess.Write)){var chunk=new byte[65536];for(int i=0;i<256;i++)data.Write(chunk,0,chunk.Length);}
                long before=GC.GetAllocatedBytesForCurrentThread();
                string copied=ResearchExportQueue.Snapshot(root,large,record,"large_copy_memory","finalized");
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Check(allocated<2*1024*1024,"Snapshot allocated entire large raw file: "+allocated);
                Check(new FileInfo(Path.Combine(copied,"raw/movement.csv")).Length==new FileInfo(large.MovementPath).Length,"Large raw copy length mismatch");
                Console.WriteLine("Large-copy allocation expected <2097152 bytes; actual="+allocated+"; synthetic malformed raw fixture, not movement evidence.");
            }
            Check(Directory.GetFiles(Path.Combine(root,"ExportQueue"),"*.json").Length==3,"Large fixture queue count differs");
            Console.WriteLine(new JObject{["fixture"]="synthetic runtime writer",["expected_queue_jobs"]=3,["actual_queue_jobs"]=Directory.GetFiles(Path.Combine(root,"ExportQueue"),"*.json").Length,["partial_bundle"]=first,["final_bundle"]=final,["wrong_identity_rejected"]=true,["premature_finalization_rejected"]=true,["raw_preserved"]=true}.ToString());
        }
        return 0;
    }
}
