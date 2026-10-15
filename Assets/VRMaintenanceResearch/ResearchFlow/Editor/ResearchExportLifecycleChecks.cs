#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchExportLifecycleChecks
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type OwnedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR subsystem inactive required.");
            string directory = Path.GetFullPath(evidenceDirectory);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Directory.Exists(directory) || (directory + Path.DirectorySeparatorChar).StartsWith(project, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Fresh external evidence directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored three-font donor required.");
            string beforeScenes = Scenes(); var active = SceneManager.GetActiveScene(); var priorEvents = EventSystem.current;
            string caller = Caller(donor.controller); string callerFiles = CallerFiles(donor.controller);
            Directory.CreateDirectory(directory);
            var rows = new List<object>(); var failures = new List<string>(); var bundles = new List<object>();
            Action<bool,string,object> check = (ok,name,data) => { rows.Add(new { pass = ok, name, data }); if (!ok) failures.Add(name); };
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                foreach (string stage in new[] { "task_deadline", "seeded_final_checkpoint", "withdrawal_current_amend", "saved_amend" })
                {
                    string label = language + "/" + stage;
                    object owned = null;
                    try
                    {
                        owned = Activator.CreateInstance(OwnedType, Instance, null, new object[] { Path.Combine(directory,label.Replace('/','_')), donor, language }, null);
                        var c = Field<ResearchSessionFlowController>(owned,"Controller");
                        Field<HandsLabLogger>(owned,"Writer").Dispose();
                        var writer = new HandsLabLogger(Path.Combine(c.Registry.DirectoryPath,"Logs"),"None","Stopped Editor lifecycle fixture",executionEnvironment:"Editor",technicalTest:true,testData:true,runId:c.Record.run_id);
                        OwnedType.GetField("Writer",Instance).SetValue(owned,writer); Set(c,"Logger",writer);
                        string identity = c.Record.participant_id+"/"+c.Record.session_id+"/"+c.Record.run_id;
                        int credits = c.Flow.TutorialCompleted;
                        if (stage == "seeded_final_checkpoint")
                        {
                            Call(owned,"Seed",ResearchFlowState.PostTask2Checkpoint,5,null);
                            c.ResearcherResume();
                            Require(string.IsNullOrEmpty(c.Error),c.Error);
                            check(c.Record.session_state == RegistryState.Completed && writer.DataComplete,label+"/actual completion lifecycle",new { c.Record.session_state, writer.State });
                            CheckBundle(c,c.Record,"session_completed","finalized",label,check,bundles);
                        }
                        else
                        {
                            Click(owned,"task_start"); Click(owned,"task_start_confirm");
                            Require(c.Flow.State == ResearchFlowState.Task1Active && string.IsNullOrEmpty(c.Error),"Task start precondition: "+c.Error);
                            if (stage == "task_deadline")
                            {
                                typeof(HandsLabLogger).GetField("timestampOffset",Instance).SetValue(writer,ResearchTaskTiming.TaskLimitSeconds+1d);
                                Call(c,"AdvanceTaskTiming");
                                Require(string.IsNullOrEmpty(c.Error),c.Error);
                                check(c.TaskEndReason == "time_limit" && c.Flow.State == ResearchFlowState.PostTask1Checkpoint,label+"/actual task deadline transition",new { c.TaskEndReason, c.Flow.State });
                                CheckBundle(c,c.Record,"task_A_time_limit","task_finalized",label,check,bundles);
                            }
                            else
                            {
                                c.ResearcherAbort(false,"withdrawal");
                                Require(c.Record.session_state == RegistryState.Aborted,"Withdrawal did not persist terminal record.");
                                check(c.Record.termination_reason == "withdrawal" && c.TaskEndReason == "withdrawal",label+"/actual withdrawal",new { c.Record.termination_reason, c.TaskEndReason });
                                CheckBundle(c,c.Record,"session_aborted","partial",label,check,bundles);
                                string participant = c.Record.participant_id;
                                if (stage == "saved_amend") { Set(c,"Record",null); Set(c,"Logger",null); }
                                string error = c.AmendTermination(participant,"safety_stop","StoppedEditorLifecycleFixture");
                                Require(error == null,error);
                                var amended = c.Registry.List(ParticipantGroup.Test).Single(r=>r.participant_id==participant);
                                check(amended.termination_reason == "withdrawal" && amended.termination_amendments.Count == 1 && amended.termination_amendments[0].to_reason == "safety_stop",label+"/original ending and amendment history",amended.termination_amendments);
                                CheckBundle(c,amended,"termination_amended","partial",label,check,bundles);
                                int jobsBefore=Directory.GetFiles(Path.Combine(c.Registry.DirectoryPath,"ExportQueue"),"*.json").Length;
                                Require(c.AmendTermination(participant,"safety_stop","StoppedEditorLifecycleFixture") == null,"Repeated amendment failed");
                                int jobsAfter=Directory.GetFiles(Path.Combine(c.Registry.DirectoryPath,"ExportQueue"),"*.json").Length;
                                check(jobsAfter==jobsBefore && c.Registry.List(ParticipantGroup.Test).Single(r=>r.participant_id==participant).termination_amendments.Count==1,
                                    label+"/same-reason amendment adds no history or export",new { jobsBefore, jobsAfter });
                                if(stage == "saved_amend") Set(c,"Record",amended);
                            }
                        }
                        check(identity == c.Record.participant_id+"/"+c.Record.session_id+"/"+c.Record.run_id && c.Flow.TutorialCompleted == credits,label+"/same owned identity and credits",new { identity, c.Flow.TutorialCompleted });
                    }
                    catch(Exception ex) { check(false,label+"/exception",ex.ToString()); }
                    finally { (owned as IDisposable)?.Dispose(); }
                }
            }
            finally
            {
                EventSystem.current = priorEvents;
                if(active.IsValid() && SceneManager.GetActiveScene()!=active) SceneManager.SetActiveScene(active);
            }
            check(beforeScenes == Scenes(),"caller scene set and dirty flags preserved",new { beforeScenes, after = Scenes() });
            check(caller == Caller(donor.controller) && callerFiles == CallerFiles(donor.controller),"caller identity and registry/log bytes preserved",null);
            string report = Path.Combine(directory,"export-lifecycle.json");
            File.WriteAllText(report,JsonConvert.SerializeObject(new { status=failures.Count==0?"PASS_SCOPED_NATIVE_LIFECYCLE":"FAIL",rows,failures,bundles,
                scope="Actual stopped Editor controller/registry/logger/queue callbacks on owned TEST fixtures. Starts from seeded five tutorial credits; final completion starts at seeded PostTask2Checkpoint. Deadline advances only owned logger offset by 421s. No full research flow, physical tasks, approval, wearer or PC delivery claimed. Raw files intentionally contain fixture evidence and may fail full research completeness QC. No ExportSession/operator export invoked; no PC ACK fabricated." },Formatting.Indented));
            return report;
        }
        static void CheckBundle(ResearchSessionFlowController c,ParticipantRecord record,string reason,string lifecycle,string label,Action<bool,string,object> check,List<object> bundles)
        {
            string queue=Path.Combine(c.Registry.DirectoryPath,"ExportQueue");
            var jobs=Directory.GetFiles(queue,"*.json").Select(p=>JObject.Parse(File.ReadAllText(p))).Where(j=>(string)j["participant_code"]==record.participant_id && (string)j["session_id"]==record.session_id && (string)j["run_id"]==record.run_id && (string)j["reason"]==reason).ToArray();
            Require(jobs.Length==1,"Expected exactly one automatic queue job for "+reason);
            string path=(string)jobs[0]["export_path"];
            var m=JObject.Parse(File.ReadAllText(Path.Combine(path,"manifest.json")));
            var marker=JObject.Parse(File.ReadAllText(Path.Combine(path,"COMPLETE.json")));
            bool hashes=(string)marker["manifest_sha256"]==Hash(Path.Combine(path,"manifest.json"));
            foreach(var entry in (JObject)m["files"])
            {
                string file=Path.Combine(path,entry.Key.Replace('/',Path.DirectorySeparatorChar));
                hashes &= new FileInfo(file).Length==(long)entry.Value["bytes"] && Hash(file)==(string)entry.Value["sha256"];
            }
            check(hashes && (string)m["lifecycle"]==lifecycle && (string)m["identity"]["run_id"]==record.run_id,label+"/"+reason+" immutable source hashes and lifecycle",new { path, lifecycle });
            string status=(string)typeof(ResearchExportQueue).GetMethod("DeliveryStatus").Invoke(null,new object[]{c.Registry.DirectoryPath,record,null});
            check(status=="queued" && !Directory.Exists(Path.Combine(c.Registry.DirectoryPath,"ExportReceipts")),label+"/"+reason+" queued without fabricated ACK",status);
            bundles.Add(new { label, registry_root=c.Registry.DirectoryPath, reason, lifecycle, export_path=path, manifest_sha256=Hash(Path.Combine(path,"manifest.json")), participant=record.participant_id, session=record.session_id, run=record.run_id });
        }
        static void Click(object owned,string command){var a=Call(owned,"Action",command,null);Call(owned,"Click",a,1);}
        static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Instance).Invoke(target,args);
        static T Field<T>(object target,string field)=>(T)target.GetType().GetField(field,Instance).GetValue(target);
        static void Set(object target,string property,object value)=>target.GetType().GetProperty(property,Instance).GetSetMethod(true).Invoke(target,new[]{value});
        static void Require(bool condition,string error){if(!condition)throw new InvalidOperationException(error);}
        static string Hash(string path){using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b=>b.ToString("x2")));}
        static string Scenes()=>string.Join("|",Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.handle+":"+s.path+":"+s.isDirty;}));
        static string Caller(ResearchSessionFlowController c)=>!c?"none":JsonConvert.SerializeObject(new { c.Record, state=c.Flow?.State, c.Error, logger=c.Logger?.RunId });
        static string CallerFiles(ResearchSessionFlowController c)=>!c?"none":string.Join("|",new[]{c.Registry?.RegistryPath,c.Registry?.ProtocolPath,c.Logger?.EventsPath,c.Logger?.MovementPath,c.Logger?.MetadataPath}.Where(File.Exists).Select(p=>p+":"+Hash(p)));
    }
}
#endif
