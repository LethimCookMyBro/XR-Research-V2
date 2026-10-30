#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTaskNavigationChecks
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
            var checks=new List<object>();var failures=new List<string>();var errors=new List<string>();
            void Check(bool pass,string name,object observed)
            {
                checks.Add(new{name,pass,observed});if(!pass)failures.Add(name);
            }
            Directory.CreateDirectory(directory);
            Application.LogCallback onLog=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+stack);};
            Application.logMessageReceived+=onLog;
            try
            {
                foreach(var task in new[]{"A","B"})
                foreach(var ending in new[]{"pause","tracking"})
                {
                    object owned=null;
                    string prefix=task+"/"+ending;
                    try
                    {
                        owned=Activator.CreateInstance(ownedType,Instance,null,new object[]{Path.Combine(directory,task+"_"+ending),donor,ParticipantLanguage.Thai},null);
                        var c=Field<ResearchSessionFlowController>(owned,"Controller");
                        var view=Field<ResearchFlowView>(owned,"View");
                        var guards=new List<HandsLabInputGuard>();var gates=new List<GameObject>();var roots=new List<GameObject>();
                        foreach(var side in new[]{Handedness.Left,Handedness.Right})
                        {
                            var owner=new GameObject("NavigationGuard_"+side);owner.transform.SetParent(c.transform,false);
                            var guard=owner.AddComponent<HandsLabInputGuard>();guard.side=side;
                            guard.interactionsRoot=new GameObject("Interactions_"+side);guard.interactionsRoot.transform.SetParent(owner.transform,false);
                            Set(guard,"TrackingValid",true);
                            guards.Add(guard);
                            foreach(var mode in new[]{"Controller","Hand"})
                            {
                                var gate=new GameObject("ResearchLocomotionGate");gate.transform.SetParent(guard.interactionsRoot.transform,false);
                                var root=new GameObject("Locomotion"+mode+"InteractorGroup");root.transform.SetParent(gate.transform,false);
                                gates.Add(gate);roots.Add(root);
                            }
                        }
                        c.guards=guards.ToArray();c.locomotionInputRoots=roots.ToArray();
                        typeof(ResearchSessionFlowController).GetField("locomotionGates",Instance).SetValue(c,gates.ToArray());
                        if(task=="B")Call(owned,"Seed",ResearchFlowState.Task2Ready,5,(bool?)null);
                        void Click(string command)=>Call(owned,"Click",(ResearchUIAction)Call(owned,"Action",command,null),1);
                        void Tick(bool inputAvailable,Vector2 axis)=>c.UpdateReferenceNavigation(inputAvailable,axis);
                        void PresentReader()
                        {
                            Call(owned,"Refresh");Canvas.ForceUpdateCanvases();
                            foreach(var panel in new[]{view.mainPanel,view.referencePanel})
                                if(panel&&panel.gameObject.activeInHierarchy&&panel.RecenterPending)
                                    Call(panel,"TryApplyPendingRecenter",true,true,true,Time.frameCount+1);
                            view.RefreshReference();Canvas.ForceUpdateCanvases();
                            bool shown=(bool)view.GetType().GetProperty("ReferenceSurfaceShown",Instance).GetValue(view);
                            Check(shown,prefix+"/Reader uses accepted visible presentation",new{shown,view.referencePanel.HasAcceptedPose,view.referencePanel.RecenterPending,view.referencePanel.PlacementBlocked});
                            if(!shown)throw new InvalidOperationException("Reader presentation precondition failed.");
                        }
                        bool AllOpen()=>gates.All(g=>g.activeSelf);
                        bool AllClosed()=>gates.All(g=>!g.activeSelf);
                        object State()=>new{c.ReferenceOpen,c.ReferenceMinimized,gates=gates.Select((g,i)=>new{
                            side=guards.SingleOrDefault(owner=>owner.interactionsRoot&&roots[i].transform.IsChildOf(owner.interactionsRoot.transform))?.side.ToString()??"missing",
                            open=g.activeSelf}).ToArray()};
                        Check(roots.Select((root,i)=>guards.Count(owner=>owner.interactionsRoot&&root.transform.IsChildOf(owner.interactionsRoot.transform))==1&&root.transform.parent==gates[i].transform).All(valid=>valid),prefix+"/every gate has one explicit guard owner",State());
                        Click("task_start");Click("task_start_confirm");
                        Check(c.Flow.IsTaskActive&&c.Flow.CurrentTask==task&&AllOpen(),prefix+"/active task enables locomotion",State());
                        Click("reference");
                        PresentReader();
                        var focus=view.referencePanel.GetComponent<ResearchSlateFocus>();
                        if(!focus)throw new InvalidOperationException("Authored Reader focus required.");
                        var pointer=new PointerEventData(EventSystem.current){pointerId=73421};
                        Tick(true,Vector2.zero);
                        Check(c.ReferenceOpen&&!focus.Focused&&AllOpen(),prefix+"/open unfocused Reader leaves locomotion available",State());
                        focus.OnPointerEnter(pointer);Tick(true,Vector2.zero);
                        Check(gates.Skip(2).All(g=>!g.activeSelf),prefix+"/Reader focus owns right navigation",State());
                        Check(gates.Take(2).All(g=>g.activeSelf),prefix+"/Reader right scroll does not lock left locomotion",State());
                        focus.OnPointerDown(pointer);focus.OnPointerExit(pointer);Tick(true,Vector2.zero);
                        Check(focus.PointerHeld&&gates.Skip(2).All(g=>!g.activeSelf),prefix+"/held Reader retains navigation after pointer exit",State());
                        focus.OnPointerUp(pointer);Tick(true,Vector2.up);
                        Check(gates.Skip(2).All(g=>!g.activeSelf),prefix+"/held axis retains ownership after focus exit",State());
                        Tick(false,Vector2.zero);
                        Check(gates.Skip(2).All(g=>!g.activeSelf),prefix+"/untracked zero is not neutral release",State());
                        Tick(true,Vector2.zero);
                        Check(c.ReferenceOpen&&AllOpen(),prefix+"/tracked neutral returns locomotion while Reader stays open",State());
                        focus.OnPointerEnter(pointer);Tick(true,Vector2.zero);
                        Click("reference_minimize");focus.OnPointerExit(pointer);Tick(true,Vector2.up);
                        Check(c.ReferenceMinimized&&gates.Skip(2).All(g=>!g.activeSelf),prefix+"/minimize waits for neutral",State());
                        Tick(true,Vector2.zero);
                        Check(AllOpen(),prefix+"/minimized neutral restores locomotion",State());
                        Click("reference_restore");PresentReader();Tick(true,Vector2.zero);
                        Check(!c.ReferenceMinimized&&AllOpen(),prefix+"/restored unfocused Reader leaves locomotion available",State());
                        focus.OnPointerEnter(pointer);Tick(true,Vector2.zero);
                        Click("reference_close");focus.OnPointerExit(pointer);Tick(true,Vector2.up);
                        Check(!c.ReferenceOpen&&gates.Skip(2).All(g=>!g.activeSelf),prefix+"/close waits for held stick",State());
                        Tick(true,Vector2.zero);
                        Check(AllOpen(),prefix+"/closed neutral restores locomotion",State());
                        if(ending=="pause")c.Command("pause","None");
                        else
                        {
                            Set(guards[1],"TrackingValid",false);
                            Call(c,"TutorialInputChanged",Handedness.Right,"tracking_lost_fixture");
                        }
                        Tick(true,Vector2.zero);
                        Check(c.Flow.Paused&&AllClosed(),prefix+"/safety pause still gates every locomotion group",new{c.Flow.Paused,state=State()});
                    }
                    catch(Exception ex){Check(false,prefix+"/exception",ex.ToString());}
                    finally{(owned as IDisposable)?.Dispose();}
                }
            }
            finally{Application.logMessageReceived-=onLog;EventSystem.current=priorEvents;}
            Check(errors.Count==0,"no Unity errors",errors);
            Check(before.SequenceEqual(Scenes())&&previews==EditorSceneManager.previewSceneCount,"original scenes unchanged",Scenes());
            string report=Path.Combine(directory,"task-navigation.json");
            string status=failures.Count==0?"PASS":"FAIL";
            File.WriteAllText(report,JsonConvert.SerializeObject(new{status,scope="Stopped Editor owned TEST flow, real Reader commands and focus handlers; explicit synthetic axis/tracking and later-frame head-presence placement inputs with no native input provider; locomotion parent gates, not SDK teleport or Quest proof",checks,failures,errors},Formatting.Indented));
            return "TASK_NAVIGATION_"+status+" checks="+checks.Count+" failures="+failures.Count+" report="+report;
        }
    }
}
#endif
