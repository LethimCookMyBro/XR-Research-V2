#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchUIControlAcquisitionChecks
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,All).Invoke(o,args);
        static T Field<T>(object o,string name) => (T)o.GetType().GetField(name,All).GetValue(o);
        static void Set(object o,string name,object value) => o.GetType().GetProperty(name,All).GetSetMethod(true).Invoke(o,new[]{value});
        static string[] Scenes() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=> { var s=SceneManager.GetSceneAt(i); return s.handle+":"+s.path+":"+s.isDirty; }).ToArray();
        static string Hash(string p) { using(var s=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) using(var h=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }
        static string[] CallerFiles(ResearchSessionFlowController c) => !c ? Array.Empty<string>() : new[]{c.Registry?.RegistryPath,c.Registry?.ProtocolPath,c.Logger?.EventsPath,c.Logger?.MovementPath,c.Logger?.MetadataPath}.Where(File.Exists).Select(p=>p+":"+Hash(p)).ToArray();
        public static string Run(string evidenceDirectory)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if(!Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Fresh absolute external directory required.");
            string directory=Path.GetFullPath(evidenceDirectory), project=Path.GetFullPath(Path.Combine(Application.dataPath,"..")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if((directory+Path.DirectorySeparatorChar).StartsWith(project,StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new IOException("Use fresh external evidence space.");
            if(!ResearchUIPolicy.ReaderPoseFixedPerTask) throw new InvalidOperationException("Current fixed Reader policy required; fixture never changes global policy.");
            var donor=Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v=>v.thaiFont&&v.japaneseFont&&v.englishFont);
            if(!donor) throw new InvalidOperationException("Authored view/fonts required.");
            var type=typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned",BindingFlags.NonPublic);
            var before=Scenes(); var active=SceneManager.GetActiveScene(); int previews=EditorSceneManager.previewSceneCount;
            var files=CallerFiles(donor.controller); string caller=JsonConvert.SerializeObject(new { Record=donor.controller?.Record, Flow=donor.controller?.Flow, Error=donor.controller?.Error });
            var cursors=Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c=>c,c=>c.HoverColor);
            var rows=new List<object>(); var errors=new List<string>(); int failures=0;
            void Check(bool pass,string name,object data) { rows.Add(new { pass,name,data }); if(!pass) failures++; }
            Application.LogCallback onLog=(message,stack,kind)=> { if(kind==LogType.Error||kind==LogType.Exception) errors.Add(message+"\n"+stack); };
            Directory.CreateDirectory(directory); Application.logMessageReceived+=onLog;
            try
            {
                foreach(var language in new[]{ParticipantLanguage.Thai,ParticipantLanguage.Japanese})
                foreach(var stage in new[]{"details","pdf_A","pdf_B"})
                {
                    object fixture=null; string name=language+"/"+stage;
                    try
                    {
                        fixture=Activator.CreateInstance(type,All,null,new object[]{Path.Combine(directory,language+"_"+stage),donor,language},null);
                        var c=Field<ResearchSessionFlowController>(fixture,"Controller"); var v=Field<ResearchFlowView>(fixture,"View");
                        var eventSystem=Field<EventSystem>(fixture,"eventSystem");
                        void Flush()
                        {
                            Call(fixture,"Refresh");
                            if(c.ReferenceOpen)
                            {
                                v.ReferenceViewer.SetVisible(true); Canvas.ForceUpdateCanvases(); Call(v.ReferenceViewer,"Update");
                                Call(Field<ScrollRect>(v,"scroll"),"LateUpdate"); Canvas.ForceUpdateCanvases(); v.RefreshPageControls();
                            }
                        }
                        ResearchUIAction Action(string command) => (ResearchUIAction)Call(fixture,"Action",command,null);
                        PointerEventData Down(ResearchUIAction a,int clicks=1,int id=74101)
                        {
                            var data=new PointerEventData(eventSystem){pointerId=id,button=PointerEventData.InputButton.Left,clickCount=clicks};
                            ExecuteEvents.Execute(a.gameObject,data,ExecuteEvents.pointerDownHandler); return data;
                        }
                        void Up(ResearchUIAction a,PointerEventData data)
                        {
                            if(a) { ExecuteEvents.Execute(a.gameObject,data,ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(a.gameObject,data,ExecuteEvents.pointerClickHandler); }
                            Flush();
                        }
                        void Click(string command,int clicks=1) { Flush(); var a=Action(command); Up(a,Down(a,clicks)); }
                        List<JObject> Events()
                        {
                            using(var stream=new StreamReader(new FileStream(c.Logger.EventsPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)))
                                return stream.ReadToEnd().Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).ToList();
                        }
                        int Count(string key) => Events().Count(e=>(string)e["event"]==key);
                        if(stage=="details")
                        {
                            var ownedRecord=c.Record; string registryHash=Hash(c.Registry.RegistryPath);
                            Set(c,"Record",null); Set(c,"Flow",null); Set(c,"TechnicalSelfSetup",true);
                            c.guards[0].side=Oculus.Interaction.Input.Handedness.Left; c.guards[1].side=Oculus.Interaction.Input.Handedness.Right; Flush();
                            bool Visible() => Field<bool>(v,"setupDetailsVisible");
                            Check(c.TechnicalReady&&!Visible(),name+"/ready compact precondition",new { c.TechnicalReady,visible=Visible() });
                            Click("technical_details"); Check(Visible(),name+"/actual click expands",Visible());
                            Click("technical_details"); Check(!Visible(),name+"/actual second click collapses",Visible());
                            var a=Action("technical_details"); Up(a,new PointerEventData(eventSystem){pointerId=74101,button=PointerEventData.InputButton.Left,clickCount=1});
                            Check(!Visible(),name+"/orphan release cannot toggle",Visible());
                            a=Action("technical_details"); var held=Down(a); Up(Action("technical_start"),held);
                            Check(c.Record==null&&c.Flow==null&&!Visible(),name+"/wrong-control release cannot start session",new { c.Record,c.Flow });
                            Call(fixture,"CancelPress",a);
                            a=Action("technical_details"); held=Down(a); Set(c,"Error","owned context changed"); Up(a,held); Set(c,"Error",""); Flush();
                            Check(!Visible(),name+"/stale context release cannot toggle",Visible());
                            Check(Hash(c.Registry.RegistryPath)==registryHash&&c.Registry.List(ParticipantGroup.Test).Single().run_id==ownedRecord.run_id,name+"/no new session or registry mutation",c.Registry.RegistryPath);
                            // Quest 16 Oct: the tester could only change language/order inside the dense researcher panel.
                            var startLanguage=c.SetupLanguage; var startOrder=c.SetupOrder;
                            Click("technical_language"); Check(c.SetupLanguage!=startLanguage,name+"/card language toggle clicks",c.SetupLanguage.ToString());
                            Click("technical_language"); Check(c.SetupLanguage==startLanguage,name+"/card language toggle returns",c.SetupLanguage.ToString());
                            Click("technical_order"); Check(c.SetupOrder!=startOrder,name+"/card order toggle clicks",c.SetupOrder.ToString());
                            var body=Field<TMPro.TMP_Text>(v,"body"); var next=c.TechnicalNextRun();
                            Check(next.id=="DEV001"&&(next.layout=="P"||next.layout=="Q")&&body.gameObject.activeInHierarchy&&body.text.Contains(next.id),name+"/card names the DEV id and layout Start will use",new { next.id,next.layout,body.text });
                            Check(Hash(c.Registry.RegistryPath)==registryHash,name+"/previewing creates no record",null);
                            Call(fixture,"Capture","setup_card",v.mainPanel,new List<object>(),rows);
                            Click("technical_order"); Check(c.SetupOrder==startOrder,name+"/card order toggle returns",c.SetupOrder.ToString());
                            // A Start that fails after CreateNext leaves an unstarted DEV record; the next Start reuses it.
                            var orphan=c.Registry.CreateNext(ParticipantGroup.Dev);
                            Set(c,"SetupOrder",c.SetupOrder==TaskOrder.AB?TaskOrder.BA:TaskOrder.AB); Set(c,"SetupOrder",startOrder);
                            Check(c.TechnicalNextRun().id==orphan.participant_id&&c.Registry.List(ParticipantGroup.Dev).Count==1,name+"/unstarted DEV record is reused, not duplicated",c.TechnicalNextRun().id);
                        }
                        else
                        {
                            Call(fixture,"Seed",stage=="pdf_A"?ResearchFlowState.Task1Active:ResearchFlowState.Task2Active,5,null);
                            Field<ResearchTaskTiming>(c,"taskTiming").Start(c.Logger.Timestamp);
                            Set(c,"ReferenceOpen",true); Set(c,"ReferenceMinimized",false); Set(c,"ReferenceSection","user_manual"); Flush();
                            var viewer=v.ReferenceViewer; float Zoom() => (float?)JObject.FromObject(viewer.Metadata)["pdf_zoom"]??-1;
                            int Page() => viewer.PageIndex;
                            var pose=v.referencePanel.transform.position; var rotation=v.referencePanel.transform.rotation; var size=((RectTransform)v.referencePanel.transform).sizeDelta;
                            string identity=c.Record.participant_id+"/"+c.Record.session_id+"/"+c.Record.run_id; int credits=c.Flow.TutorialCompleted;
                            Check(viewer.IsReady&&viewer.PageCount>=2&&Page()==1&&Zoom()==1,name+"/authored PDF ready",viewer.Metadata);
                            int nav=Count("REFERENCE_VIEWER_NAVIGATE"), zoom=Count("REFERENCE_PDF_VIEW_CHANGED");
                            Click("reference_next"); Check(Page()==2,name+"/actual Next advances one page",viewer.Metadata);
                            Click("reference_previous"); Check(Page()==1,name+"/actual Previous decrements page",viewer.Metadata);
                            Click("reference_next",2); Check(Page()==2,name+"/fresh page clickCount2 advances once",viewer.Metadata);
                            var next=Action("reference_previous"); Up(next,new PointerEventData(eventSystem){pointerId=74101,button=PointerEventData.InputButton.Left,clickCount=1});
                            Check(Page()==2,name+"/orphan page release does not navigate",viewer.Metadata);
                            var held=Down(next,1,74102); Click("reference_previous"); int afterFresh=Page(); Up(next,held);
                            Check(afterFresh==1&&Page()==afterFresh,name+"/old page acquisition cannot navigate after fresh other-pointer page change",viewer.Metadata);
                            Check(Count("REFERENCE_VIEWER_NAVIGATE")==nav+4,name+"/four accepted page clicks match observer log",new { actual=Count("REFERENCE_VIEWER_NAVIGATE")-nav });
                            Click("reference_media.zoom_in"); Check(Mathf.Approximately(Zoom(),1.25f),name+"/actual zoom in",viewer.Metadata);
                            Click("reference_media.zoom_in"); Check(Mathf.Approximately(Zoom(),1.5f),name+"/fresh zoom reacquisition",viewer.Metadata);
                            Click("reference_media.zoom_out"); Check(Mathf.Approximately(Zoom(),1.25f),name+"/actual zoom out",viewer.Metadata);
                            Click("reference_media.zoom_reset"); Check(Mathf.Approximately(Zoom(),1),name+"/actual zoom reset",viewer.Metadata);
                            var zoomAction=Action("reference_media.zoom_in"); held=Down(zoomAction,1,74102); Click("reference_next"); Up(zoomAction,held);
                            Check(Zoom()==1,name+"/zoom held across page change rejected",viewer.Metadata);
                            zoomAction=Action("reference_media.zoom_in"); held=Down(zoomAction,1,74102); Click("reference_tab.step_by_step"); Up(zoomAction,held);
                            Check(c.ReferenceSection=="step_by_step"&&!v.GetComponentsInChildren<ResearchUIAction>(false).Any(a=>a.command=="reference_media.zoom_in"),name+"/format switch retires held PDF action",viewer.Metadata);
                            Click("reference_tab.user_manual"); Check(Zoom()==1,name+"/return to PDF retains unchanged zoom",viewer.Metadata);
                            Click("reference_media.zoom_in"); Check(Zoom()==1.25f,name+"/fresh PDF action after format return accepted",viewer.Metadata);
                            Check(Count("REFERENCE_PDF_VIEW_CHANGED")==zoom+5,name+"/only five fresh zoom clicks logged",new { actual=Count("REFERENCE_PDF_VIEW_CHANGED")-zoom });
                            Check(pose==v.referencePanel.transform.position&&rotation==v.referencePanel.transform.rotation&&size==((RectTransform)v.referencePanel.transform).sizeDelta,name+"/fixed panel pose and bounds preserved",new {pose=new[]{pose.x,pose.y,pose.z},actual=new[]{v.referencePanel.transform.position.x,v.referencePanel.transform.position.y,v.referencePanel.transform.position.z},size=new[]{size.x,size.y},actualSize=new[]{((RectTransform)v.referencePanel.transform).sizeDelta.x,((RectTransform)v.referencePanel.transform).sizeDelta.y}});
                            Check(identity==c.Record.participant_id+"/"+c.Record.session_id+"/"+c.Record.run_id&&credits==c.Flow.TutorialCompleted&&c.Flow.IsTaskActive&&!c.Flow.Paused&&string.IsNullOrEmpty(c.Error),name+"/IDs credits and task flow unchanged",new { c.Record,c.Flow.TutorialCompleted,c.Flow.State,c.Error });
                            rows.Add(new { name=name+"/observer",c.Logger.EventsPath,sha256=Hash(c.Logger.EventsPath) });
                        }
                    }
                    catch(Exception ex) { Check(false,name+"/execution",(ex.InnerException??ex).ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            finally { Application.logMessageReceived-=onLog; foreach(var cursor in cursors) if(cursor.Key) cursor.Key.HoverColor=cursor.Value; }
            Check(errors.Count==0,"no swallowed Unity callback failures",errors);
            Check(before.SequenceEqual(Scenes())&&active==SceneManager.GetActiveScene()&&previews==EditorSceneManager.previewSceneCount,"caller scenes preserved",Scenes());
            Check(files.SequenceEqual(CallerFiles(donor.controller))&&caller==JsonConvert.SerializeObject(new { Record=donor.controller?.Record,Flow=donor.controller?.Flow,Error=donor.controller?.Error }),"caller registry logs and state preserved",null);
            string output=Path.Combine(directory,"ui-control-acquisition.json");
            File.WriteAllText(output,JsonConvert.SerializeObject(new { status=failures==0?"PASS":"FAIL",failures,rows,
                scope="Stopped Editor actual Unity Button/EventSystem down/up/click and ResearchUIAction callbacks. Reuses isolated Safety Owned fixture with explicit runtime action lifecycle and seeded technical tracking/task initial state. Authored TH/JA Task A/B media. Two distinct synthetic pointer IDs for stale held-acquisition tests. No direct LocalCommand or Button.onClick.Invoke; no SDK ray acquisition, Quest, wearer, study approval or successful training claim." },Formatting.Indented));
            if(failures!=0) throw new InvalidOperationException("UI_CONTROL_ACQUISITION_FAIL "+output);
            return "UI_CONTROL_ACQUISITION_PASS "+output;
        }
    }
}
#endif
