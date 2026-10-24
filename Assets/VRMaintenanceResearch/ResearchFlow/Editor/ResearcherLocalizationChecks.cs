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
    public static class ResearcherLocalizationChecks
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
            var stages = new[] { ("Researcher_DEV_selector", ResearchFlowState.Task1Ready, 5, (bool?)null) };
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
                            // This stage explicitly measures DEV; the first-launch default is now the Thai study group.
                            Get<TMP_Dropdown>(researcher,"group").value = 2;
                            researcher.Refresh();
                            Call(owned,"Refresh"); Canvas.ForceUpdateCanvases();
                            var actualCanvas = researcher.GetComponent<Canvas>();
                            var actualSurface = researcher.transform.Find("Setup") as RectTransform;
                            var startCaption = Get<TMP_Text>(researcher,"startLabel");
                            var groupCaption = Get<TMP_Dropdown>(researcher,"group").captionText;
                            var visibleCaptions = researcher.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text)).ToArray();
                            rows.Add(new { check="researcher_surface_diagnostic", actualCanvas.enabled, active=actualCanvas.gameObject.activeInHierarchy,
                                mode=actualCanvas.renderMode.ToString(), surface=actualSurface && actualSurface.gameObject.activeInHierarchy,
                                start=startCaption.gameObject.activeInHierarchy, group=groupCaption.text, captions=visibleCaptions.Length });
                            if (!actualCanvas || !actualCanvas.enabled || !actualCanvas.gameObject.activeInHierarchy || actualCanvas.renderMode != RenderMode.WorldSpace
                                || !actualSurface || !actualSurface.gameObject.activeInHierarchy || !startCaption.gameObject.activeInHierarchy
                                || !groupCaption.gameObject.activeInHierarchy || !groupCaption.text.Contains("DEV") || visibleCaptions.Length == 0)
                                throw new InvalidOperationException("Actual visible researcher Canvas, DEV selector and captions required; hidden main panel is not researcher evidence.");
                            var keyCaptions = new[] { startCaption, groupCaption, Get<TMP_Text>(researcher,"status"), Get<TMP_Text>(researcher,"error"), actualSurface.GetChild(0).GetComponent<TMP_Text>() };
                            bool Script(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", language == ParticipantLanguage.Thai ? @"[฀-๿]" : @"[぀-ヿ一-鿿]");
                            foreach (var label in keyCaptions)
                            {
                                bool pass=label && Script(label.text);
                                rows.Add(new { check="localized_visible_caption", text=label?label.text:null, pass });
                                if(!pass) failures.Add("Required researcher heading/start/group/readiness/reason lacks requested script.");
                            }
                            var reasonInputs = new[] {
                                "Select or create a participant ID.", "Device not ready: floor/boundary is not confirmed.", "Device not ready: both controllers must be tracked.", "Data logger not ready.",
                                "No open session.", "Station transition in progress.", "Participant app is not focused.", "Headset or controllers not tracked.", "Quest setup requires Controller mode.",
                                "A session is already open: DEV123", "DEV123 is Completed and stays locked.", "Session is InProgress; logger Error.", "Nothing to resume: participant is in Task1Active." };
                            var reasonKeys = new[] { "select_block", "floor_block", "tracking_block", "logger_block", "no_session", "transition", "focus", "tracking", "quest_mode", "open_session", "state_locked", "session_logger", "nothing_resume" };
                            var reasonArgs = new[] { Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(), new object[]{"DEV123"}, new object[]{"DEV123","Completed"}, new object[]{"InProgress","Error"}, new object[]{"Task1Active"} };
                            for(int ri=0;ri<reasonInputs.Length;ri++)
                            {
                                var localizer=researcher.GetType().GetMethod("LocalizeBlocker",Instance);
                                string actual=localizer!=null?(string)localizer.Invoke(researcher,new object[]{reasonInputs[ri]}):reasonInputs[ri];
                                string expected=string.Format(ResearchStrings.Get("researcher."+reasonKeys[ri],language.ToString()),reasonArgs[ri]);
                                bool pass=Script(actual)&&actual==expected;
                                rows.Add(new {check="readiness_reason", input=reasonInputs[ri],actual,expected,localized_entrypoint_present=localizer!=null,pass});
                                if(!pass)failures.Add("Lost/localization mismatch readiness reason: "+reasonKeys[ri]);
                            }
                            const string raw="E_TEST_RECORD_IO: DEV123/request-456";
                            var unknownLocalizer=researcher.GetType().GetMethod("LocalizeBlocker",Instance);
                            string unknown=unknownLocalizer!=null?(string)unknownLocalizer.Invoke(researcher,new object[]{raw}):raw;
                            rows.Add(new {check="unknown_diagnostic_preserved",actual=unknown,pass=unknown==raw});
                            if(unknown!=raw)failures.Add("Raw diagnostics changed.");
                            var sourceIds=new[]{"Language Dropdown","Task Order Dropdown","Input Mode Dropdown"};
                            var choiceIds=new[]{new[]{"Thai","Japanese","English"},new[]{"Auto","AB_P","AB_Q","BA_P","BA_Q"},new[]{"Controller","Hand Tracking","Technical Both"}};
                            for(int si=0;si<sourceIds.Length;si++)
                            {
                                var choices=actualSurface.Find(sourceIds[si]+" Choices");
                                var ids=choices.Cast<Transform>().Select(t=>t.name).ToArray();
                                bool pass=ids.SequenceEqual(choiceIds[si]);
                                rows.Add(new {check="choice_identity_order_preserved",source=sourceIds[si],ids,pass});
                                if(!pass)failures.Add("Choice ID/order changed: "+sourceIds[si]);
                            }
                            var heading=actualSurface.GetChild(0).GetComponent<TMP_Text>();
                            var other=language==ParticipantLanguage.Thai?ParticipantLanguage.Japanese:ParticipantLanguage.Thai;
                            Set(controller,"SetupLanguage",other); researcher.Refresh();
                            bool switched=heading.text==ResearchStrings.Get("researcher.setup",other.ToString()) && heading.font==(other==ParticipantLanguage.Thai?donor.thaiFont:donor.japaneseFont);
                            Set(controller,"SetupLanguage",language); researcher.Refresh(); researcher.ApplyVisualTheme(); researcher.Refresh();
                            bool restored=heading.text==ResearchStrings.Get("researcher.setup",language.ToString());
                            rows.Add(new {check="repeat_refresh_language_switch_stable_refs",switched,restored,pass=switched&&restored});
                            if(!switched||!restored)failures.Add("Translated text broke identity/font on refresh.");
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
            string report=Path.Combine(dir,"researcher-localization.json");
            File.WriteAllText(report,JsonConvert.SerializeObject(new {language=language.ToString(),scope="Existing owned Safety/Visual fixtures and Capture, external TEST registry/writer per stage; explicit state and inspection-clock snapshots, not completed workflow, SDK input, device, comfort or language approval. Researcher is actual ShowOnQuest rendering of the DEV selector, not Display2 pixels or EditorWindow DEV console. No production mutations.",rows,captures,failures,preserved,pass=failures.Count==0},Formatting.Indented));
            if(failures.Count!=0)throw new InvalidOperationException("RESEARCHER_LOCALIZATION_FAIL "+report+" | "+string.Join(" | ",failures));
            return "RESEARCHER_LOCALIZATION_PASS "+report;
        }
        public static string RunSessions(string freshDirectory, ParticipantLanguage language) => RunSessionsCore(freshDirectory, language, false);
        public static string RunLiveDelivery(string freshDirectory, ParticipantLanguage language) => RunSessionsCore(freshDirectory, language, true);
        private static string RunSessionsCore(string freshDirectory, ParticipantLanguage language, bool liveDelivery)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed || (language != ParticipantLanguage.Thai && language != ParticipantLanguage.Japanese))
                throw new InvalidOperationException("Stopped Editor with XR inactive, TH/JA only.");
            string dir = Path.GetFullPath(freshDirectory), project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(freshDirectory) || dir.Equals(project, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(dir) || File.Exists(dir))
                throw new IOException("Fresh absolute external directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Current canonical font donor required.");
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var previousEvents = UnityEngine.EventSystems.EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var rows = new List<object>(); var captures = new List<object>(); var failures = new List<string>();
            var callbackErrors = new List<string>();
            Application.LogCallback onLog = (message, stack, kind) => { if (kind == LogType.Error || kind == LogType.Exception) callbackErrors.Add(message + "\n" + stack); };
            void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed }); if (!pass) failures.Add(name); }
            Directory.CreateDirectory(dir); Application.logMessageReceived += onLog;
            try
            {
                foreach (string stage in new[] { "active", "paused", "abort_choice", "invalid_choice", "aborted_summary", "invalid_summary" })
                {
                    object owned = null;
                    try
                    {
                        string ownedPath = Path.Combine(dir, stage);
                        owned = Activator.CreateInstance(ownedType, Instance, null, new object[] { ownedPath, donor, language }, null);
                        var c = Get<ResearchSessionFlowController>(owned, "Controller"); var view = Get<ResearchFlowView>(owned, "View");
                        var events = Get<UnityEngine.EventSystems.EventSystem>(owned, "eventSystem");
                        Call(owned, "Click", (ResearchUIAction)Call(owned, "Action", "task_start", null), 1);
                        Call(owned, "Click", (ResearchUIAction)Call(owned, "Action", "task_start_confirm", null), 1);
                        string identity = c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id;
                        var timing = Get<ResearchTaskTiming>(c, "taskTiming");
                        Check(c.Flow.IsTaskActive && timing.Started && !timing.Ended && c.Record.session_state == RegistryState.InProgress, stage + "/actual task start", new { c.Record.participant_id, state = c.Flow.State.ToString(), timing.Started, timing.Ended });
                        if (!c.Flow.IsTaskActive || !timing.Started || timing.Ended) throw new InvalidOperationException("Actual task start failed; no substitute seeded active state.");
                        var researcher = ResearcherSetupView.Build(view.transform, language == ParticipantLanguage.Thai ? donor.thaiFont : donor.japaneseFont);
                        researcher.controller = c; researcher.OperatorDirectoryOverride = Path.Combine(ownedPath, "operator");
                        var canvas = researcher.GetComponent<Canvas>();
                        // Project desktop content into the owned camera; ShowOnQuest remains pre-session only.
                        researcher.transform.SetParent(view.mainPanel.transform, false);
                        var root = (RectTransform)researcher.transform;
                        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.anchoredPosition = Vector2.zero;
                        root.localScale = Vector3.one * .82f; root.localRotation = Quaternion.identity;
                        canvas.renderMode = RenderMode.WorldSpace; canvas.targetDisplay = 0; canvas.worldCamera = view.mainPanel.GetComponent<Canvas>().worldCamera; canvas.enabled = true;
                        void Flush() { researcher.Refresh(); Canvas.ForceUpdateCanvases(); }
                        void Click(UnityEngine.UI.Button button)
                        {
                            if (!button || !button.gameObject.activeInHierarchy || !button.IsInteractable()) throw new InvalidOperationException("Actual active operator button required.");
                            var pointer = new UnityEngine.EventSystems.PointerEventData(events) { pointerId = 75202, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, clickCount = 1 };
                            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                            UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler); Flush();
                        }
                        Flush();
                        UnityEngine.UI.Button OptionalButton(string name) => researcher.GetType().GetField(name, Instance)?.GetValue(researcher) as UnityEngine.UI.Button;
                        var stopButton = OptionalButton("stop"); var detailsButton = OptionalButton("details");
                        if (liveDelivery)
                        {
                            Check(stopButton && detailsButton, stage + "/distinct Stop and Details exist", new { stop = (bool)stopButton, details = (bool)detailsButton });
                            Check(!researcher.SummaryContentVisible(), stage + "/details default collapsed", researcher.SummaryContentVisible());
                            Check(!researcher.ShowOnQuest(view), stage + "/attached record cannot enter pre-session Quest setup", c.Record.session_id);
                            var displayedError = Get<TMP_Text>(researcher,"error");
                            Check(c.Flow.IsTaskActive && !c.Flow.Paused && string.IsNullOrEmpty(displayedError.text),
                                stage+"/normal active task has no routine resume error",displayedError.text);
                            string originalError=c.Error, originalLocal=Get<string>(researcher,"localError");
                            try
                            {
                                researcher.GetType().GetField("localError",Instance).SetValue(researcher,"E_ACTIVE_LOCAL"); Flush();
                                Check(displayedError.text=="E_ACTIVE_LOCAL",stage+"/active preserves local error",displayedError.text);
                                Set(c,"Error","E_ACTIVE_CONTROLLER"); Flush();
                                Check(displayedError.text=="E_ACTIVE_CONTROLLER",stage+"/active controller error has priority",displayedError.text);
                            }
                            finally { Set(c,"Error",originalError); researcher.GetType().GetField("localError",Instance).SetValue(researcher,originalLocal); Flush(); }
                        }
                        if (stage == "paused")
                        {
                            bool accepted;
                            if (liveDelivery && stopButton) { Click(stopButton); accepted = c.Flow.Paused; }
                            else { accepted = c.ResearcherStop(); Flush(); }
                            if (liveDelivery && stopButton)
                            {
                                var writer = c.Logger; double active = c.TaskActiveElapsed;
                                Click(stopButton);
                                Check(c.Flow.Paused && c.Record.session_state == RegistryState.InProgress && !timing.Ended
                                    && ReferenceEquals(writer,c.Logger) && c.Logger.State.ToString()=="Active" && Math.Abs(c.TaskActiveElapsed-active)<.001,
                                    stage + "/repeated actual Stop retains writer clock and registry", new { c.Flow.Paused, timing.Ended, active, after=c.TaskActiveElapsed, writer=c.Logger.State.ToString() });
                                string log;
                                using (var stream = new FileStream(c.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (var reader = new StreamReader(stream)) log = reader.ReadToEnd();
                                var stops = log.Split('\n').Where(line => line.Contains("\"SESSION_PAUSED\"")).Select(Newtonsoft.Json.Linq.JObject.Parse)
                                    .Where(row => (string)row["reason"] == "researcher_stop" || (string)row["reason"] == "participant_stop").ToArray();
                                Check(stops.Length == 1 && (string)stops[0]["reason"] == "researcher_stop" && (string)stops[0]["source"] == "System"
                                    && (string)stops[0]["input_source"] == "None" && (string)stops[0]["result"] == "ResearcherStopped"
                                    && !log.Contains("SESSION_ABORTED") && !log.Contains("SESSION_INVALIDATED"),
                                    stage + "/one researcher-attributed Stop persisted without terminal event", stops);
                                var unfocused=c.GetType().GetField("applicationUnfocused",Instance); bool previousFocus=(bool)unfocused.GetValue(c);
                                try
                                {
                                    unfocused.SetValue(c,true); Flush();
                                    Check(!Get<UnityEngine.UI.Button>(researcher,"resume").IsInteractable()
                                        && Get<TMP_Text>(researcher,"error").text.Contains(ResearchStrings.Get("researcher.focus",language.ToString())),
                                        stage+"/paused focus blocker remains visible",Get<TMP_Text>(researcher,"error").text);
                                }
                                finally { unfocused.SetValue(c,previousFocus); Flush(); }
                                var actualResume=Get<UnityEngine.UI.Button>(researcher,"resume");
                                Check(actualResume.IsInteractable(), stage + "/Resume still accessible after Stop", c.ResearcherResumeBlocker());
                                if(actualResume.IsInteractable()) { Click(actualResume); Check(!c.Flow.Paused && !timing.Ended, stage + "/actual Resume after Stop", c.Flow.Paused); Click(stopButton); }
                            }
                            Check(accepted && c.Flow.Paused && c.Record.session_state == RegistryState.InProgress && !timing.Ended, stage + "/actual nonterminal Stop", new { accepted, c.Flow.Paused, state = c.Record.session_state.ToString(), timing.Ended });
                        }
                        if (stage.EndsWith("_choice", StringComparison.Ordinal) || stage.EndsWith("_summary", StringComparison.Ordinal))
                        {
                            bool invalid = stage.StartsWith("invalid", StringComparison.Ordinal);
                            Click(Get<UnityEngine.UI.Button>(researcher, invalid ? "invalid" : "abort"));
                            Check(c.Record.session_state == RegistryState.InProgress && !timing.Ended, stage + "/request is nonterminal", new { state = c.Record.session_state.ToString(), timing.Ended });
                            if (stage.EndsWith("_summary", StringComparison.Ordinal))
                            {
                                Click(Get<UnityEngine.UI.Button[]>(researcher, "reasonButtons")[0]);
                                var saved = c.Registry.List(ParticipantGroup.Test).Single();
                                Check(saved.session_state == (invalid ? RegistryState.Invalid : RegistryState.Aborted) && saved.termination_reason == "withdrawal" && timing.Ended,
                                    stage + "/actual chosen ending persisted", new { state = saved.session_state.ToString(), saved.termination_reason, timing.Ended });
                            }
                        }
                        Flush();
                        var surface = researcher.transform.Find("Setup") as RectTransform;
                        Check(surface && surface.gameObject.activeInHierarchy && canvas.enabled && c.Record != null, stage + "/actual attached researcher visible", new { canvas.enabled, canvas.name });
                        var heading = surface.GetChild(0).GetComponent<TMP_Text>();
                        string expectedHeading = ResearchStrings.Get("researcher." + (stage.EndsWith("_summary", StringComparison.Ordinal) ? "summary" : "session"), language.ToString());
                        Check(heading.text == expectedHeading, stage + "/localized attached heading", new { heading.text, expectedHeading });
                        var panel = surface.gameObject.AddComponent<ResearchPanelMotion>(); panel.enabled = false;
                        if (liveDelivery)
                        {
                            Call(owned,"Capture",stage+"_compact",panel,captures,rows);
                            var deliveryLabel=researcher.GetType().GetField("delivery",Instance)?.GetValue(researcher) as TMP_Text;
                            var statusProperty=c.GetType().GetProperty("ExportDeliveryStatus");
                            Check(deliveryLabel && deliveryLabel.gameObject.activeInHierarchy && statusProperty!=null, stage+"/delivery visible including terminal", deliveryLabel ? deliveryLabel.text : "missing");
                            if(stage=="active" && deliveryLabel && statusProperty!=null)
                            {
                                int deliveryCapture = 0;
                                void Delivery(string expected)
                                {
                                    // Force an elapsed-cache boundary, not a fabricated status; production still reads the actual external TEST snapshot/receipt.
                                    c.GetType().GetField("exportStatusKey",Instance).SetValue(c,null); Flush();
                                    string actual=(string)statusProperty.GetValue(c), caption=ResearchStrings.Get("researcher.delivery_"+expected,language.ToString());
                                    Check(actual==expected && deliveryLabel.text==caption, stage+"/actual delivery "+expected,new { actual,deliveryLabel.text,caption,fixtureReceipt=true });
                                    Call(owned,"Capture","delivery_"+(++deliveryCapture)+"_"+expected,panel,captures,rows);
                                }
                                Delivery("saved");
                                string bundle=ResearchExportQueue.Snapshot(c.Registry.DirectoryPath,c.Logger,c.Record,"ui_fixture","partial").Result;
                                Delivery("queued");
                                string Hash(string path) { using(var sha=System.Security.Cryptography.SHA256.Create()) return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(x=>x.ToString("x2"))); }
                                var receipt=Newtonsoft.Json.Linq.JObject.FromObject(new {
                                    schema_version="researchflow.pc-receipt.1",bundle_id=Path.GetFileName(bundle),participant_code=c.Record.participant_id,
                                    session_id=c.Record.session_id,run_id=c.Record.run_id,source_manifest_sha256=Hash(Path.Combine(bundle,"manifest.json")),
                                    pc_bundle_id="owned_ui_fixture",pc_manifest_sha256=new string('a',64),receipt_sha256=new string('b',64),
                                    received_utc=DateTime.UtcNow.ToString("O"),qc_data_complete=false,qc_failure_count=0 });
                                string receipts=Path.Combine(c.Registry.DirectoryPath,"ExportReceipts"); Directory.CreateDirectory(receipts);
                                string receiptPath=Path.Combine(receipts,Path.GetFileName(bundle)+".json"); File.WriteAllText(receiptPath,receipt.ToString());
                                Delivery("received");
                                receipt["run_id"]="wrong_fixture_run"; File.WriteAllText(receiptPath,receipt.ToString()); Delivery("attention");
                                receipt["run_id"]=c.Record.run_id; File.WriteAllText(receiptPath,receipt.ToString()); Delivery("received");
                                Click(Get<UnityEngine.UI.Button>(researcher,"export"));
                                Check(string.IsNullOrEmpty(Get<string>(researcher,"localError")) && !Get<TMP_Text>(researcher,"error").text.Contains("Export:"),
                                    stage+"/manual export success is not an error path",new {localError=Get<string>(researcher,"localError"),c.LastExportPath});
                                Delivery("saved");
                                Check(c.WaitForExports(60000),stage+"/background export finished",c.ExportDeliveryError);
                                Delivery("queued");
                            }
                            foreach(var button in new[]{stopButton,Get<UnityEngine.UI.Button>(researcher,"resume"),Get<UnityEngine.UI.Button>(researcher,"export"),detailsButton})
                            {
                                if(!button || !button.gameObject.activeInHierarchy) continue;
                                var rect=(RectTransform)button.transform; var label=button.GetComponentInChildren<TMP_Text>(); label.ForceMeshUpdate();
                                var corners=new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                                bool contained=corners.Select(rect.InverseTransformPoint).All(v=>v.x>=rect.rect.xMin-.5f && v.x<=rect.rect.xMax+.5f && v.y>=rect.rect.yMin-.5f && v.y<=rect.rect.yMax+.5f);
                                bool glyphs=label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(ch=>ch.isVisible).All(ch=>ch.textElement!=null && ch.textElement.glyph!=null && ch.textElement.glyph.index!=0);
                                Check(contained && !label.isTextOverflowing && glyphs && rect.rect.width>=160 && rect.rect.height>=42,
                                    stage+"/actual button label and hit area "+button.name,new { label.text,contained,label.isTextOverflowing,glyphs,width=rect.rect.width,height=rect.rect.height });
                            }
                        }
                        if(liveDelivery && stopButton)
                        {
                            var square=stopButton.transform.Find("Stop Square");
                            Check(square && square.GetComponent<UnityEngine.UI.Image>() && !square.GetComponent<UnityEngine.UI.Image>().raycastTarget,
                                stage+"/geometric Stop icon independent of font atlas",square ? square.name : "missing");
                        }
                        if(detailsButton) { Click(detailsButton); Check(researcher.SummaryContentVisible(),stage+"/actual Details opens summary",researcher.SummaryContentVisible()); }
                        var attachedSummary = Get<TMP_Text>(researcher, "summary");
                        string expectedSummaryPrefix = ResearchStrings.Get("summary.test", language.ToString());
                        Check(attachedSummary.text.StartsWith(expectedSummaryPrefix, StringComparison.Ordinal) && attachedSummary.text.Contains(c.Record.participant_id),
                            stage + "/localized actual summary preserves identity", new { attachedSummary.text, expectedSummaryPrefix });
                        Check(c.SummaryText().StartsWith("TEST / DEMO DATA", StringComparison.Ordinal), stage + "/default English summary retained", c.SummaryText());
                        Call(owned, "Capture", stage, panel, captures, rows);
                        if (stage.EndsWith("_summary", StringComparison.Ordinal))
                        {
                            var displayedError = Get<TMP_Text>(researcher, "error");
                            Check(string.IsNullOrEmpty(c.Error) && string.IsNullOrEmpty(Get<string>(researcher, "localError")) && string.IsNullOrEmpty(displayedError.text),
                                stage + "/terminal has no irrelevant start resume blocker", displayedError.text);
                            string originalError = c.Error, originalLocalError = Get<string>(researcher, "localError");
                            try
                            {
                                Set(c, "Error", "E_FIXTURE_CONTROLLER"); Flush();
                                Check(displayedError.text == "E_FIXTURE_CONTROLLER", stage + "/terminal preserves controller error", displayedError.text);
                                Set(c, "Error", originalError);
                                researcher.GetType().GetField("localError", Instance).SetValue(researcher, "E_FIXTURE_OPERATOR"); Flush();
                                Check(displayedError.text == "E_FIXTURE_OPERATOR", stage + "/terminal preserves operator error", displayedError.text);
                            }
                            finally { Set(c, "Error", originalError); researcher.GetType().GetField("localError", Instance).SetValue(researcher, originalLocalError); Flush(); }
                            var summary = Get<TMP_Text>(researcher, "summary");
                            var scroll = summary.transform.parent.GetComponent<UnityEngine.UI.ScrollRect>();
                            Check(researcher.SummaryContentVisible() && scroll && scroll.vertical && scroll.content == summary.rectTransform, stage + "/actual summary content and vertical scroll", new { summary.text, summary.preferredHeight, viewportHeight = ((RectTransform)summary.transform.parent).rect.height });
                            if (scroll) { scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases(); Call(owned, "Capture", stage + "_bottom", panel, captures, rows); }
                        }
                        if(liveDelivery && detailsButton)
                        {
                            Click(detailsButton); Check(!researcher.SummaryContentVisible(),stage+"/actual Details closes summary",researcher.SummaryContentVisible());
                            Click(detailsButton);
                            string oldRun=c.Record.run_id;
                            try { c.Record.run_id=oldRun+"_fixture_identity"; Flush(); Check(!researcher.SummaryContentVisible(),stage+"/record identity resets details",researcher.SummaryContentVisible()); }
                            finally { c.Record.run_id=oldRun; Flush(); }
                        }
                        failures.AddRange(Get<List<string>>(owned, "TextOverflows").Select(s => language + "/" + s));
                        Check(identity == c.Record.participant_id + "/" + c.Record.session_id + "/" + c.Record.run_id && c.Registry.List(ParticipantGroup.Test).Count == 1, stage + "/single owned identity retained", identity);
                        rows.Add(new { stage, eventsPath = c.Logger.EventsPath, registry = c.Registry.RegistryPath, export = c.LastExportPath, scopedDesktopProjection = true });
                    }
                    catch (Exception e) { var cause = e is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : e; Check(false, stage + "/execution", cause.ToString()); }
                    finally { (owned as IDisposable)?.Dispose(); }
                }
            }
            finally { Application.logMessageReceived -= onLog; foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            Check(callbackErrors.Count == 0, "no swallowed Unity callbacks", callbackErrors);
            Check(SceneManager.GetActiveScene() == scene && scene.isDirty == dirty && EditorSceneManager.previewSceneCount == previews && UnityEngine.EventSystems.EventSystem.current == previousEvents, "caller scene dirty preview EventSystem preserved", new { dirty, currentDirty = scene.isDirty, previews, currentPreviews = EditorSceneManager.previewSceneCount });
            string report = Path.Combine(dir, "researcher-session-surfaces.json");
            File.WriteAllText(report, JsonConvert.SerializeObject(new { language = language.ToString(), rows, captures, failures, pass = failures.Count == 0,
                scope = "Delivery ACK files are explicitly manufactured in the owned TEST fixture to exercise UI mapping, not proof of real PC receipt. Same Owned TEST registry/writer starts from Task1Ready fixture, then actual participant start-confirm pointer callbacks, real nonterminal Stop and ResearcherSetup operator ending callbacks with persisted registry/logs. Desktop Setup content is camera-projected in owned WorldSpace for actual Unity pixels and unchanged TMP overflow checks; not Display2 native desktop screenshots or in-session Quest access. TH/JA draft, no wearer/language approval, no complete study workflow claim." }, Formatting.Indented));
            if (failures.Count != 0) throw new InvalidOperationException("RESEARCHER_SESSION_SURFACES_FAIL " + report);
            return "RESEARCHER_SESSION_SURFACES_PASS " + report;
        }

        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Instance).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetProperty(name,Instance).GetSetMethod(true).Invoke(o,new[]{value});
        static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Instance).Invoke(o,args);
    }
}
#endif
