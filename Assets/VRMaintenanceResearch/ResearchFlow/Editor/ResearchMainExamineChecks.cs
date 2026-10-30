#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchMainExamineChecks
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Instance).GetValue(value);
        static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Instance).Invoke(value,args);
        static void Set(object value,string name,object state)=>value.GetType().GetProperty(name,Instance).GetSetMethod(true).Invoke(value,new[]{state});
        static string[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>
        {
            var scene=SceneManager.GetSceneAt(i);
            return scene.handle+":"+scene.path+":"+scene.isDirty;
        }).ToArray();

        public static string Run(string evidenceDirectory)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor without XR required.");
            string directory=Path.GetFullPath(evidenceDirectory);
            if(!Path.IsPathRooted(evidenceDirectory)||Directory.Exists(directory)||File.Exists(directory))
                throw new IOException("Fresh absolute evidence directory required.");
            var donor=Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v=>v.thaiFont&&v.japaneseFont&&v.englishFont);
            if(!donor)throw new InvalidOperationException("Authored font donor required.");
            var ownedType=typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned",BindingFlags.NonPublic);
            var before=Scenes(); int previews=EditorSceneManager.previewSceneCount;
            var priorEvents=EventSystem.current;
            var cursors=Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c=>c,c=>c.HoverColor);
            var checks=new List<object>();var failures=new List<string>();var errors=new List<string>();
            var captures=new List<object>();var layout=new List<object>();
            void Check(bool pass,string name,object observed)
            {
                checks.Add(new{name,pass,observed});if(!pass)failures.Add(name);
            }
            Directory.CreateDirectory(directory);
            Application.LogCallback onLog=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);};
            Application.logMessageReceived+=onLog;
            try
            {
                foreach(var language in new[]{ParticipantLanguage.Thai,ParticipantLanguage.Japanese,ParticipantLanguage.English})
                foreach(var task in new[]{"A","B","training"})
                {
                    object owned=null;
                    var targets=new List<ResearchExamineTarget>();
                    string prefix=language+"/"+task;
                    try
                    {
                        owned=Activator.CreateInstance(ownedType,Instance,null,new object[]{Path.Combine(directory,language+"_"+task),donor,language},null);
                        var controller=Field<ResearchSessionFlowController>(owned,"Controller");
                        var view=Field<ResearchFlowView>(owned,"View");
                        var writer=Field<HandsLabLogger>(owned,"Writer");
                        void Refresh()=>Call(owned,"Refresh");
                        ResearchUIAction Action(string command)=>(ResearchUIAction)Call(owned,"Action",command,null);
                        void Click(string command)=>Call(owned,"Click",Action(command),1);
                        JObject[] Events()
                        {
                            using var file = new FileStream(writer.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var reader = new StreamReader(file);
                            return reader.ReadToEnd().Split('\n').Where(s=>!string.IsNullOrWhiteSpace(s)).Select(JObject.Parse).ToArray();
                        }
                        JObject[] Examines()=>Events().Where(e=>(string)e["event"]=="EXAMINE").ToArray();
                        var fan=UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
                        var observation=fan ? fan.Capture() : null;
                        string Expected(string id)=>id.StartsWith("B.",StringComparison.Ordinal) ? ResearchS1FanObservations.Get(observation,id,controller.Language) : ResearchStrings.Get("examine.info."+id,controller.Language);
                        void Capture(string name)
                        {
                            Call(owned,"Capture",name,view.mainPanel,captures,layout);
                            if (name.StartsWith("observation_",StringComparison.Ordinal))
                            {
                                var panel=(RectTransform)view.mainPanel.transform;
                                foreach(var button in panel.GetComponentsInChildren<Button>(true).Where(b=>b.gameObject.activeInHierarchy))
                                {
                                    var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                                    var bounds=panel.rect;bounds.xMin-=1;bounds.yMin-=1;bounds.xMax+=1;bounds.yMax+=1;
                                    Check(corners.All(p=>bounds.Contains(panel.InverseTransformPoint(p))),prefix+"/"+name+"/button inside panel/"+button.name,
                                        corners.Select(p=>panel.InverseTransformPoint(p).ToString()).ToArray());
                                }
                            }
                            foreach(var text in view.mainPanel.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy))
                            {
                                text.ForceMeshUpdate(true,true);
                                Check(!text.isTextOverflowing&&!text.isTextTruncated,prefix+"/"+name+"/text fits/"+text.name,
                                    new{text.text,text.isTextOverflowing,text.isTextTruncated,text.fontSize,text.preferredHeight,height=text.rectTransform.rect.height});
                            }
                        }
                        ResearchExamineTarget Target(string id,ResearchTaskPort port,ResearchTrainingStation training=null)
                        {
                            var go=new GameObject("OwnedExamine_"+id,typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(Image),typeof(Button));
                            go.SetActive(false);go.transform.SetParent(view.transform,false);
                            var textGo=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
                            textGo.transform.SetParent(go.transform,false);
                            var target=go.AddComponent<ResearchExamineTarget>();
                            target.controller=controller;target.task=port;target.training=training;target.stableTarget=id;
                            target.stationCanvas=go.GetComponent<Canvas>();target.stationGroup=go.GetComponent<CanvasGroup>();
                            target.stationCanvas.renderMode=RenderMode.WorldSpace;
                            var interaction=new GameObject("LegacyExamineInteraction");interaction.transform.SetParent(go.transform,false);
                            target.interactionRoots=new[]{interaction};
                            go.SetActive(true);Call(target,"OnEnable");targets.Add(target);return target;
                        }
                        void HiddenTargets()
                        {
                            foreach(var target in targets)
                            {
                                typeof(ResearchExamineTarget).GetMethod("LateUpdate",Instance)?.Invoke(target,null);
                                Check(!target.ChipVisible&&!target.stationCanvas.enabled&&!target.stationGroup.blocksRaycasts&&
                                    !target.interactionRoots.Any(g=>g.activeSelf),prefix+"/no object-attached surface/"+target.stableTarget,
                                    new{target.ChipVisible,target.stationCanvas.enabled,target.stationGroup.blocksRaycasts,roots=target.interactionRoots.Select(g=>g.activeSelf).ToArray()});
                            }
                        }
                        bool Direct(string id)
                        {
                            var method=typeof(ResearchExamineTarget).GetMethod("TryExamineFromMain",BindingFlags.Static|BindingFlags.Public);
                            if(method==null)return false;
                            object[] args={controller,id,"None",null};return (bool)method.Invoke(null,args);
                        }
                        if(task=="training")
                        {
                            var stationGo=new GameObject("OwnedTrainerObservation");stationGo.SetActive(false);stationGo.transform.SetParent(controller.transform,false);
                            var station=stationGo.AddComponent<ResearchTrainingStation>();station.enabled=false;station.controller=controller;
                            controller.trainingStation=station;
                            Target("training.module",null,station);
                            foreach(var guard in controller.guards)
                            {
                                Set(guard,"TrackingValid",true);Set(guard,"InteractionsAllowed",true);Set(guard,"WaitingForNeutral",false);
                            }
                            Call(owned,"Seed",ResearchFlowState.TutorialReferenceInspect,4,(bool?)null);
                            Capture("tutorial_examine_prompt");controller.TutorialPromptShown();
                            var handleGo=new GameObject("OwnedTrainingStatusHandle");handleGo.SetActive(false);handleGo.transform.SetParent(station.transform,false);
                            station.turnHandle=handleGo.AddComponent<ResearchTrainingTurntable>();station.turnHandle.enabled=false;
                            Call(station,"ShowStatus","rotate_access");
                            Capture("tutorial_status_on_main");
                            string status=ResearchStrings.Get("training.status.turn.grab",controller.Language);
                            Check(view.mainPanel.GetComponentsInChildren<TMP_Text>(true).Any(t=>t.gameObject.activeInHierarchy&&t.text==status),prefix+"/physical status appears on Main",status);
                            Check(!station.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="TrainingPartStatus"),prefix+"/no part status billboard",true);
                            Call(station,"ShowStatus","examine");
                            Check(controller.TutorialFact=="examine"&&controller.TutorialArmedFact=="examine",prefix+"/genuine displayed instruction armed",
                                new{controller.TutorialFact,controller.TutorialArmedFact,controller.TutorialInstructionEpoch});
                            int count=Examines().Length;
                            Click("examine_open");Refresh();
                            var rows=Examines();
                            Check(rows.Length==count+1&&(string)rows.Last()["target"]=="training.module",prefix+"/one explicit tutorial EXAMINE",rows);
                            Check(controller.TutorialFact!="examine",prefix+"/tutorial examine credit advances once",controller.TutorialFact);
                            int completed=Events().Count(e=>(string)e["event"]=="TUTORIAL_ACTION_COMMITTED"&&(string)e["target"]=="training.examine");
                            int accepted=Events().Count(e=>(string)e["event"]=="TUTORIAL_ACTION_ACCEPTED"&&(string)e["target"]=="training.examine");
                            Check(completed==1&&accepted==1,prefix+"/one tutorial commit and one accepted credit",new{completed,accepted});
                            HiddenTargets();
                            continue;
                        }
                        var port=controller.taskPorts.Single(p=>p.taskId==task);
                        port.Behavior += (name,target,before,after) =>
                        {
                            if(controller.Flow?.CurrentTask==port.taskId) Call(controller,"TaskBehavior",name,target,before,after);
                        };
                        port.BindExamineInfo((id,lang)=>task=="B" ? ResearchS1FanObservations.Get(observation,id,lang) : ResearchStrings.Get("examine.info."+id,lang));
                        string id=task+".module";
                        Target(id,port);
                        foreach(string reserved in new[]{task+".mount",task+".connection"})
                        {
                            var target=Target(reserved,port);
                            Call(target,"OnDisable");target.enabled=false;
                        }
                        var other=controller.taskPorts.Single(p=>p.taskId!=task);
                        Target(other.taskId+".module",other);
                        Call(owned,"Seed",task=="A"?ResearchFlowState.Task1Ready:ResearchFlowState.Task2Ready,5,(bool?)null);
                        Click("task_start");Click("task_start_confirm");
                        Check(controller.Flow.IsTaskActive&&controller.Flow.CurrentTask==task&&controller.ReaderAuthorized,prefix+"/normal TEST task active",controller.Flow.State.ToString());
                        Capture("task_card");HiddenTargets();
                        for(int attempt=0;attempt<2;attempt++)
                        {
                            if(task=="B"&&attempt==1) observation.frontGuardPlacement=S1Placement.NotInstalled;
                            int count=Examines().Length;
                            Click("examine_open");Refresh();Capture("observation_"+attempt);
                            var rows=Examines();
                            Check(rows.Length==count+1&&(string)rows.Last()["target"]==id,prefix+"/one EXAMINE on direct Main click/"+attempt,rows.Skip(count).ToArray());
                            string expected=Expected(id);
                            var displayed=view.mainPanel.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy).Select(t=>t.text).ToArray();
                            Check(!string.IsNullOrEmpty(expected)&&!expected.StartsWith("examine.info.")&&displayed.Any(s=>s.Contains(expected)),prefix+"/neutral authored observation shown/"+id,new{expected,displayed});
                            Refresh();Refresh();
                            Check(Examines().Length==count+1,prefix+"/refresh does not repeat EXAMINE/"+attempt,Examines().Length);
                            HiddenTargets();
                            Click("examine_close");Refresh();
                            Check(Examines().Length==count+1,prefix+"/Back closes without EXAMINE/"+attempt,Examines().Length);
                        }
                        int beforeRejected=Examines().Length;
                        Check(!Direct(other.taskId+".module")&&!Direct("missing.target")&&!Direct(task+".mount")&&!Direct(task+".connection"),prefix+"/reject wrong task, unknown and reserved targets",controller.Flow.CurrentTask);
                        var acquired=Action("examine_open");
                        Call(owned,"OrphanClick",acquired);
                        Check(Examines().Length==beforeRejected,prefix+"/orphan click is not EXAMINE",Examines().Length);
                        Call(owned,"Down",acquired,1,PointerEventData.InputButton.Left);
                        Set(controller,"Error","owned stale context probe");
                        Call(owned,"UpClick",acquired,1);Set(controller,"Error","");Refresh();
                        Check(Examines().Length==beforeRejected,prefix+"/changed context rejects acquired EXAMINE",Examines().Length);
                        acquired=Action("examine_open");Call(owned,"Down",acquired,1,PointerEventData.InputButton.Left);
                        controller.Command("pause","None");
                        Call(owned,"UpClick",acquired,1);Refresh();
                        Check(controller.Flow.Paused&&!Direct(id)&&!view.LocalCommand("examine_open","None"),prefix+"/pause blocks observation actions",controller.Flow.Paused);
                        Check(Examines().Length==beforeRejected,prefix+"/rejected actions never emit EXAMINE",Examines().Length);
                        HiddenTargets();Capture("paused");
                        Check(Field<List<string>>(owned,"TextOverflows").Count==0,prefix+"/all captures have no overflow",Field<List<string>>(owned,"TextOverflows").ToArray());
                    }
                    catch(Exception error)
                    {
                        Check(false,prefix+"/execution",(error is TargetInvocationException wrapped&&wrapped.InnerException!=null?wrapped.InnerException:error).ToString());
                    }
                    finally
                    {
                        foreach(var target in targets)if(target)Call(target,"OnDisable");
                        (owned as IDisposable)?.Dispose();
                    }
                }
            }
            finally
            {
                Application.logMessageReceived-=onLog;
                foreach(var cursor in cursors)if(cursor.Key)cursor.Key.HoverColor=cursor.Value;
            }
            Check(errors.Count==0,"no Unity errors",errors.ToArray());
            Check(before.SequenceEqual(Scenes())&&previews==EditorSceneManager.previewSceneCount&&priorEvents==EventSystem.current,"caller scenes and EventSystem preserved",new{before,after=Scenes()});
            string report=Path.Combine(directory,"main-examine.json");
            File.WriteAllText(report,JsonConvert.SerializeObject(new{status=failures.Count==0?"PASS":"FAIL",scope="Owned TEST A/B/training x TH/JA/EN, real UI pointer callbacks, neutral fixture observations, stopped Editor; not Quest or physical geometry proof",checks,failures,errors,captures,layout},Formatting.Indented));
            return "MAIN_EXAMINE_"+(failures.Count==0?"PASS":"FAIL")+" checks="+checks.Count+" failures="+failures.Count+" report="+report;
        }
    }
}
#endif
