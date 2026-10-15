#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchWorkflowGalleryChecks
    {
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public static string Run(string freshDirectory, ParticipantLanguage language)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || (language != ParticipantLanguage.Thai && language != ParticipantLanguage.Japanese))
                throw new InvalidOperationException("Stopped Editor TH/JA gallery only.");
            string dir = Path.GetFullPath(freshDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (dir.Equals(project, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(dir) || File.Exists(dir))
                throw new IOException("Fresh external gallery directory required.");
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Current canonical font donor required.");
            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var rows = new List<object>(); var captures = new List<object>(); var failures = new List<string>();
            var stages = new[] {
                ("Training_welcome", ResearchFlowState.TutorialWelcome, 0, (bool?)null),
                ("Training_movement", ResearchFlowState.TutorialInteractions, 0, (bool?)null),
                ("Training_select", ResearchFlowState.TutorialInteractions, 1, (bool?)null),
                ("Training_grab", ResearchFlowState.TutorialInteractions, 2, (bool?)null),
                ("Training_reader", ResearchFlowState.TutorialReferenceInspect, 3, (bool?)null),
                ("Training_inspect", ResearchFlowState.TutorialReferenceInspect, 4, (bool?)null),
                ("Training_complete", ResearchFlowState.TutorialComplete, 5, (bool?)null),
                ("Task1Ready", ResearchFlowState.Task1Ready, 5, (bool?)null),
                ("Task2Ready", ResearchFlowState.Task2Ready, 5, (bool?)null),
                ("Task_A", ResearchFlowState.Task1Active, 5, (bool?)null),
                ("Task_B", ResearchFlowState.Task2Active, 5, (bool?)null),
                ("Inspect_checking", ResearchFlowState.Task1Active, 5, (bool?)null),
                ("Inspect_fail", ResearchFlowState.Task1Active, 5, (bool?)false),
                ("Inspect_pass", ResearchFlowState.Task1Active, 5, (bool?)true),
                ("Checkpoint1", ResearchFlowState.PostTask1Checkpoint, 5, (bool?)null),
                ("Checkpoint2", ResearchFlowState.PostTask2Checkpoint, 5, (bool?)null),
                ("ParticipantComplete", ResearchFlowState.ParticipantComplete, 5, (bool?)null),
                ("ResearcherSummary", ResearchFlowState.ResearcherSummary, 5, (bool?)null),
                ("Researcher_DEV_selector", ResearchFlowState.Task1Ready, 5, (bool?)null)
            };
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var stage in stages)
                {
                    object owned = null;
                    try
                    {
                        owned = Activator.CreateInstance(ownedType, Instance, null, new object[] { Path.Combine(dir, stage.Item1), donor, language }, null);
                        var controller = Get<ResearchSessionFlowController>(owned,"Controller"); var view = Get<ResearchFlowView>(owned,"View");
                        Call(owned,"Seed",stage.Item2,stage.Item3,stage.Item4);
                        var timing = Get<ResearchTaskTiming>(controller,"taskTiming");
                        if (controller.Flow.IsTaskActive)
                        {
                            timing.Start(0);
                            if (stage.Item1.StartsWith("Inspect_",StringComparison.Ordinal))
                            {
                                if (!timing.RequestInspection(0,stage.Item4 == true)) throw new InvalidOperationException("Owned inspection clock could not start.");
                                if (stage.Item4.HasValue)
                                {
                                    timing.Tick(ResearchTaskTiming.InspectionSeconds);
                                    if (!timing.MarkFeedbackVisible(ResearchTaskTiming.InspectionSeconds)) throw new InvalidOperationException("Owned feedback stage could not become visible.");
                                }
                            }
                        }
                        var panel = view.mainPanel;
                        if (stage.Item1 == "Training_reader") { Set(controller,"ReferenceOpen",true); Set(controller,"ReferenceSection","user_manual"); panel=view.referencePanel; }
                        if (stage.Item1 == "Researcher_DEV_selector")
                        {
                            Set(controller,"Record",null); Set(controller,"Flow",null); Set(controller,"TechnicalSelfSetup",true); Set(controller,"SetupLanguage",language);
                            var researcher = ResearcherSetupView.Build(controller.transform,language == ParticipantLanguage.Thai ? donor.thaiFont : donor.japaneseFont);
                            researcher.controller = controller;
                            Call(owned,"Refresh");
                            if (!view.OpenResearcherSetup()) throw new InvalidOperationException("Actual owned researcher ShowOnQuest path rejected.");
                            Call(owned,"Refresh"); Canvas.ForceUpdateCanvases();
                            var actualCanvas = researcher.GetComponent<Canvas>();
                            var actualSurface = researcher.transform.Find("Setup") as RectTransform;
                            var startCaption = Get<TMP_Text>(researcher,"startLabel");
                            var groupCaption = Get<TMP_Dropdown>(researcher,"group").captionText;
                            var visibleCaptions = researcher.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text)).ToArray();
                            if (!actualCanvas || !actualCanvas.enabled || !actualCanvas.gameObject.activeInHierarchy || actualCanvas.renderMode != RenderMode.WorldSpace
                                || !actualSurface || !actualSurface.gameObject.activeInHierarchy || !startCaption.gameObject.activeInHierarchy
                                || !groupCaption.gameObject.activeInHierarchy || !groupCaption.text.Contains("DEV") || visibleCaptions.Length == 0)
                                throw new InvalidOperationException("Actual visible researcher Canvas, DEV selector and captions required; hidden main panel is not researcher evidence.");
                            // Capture already accepts a ResearchPanelMotion solely for its RectTransform bounds. This disabled marker lives only on the owned actual researcher surface.
                            panel = actualSurface.gameObject.AddComponent<ResearchPanelMotion>(); panel.enabled = false;
                            rows.Add(new {stage=stage.Item1,actual_researcher_canvas=actualCanvas.name,actual_capture_surface=actualSurface.name,
                                caption_count=visibleCaptions.Length,caption_texts=visibleCaptions.Select(t=>t.text).ToArray(),
                                dev_selector=groupCaption.text,main_content_hidden=!Get<GameObject>(view,"mainContent").activeSelf,
                                researcher_child_of_main=researcher.transform.IsChildOf(view.mainPanel.transform),actual_canvas_visible=true});
                        }
                        Call(owned,"Capture",stage.Item1,panel,captures,rows);
                        var overflow = Get<List<string>>(owned,"TextOverflows");
                        failures.AddRange(overflow.Select(x=>language+"/"+x));
                        rows.Add(new { stage=stage.Item1,language=language.ToString(),seeded=true,flow=controller.Flow?.State.ToString(),inspection=timing.Stage.ToString(),overflowCount=overflow.Count,
                            localization_review_required=stage.Item1=="Researcher_DEV_selector",pass=overflow.Count==0 });
                    }
                    catch(Exception error)
                    {
                        var cause=error is TargetInvocationException wrapped && wrapped.InnerException!=null ? wrapped.InnerException : error;
                        failures.Add(stage.Item1+": "+cause.Message); rows.Add(new {stage=stage.Item1,error=cause.ToString(),pass=false});
                    }
                    finally { (owned as IDisposable)?.Dispose(); }
                }
            }
            finally { foreach(var cursor in cursors) if(cursor.Key)cursor.Key.HoverColor=cursor.Value; }
            bool preserved = SceneManager.GetActiveScene()==scene && scene.isDirty==dirty && EditorSceneManager.previewSceneCount==previews;
            if(!preserved)failures.Add("Caller active scene/dirty or preview count changed.");
            string report=Path.Combine(dir,"workflow-gallery.json");
            File.WriteAllText(report,JsonConvert.SerializeObject(new {language=language.ToString(),scope="Existing owned Safety/Visual fixtures and Capture, external TEST registry/writer per stage; explicit state and inspection-clock snapshots, not completed workflow, SDK input, device, comfort or language approval. Researcher is actual ShowOnQuest rendering of the DEV selector, not Display2 pixels or EditorWindow DEV console. No production mutations.",rows,captures,failures,preserved,pass=failures.Count==0},Formatting.Indented));
            if(failures.Count!=0)throw new InvalidOperationException("WORKFLOW_GALLERY_FAIL "+report+" | "+string.Join(" | ",failures));
            return "WORKFLOW_GALLERY_PASS "+report;
        }
        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Instance).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetProperty(name,Instance).GetSetMethod(true).Invoke(o,new[]{value});
        static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Instance).Invoke(o,args);
    }
}
#endif
