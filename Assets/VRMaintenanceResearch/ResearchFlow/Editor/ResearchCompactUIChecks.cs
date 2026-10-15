#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchCompactUIChecks
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static string Run(string freshDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stop Play and XR before the isolated compact UI fixture.");
            string dir=Path.GetFullPath(freshDirectory);
            if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException("Fresh evidence directory required: "+dir);
            var donor=Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v=>v.thaiFont&&v.japaneseFont&&v.englishFont);
            if(!donor) throw new InvalidOperationException("Load the authored ResearchFlow view/fonts first.");
            var before=Scenes(); var active=SceneManager.GetActiveScene();
            Directory.CreateDirectory(dir);
            var checks=new List<object>();var stages=new List<object>();var failures=new List<string>();
            Action<bool,string> check=(ok,name)=>{checks.Add(new{name,pass=ok});if(!ok)failures.Add(name);};
            var type=typeof(ResearchVisualClarityChecks).GetNestedType("Fixture",BindingFlags.NonPublic);
            foreach(var lang in new[]{ParticipantLanguage.Thai,ParticipantLanguage.Japanese})
            {
                IDisposable owner=null;
                try
                {
                    owner=(IDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{dir,donor,lang},null);
                    var view=(ResearchFlowView)type.GetField("View",Fields).GetValue(owner);
                    var controller=(ResearchSessionFlowController)type.GetField("Controller",Fields).GetValue(owner);
                    Action<string> stage=name=>
                    {
                        string id=lang+"_"+name;
                        var measurement=JObject.Parse(ResearchCompactUIMeasure.Measure(view,Path.Combine(dir,id+".json")));
                        var capture=type.GetMethod("Capture",Fields).Invoke(owner,new object[]{name,view.mainPanel});
                        stages.Add(new{id,measurement,capture});
                        check((bool)measurement["pass"],id+" geometry");
                    };
                    var left=controller.gameObject.AddComponent<HandsLabInputGuard>();left.side=Oculus.Interaction.Input.Handedness.Left;
                    var right=controller.gameObject.AddComponent<HandsLabInputGuard>();right.side=Oculus.Interaction.Input.Handedness.Right;
                    Set(left,"TrackingValid",true);Set(right,"TrackingValid",true);controller.guards=new[]{left,right};
                    Set(controller,"Flow",null);Set(controller,"TechnicalSelfSetup",true);
                    view.Refresh();
                    check(controller.Record==null&&controller.TechnicalReady,lang+" owned technical readiness, no study record");
                    check(!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" ready collapsed");stage("setup_ready");
                    check(view.LocalCommand("technical_details","SimulatedController"),lang+" details direct route accepted");
                    check(Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" details expanded");stage("setup_details");
                    check(view.LocalCommand("technical_details","SimulatedController"),lang+" details collapse route accepted");
                    check(!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" details collapsed");
                    Set(right,"TrackingValid",false);view.Refresh();
                    check(!controller.TechnicalReady&&Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" tracking failure autoexpand");
                    check(!view.LocalCommand("technical_details","SimulatedController"),lang+" cannot hide failure");stage("setup_tracking_failure");
                    Set(right,"TrackingValid",true);view.Refresh();
                    check(controller.TechnicalReady&&!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" recovery compact");stage("setup_recovered");
                    Set(controller,"TechnicalSelfSetup",false);
                    foreach(var state in new[]{ResearchFlowState.Task1Active,ResearchFlowState.Task2Active})
                    {Set(controller,"Flow",new ResearchSessionFlow(TaskOrder.AB,state,5));stage(state==ResearchFlowState.Task1Active?"task_A":"task_B");}
                    Set(controller,"Flow",new ResearchSessionFlow(TaskOrder.AB,ResearchFlowState.Task1Ready,5));
                    Set(controller,"TaskStartConfirmation",false);stage("task_ready");
                    Set(controller,"TaskStartConfirmation",true);stage("timer_confirmation");
                    check(controller.Record==null&&controller.Flow.State==ResearchFlowState.Task1Ready,lang+" layout seed did not start a task or create study record");
                }
                catch(Exception error)
                {var cause=error is TargetInvocationException wrapper&&wrapper.InnerException!=null?wrapper.InnerException:error;check(false,lang+" fixture exception "+cause);}
                finally {owner?.Dispose();}
            }
            check(active==SceneManager.GetActiveScene()&&before.SequenceEqual(Scenes()),"caller scene list/active/dirty preserved");
            string json=JsonConvert.SerializeObject(new {scope="Owned stopped-Editor technical fixture; guards/tracking and confirmation layout seeded; direct LocalCommand Details route only, no EventSystem or physical input proof; actual Refresh/TMP meshes and mono captures; no timing/wearer/language approval",checks,stages,failures,pass=failures.Count==0},Formatting.Indented);
            string output=Path.Combine(dir,"compact-ui.json");File.WriteAllText(output,json);return output;
        }
        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Fields).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetProperty(name,Fields).GetSetMethod(true).Invoke(o,new[]{value});
        static string[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.handle+"|"+s.path+"|"+s.isDirty;}).ToArray();
    }
}
#endif
