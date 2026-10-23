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
                    check(!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" ready collapsed");
                    check(!Get<Component>(view,"setupMetaChip").gameObject.activeSelf,lang+" redundant setup metadata chip stays hidden");stage("setup_ready");
                    check(view.LocalCommand("technical_details","SimulatedController"),lang+" details direct route accepted");
                    check(Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" details expanded");stage("setup_details");
                    check(view.LocalCommand("technical_details","SimulatedController"),lang+" details collapse route accepted");
                    check(!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" details collapsed");
                    Set(right,"TrackingValid",false);view.Refresh();
                    // 17 Oct: one status line names the missing item; the rows stay on the details page, which still opens on failure.
                    check(!controller.TechnicalReady&&!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf&&Get<TMPro.TMP_Text>(view,"setupStatus").text.Contains(ResearchStrings.Get("technical.right",lang.ToString())),lang+" tracking failure named in the status line");
                    check(view.LocalCommand("technical_details","SimulatedController")&&Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" details open during failure");stage("setup_tracking_failure");
                    check(view.LocalCommand("technical_details","SimulatedController"),lang+" details close during failure");
                    Set(right,"TrackingValid",true);view.Refresh();
                    check(controller.TechnicalReady&&!Get<RectTransform>(view,"technicalRows").gameObject.activeSelf,lang+" recovery compact");stage("setup_recovered");
                    Set(controller,"TechnicalSelfSetup",false);
                    foreach(var state in new[]{ResearchFlowState.Task1Active,ResearchFlowState.Task2Active})
                    {
                        Set(controller,"Flow",new ResearchSessionFlow(TaskOrder.AB,state,5));stage(state==ResearchFlowState.Task1Active?"task_A":"task_B");
                        check(!view.LocalCommand("task_details","SimulatedController"),lang+" "+controller.Flow.CurrentTask+" unstarted fixture task clock blocks detail commands");
                        // Owner 20 Oct: the same card with the participant's "full text" choice (verbatim S1 sentences).
                        typeof(ResearchFlowView).GetField("taskDetailsOpen",Fields).SetValue(view,true);
                        stage(state==ResearchFlowState.Task1Active?"task_A_full":"task_B_full");
                    }
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

        // Surfaces a participant meets around a Task with a live session record: Task card, Reader, DEV and Study pause cards,
        // Study Stop card and the off-view strip. Render-only seeds for before/after review; no clicks, no Play.
        public static string RunSessionStages(string freshDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stop Play and XR before the isolated compact UI fixture.");
            string dir=Path.GetFullPath(freshDirectory);
            if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException("Fresh evidence directory required: "+dir);
            var donor=Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v=>v.thaiFont&&v.japaneseFont&&v.englishFont);
            if(!donor) throw new InvalidOperationException("Load the authored ResearchFlow view/fonts first.");
            var before=Scenes(); var active=SceneManager.GetActiveScene();
            Directory.CreateDirectory(dir);
            var stages=new List<object>();var failures=new List<string>();var checks=new List<object>();
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
                    var left=controller.gameObject.AddComponent<HandsLabInputGuard>();left.side=Oculus.Interaction.Input.Handedness.Left;
                    var right=controller.gameObject.AddComponent<HandsLabInputGuard>();right.side=Oculus.Interaction.Input.Handedness.Right;
                    Set(left,"TrackingValid",true);Set(right,"TrackingValid",true);controller.guards=new[]{left,right};
                    var strip=view.RecoveryCanvas.GetComponent<ResearchPanelMotion>();
                    typeof(ResearchPanelMotion).GetField("environment",Fields).SetValue(strip,Array.Empty<Renderer>());
                    var registry=controller.Registry;
                    var dev=registry.StartOrResume(registry.CreateNext(ParticipantGroup.Test).participant_id,lang,ExperimentInputMode.Controller,TaskOrder.AB);
                    var study=JsonConvert.DeserializeObject<ParticipantRecord>(JsonConvert.SerializeObject(dev));study.test_data=false;
                    void Seed(ParticipantRecord record,ResearchFlowState state,string pause,bool reader)
                    {
                        var flow=new ResearchSessionFlow(TaskOrder.AB,state,5);if(pause!=null)flow.Pause();
                        Set(controller,"Record",record);Set(controller,"Flow",flow);Set(controller,"PauseKind",pause??"");
                        Set(controller,"ReferenceOpen",reader);Set(controller,"ReferenceMinimized",false);Set(controller,"ReferenceSection","user_manual");
                        view.Refresh();Canvas.ForceUpdateCanvases();
                    }
                    void Stage(string name,ResearchPanelMotion panel,bool measure)
                    {
                        string id=lang+"_"+name;
                        var measurement=measure?JObject.Parse(ResearchCompactUIMeasure.Measure(view,Path.Combine(dir,id+".json"))):null;
                        var capture=type.GetMethod("Capture",Fields).Invoke(owner,new object[]{name,panel});
                        var offered=view.GetComponentsInChildren<ResearchUIAction>(false).Where(a=>a.GetComponent<UnityEngine.UI.Button>().IsInteractable())
                            .Select(a=>(a.GetComponentInParent<ResearchPanelMotion>()?.panelId??"")+":"+a.command).ToArray();
                        stages.Add(new{id,record=controller.Record.test_data?"TEST":"Study",paused=controller.Flow.Paused,controller.PauseKind,offered,measurement,capture});
                        if(measurement!=null&&!(bool)measurement["pass"])failures.Add(id+" geometry");
                    }
                    Seed(dev,ResearchFlowState.Task1Ready,null,false);Stage("session_ready_A",view.mainPanel,true);
                    Seed(dev,ResearchFlowState.Task1Active,null,false);Stage("session_task_A",view.mainPanel,true);
                    Seed(dev,ResearchFlowState.Task2Active,null,false);Stage("session_task_B",view.mainPanel,true);
                    Get<ResearchTaskTiming>(controller,"taskTiming").Start(((VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogger)type.GetField("Writer",Fields).GetValue(owner)).Timestamp);
                    var fullText=typeof(ResearchFlowView).GetField("taskDetailsOpen",Fields); // absent before the 20 Oct card
                    if(fullText!=null)
                        foreach(var state in new[]{ResearchFlowState.Task1Active,ResearchFlowState.Task2Active})
                        {
                            Seed(dev,state,null,false);
                            check(controller.Record.test_data && Get<ResearchTaskTiming>(controller,"taskTiming").CanManipulate,
                                lang+" "+controller.Flow.CurrentTask+" details fixture owns TEST record and started manipulable clock");
                            CheckTaskDetails(view,controller,check);
                            fullText.SetValue(view,true);Stage(state==ResearchFlowState.Task1Active?"session_task_A_full":"session_task_B_full",view.mainPanel,true);
                        }
                    Seed(dev,ResearchFlowState.Task1Active,null,true);Stage("session_reader_A",view.referencePanel,false);
                    Seed(dev,ResearchFlowState.Task1Active,"participant",false);Stage("pause_dev",view.mainPanel,true);
                    Seed(study,ResearchFlowState.Task1Active,"participant",false);Stage("pause_study",view.mainPanel,true);
                    Seed(study,ResearchFlowState.Task1Active,"stop",false);Stage("stop_study",view.mainPanel,true);
                    Seed(dev,ResearchFlowState.Task1Active,null,false);
                    // A minimized task card is the deliberate off-view case (TerminalUI "Main minimized"): no glance dwell applies.
                    if(!view.LocalCommand("main_minimize","None"))failures.Add(lang+" task card did not minimize");
                    view.RefreshRecoveryUtility();
                    // Edit mode never runs the placement frame or the fade-in; apply both as the next frame would.
                    typeof(ResearchPanelMotion).GetMethod("TryApplyPendingRecenter",Fields).Invoke(strip,new object[]{true,true,true,Time.frameCount+1});
                    foreach(var group in strip.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
                    Canvas.ForceUpdateCanvases();
                    if(!strip.gameObject.activeSelf||!strip.transform.Find("Stop").gameObject.activeSelf||strip.PlacementBlocked)failures.Add(lang+" off-view strip has no placed Stop");
                    Stage("offview_strip",strip,false);
                }
                catch(Exception error)
                {var cause=error is TargetInvocationException wrapper&&wrapper.InnerException!=null?wrapper.InnerException:error;failures.Add(lang+" fixture exception "+cause);}
                finally {owner?.Dispose();}
            }
            if(!(active==SceneManager.GetActiveScene()&&before.SequenceEqual(Scenes())))failures.Add("caller scene list/active/dirty changed");
            string json=JsonConvert.SerializeObject(new {scope="Owned stopped-Editor fixture with a TEST record (Study = same record with test_data false); render-only state seeds and direct view-only Details commands on an owned TEST record with a started fixture clock; no native clicks; actual Refresh/TMP meshes and mono captures; not workflow, pointer, Quest or language approval",stages,checks,failures,pass=failures.Count==0},Formatting.Indented);
            string output=Path.Combine(dir,"session-stages.json");File.WriteAllText(output,json);return output;
        }
        static void CheckTaskDetails(ResearchFlowView view, ResearchSessionFlowController controller, Action<bool, string> check)
        {
            string id = controller.Language + " " + controller.Flow.CurrentTask;
            var brief = Get<RectTransform>(view, "taskBrief");
            var scroll = Get<UnityEngine.UI.ScrollRect>(view, "taskBriefScroll");
            var panel = (RectTransform)view.mainPanel.transform;
            var taskActions = Get<UnityEngine.UI.Button[]>(view, "actions");
            var referenceButton = taskActions.First(b => b.GetComponent<ResearchUIAction>().command == "reference");
            var inspectButton = taskActions.First(b => b.GetComponent<ResearchUIAction>().command == "inspect");
            check(referenceButton.targetGraphic.color == inspectButton.targetGraphic.color
                && referenceButton.GetComponentInChildren<TMPro.TMP_Text>().color == inspectButton.GetComponentInChildren<TMPro.TMP_Text>().color
                && ((RectTransform)referenceButton.transform).sizeDelta == ((RectTransform)inspectButton.transform).sizeDelta,
                id + " Reference and Inspect have equal visual weight");
            var closedSize = panel.sizeDelta;
            var closedPose = new Pose(panel.position, panel.rotation);
            var state = controller.Flow.State;
            bool reference = controller.ReferenceOpen;
            check(brief.anchoredPosition.y == 0 && !scroll.enabled && brief.GetChild(0).gameObject.activeSelf && brief.GetChild(1).gameObject.activeSelf
                && !brief.GetChild(2).gameObject.activeSelf && !brief.GetChild(3).gameObject.activeSelf, id + " collapsed brief has two summary rows");
            bool opened = view.LocalCommand("task_details", "SimulatedController");
            check(opened, id + " open inline details");
            if (!opened) return;
            Canvas.ForceUpdateCanvases();
            var expected = ResearchStrings.Get("task." + controller.Flow.CurrentTask + ".body", controller.Language).Split('\n');
            check(expected.Length == 4 && brief.childCount == 4, id + " four authored detail paragraphs");
            for (int i = 0; i < expected.Length; i++)
            {
                var label = brief.GetChild(i).Find("Label").GetComponent<TMPro.TMP_Text>();
                string text = label.text.Replace("<nobr>", "").Replace("</nobr>", "");
                check(text == expected[i] && label.fontSize == 32 && !label.enableAutoSizing, id + " complete paragraph " + i + " without font shrink");
            }
            check(panel.sizeDelta == closedSize && Vector3.Distance(panel.position, closedPose.position) < .00001f
                && Quaternion.Angle(panel.rotation, closedPose.rotation) < .001f, id + " details never resizes or relocates panel");
            check(scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>() && scroll.content == brief
                && brief.parent == scroll.viewport && brief.rect.height > scroll.viewport.rect.height, id + " full details scrolls inside bounded body viewport");
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            float bottom = brief.anchoredPosition.y;
            var last = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, brief.GetChild(3));
            check(bottom > 0 && last.min.y >= scroll.viewport.rect.yMin - 1 && last.max.y <= scroll.viewport.rect.yMax + 1,
                id + " final paragraph reachable inside viewport at scroll bottom");
            view.Refresh(); Canvas.ForceUpdateCanvases(); view.Refresh();
            check(Mathf.Abs(brief.anchoredPosition.y - bottom) < .1f, id + " repeated refresh keeps scroll position");
            check(view.LocalCommand("task_details", "SimulatedController") && !scroll.enabled, id + " collapse details");
            check(view.LocalCommand("task_details", "SimulatedController"), id + " reopen details");
            check(Mathf.Abs(brief.anchoredPosition.y - bottom) < .1f, id + " collapse reopen keeps scroll position");
            check(controller.ReferenceOpen == reference && controller.Flow.State == state, id + " detail choice never changes Reference or workflow");
            check(view.LocalCommand("task_details", "SimulatedController"), id + " leave next fixture stage collapsed");
        }

        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Fields).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetProperty(name,Fields).GetSetMethod(true).Invoke(o,new[]{value});
        static string[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.handle+"|"+s.path+"|"+s.isDirty;}).ToArray();
    }
}
#endif
