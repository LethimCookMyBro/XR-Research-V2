#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using Unity.XR.CoreUtils;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Opt-in isolated Editor geometry/technical callback evidence; never native input or hardware acceptance.
    public static class ResearchSpatialReadabilityChecks
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        public static string RunTutorialAffordances(string evidenceDirectory)
        {
            using var legacyPolicy = ResearchUIPolicy.LegacyRegressionScope(); // exercises the retired panel-drag lesson
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run Tutorial affordance fixtures outside Play Mode.");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("An explicit evidence directory is required.");
            string directory = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "tutorial-affordances.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior Tutorial evidence: " + path);
            var sceneBefore = SceneManager.GetActiveScene(); bool dirtyBefore = sceneBefore.isDirty;
            var checks = new List<object>(); var measurements = new List<object>(); var failures = new List<string>();
            void Check(bool condition, string name)
            {
                checks.Add(new { name, pass = condition });
                if (!condition) failures.Add(name);
            }
            void Case(string name, Action action)
            {
                try { action(); }
                catch (Exception error)
                {
                    var cause = error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error;
                    checks.Add(new { name, pass = false, error = cause.ToString() }); failures.Add(name + ": " + cause.Message);
                }
            }
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            Case("Authored three-language Tutorial affordances", () =>
            {
                Require(donor, "Load canonical TH/JP/EN fonts before Tutorial affordances.");
                foreach (var language in new[] { ParticipantLanguage.English, ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                {
                    var preview = EditorSceneManager.NewPreviewScene();
                    var root = NewObject("TutorialAffordanceFixture_" + language, preview); root.SetActive(false);
                    GameObject activeUI = null;
                    try
                    {
                        var controller = root.AddComponent<ResearchSessionFlowController>();
                        string prefix = language + "/";
                        Set(controller, "Record", null); Set(controller, "SetupLanguage", language);
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                        Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                        var portA = root.AddComponent<ResearchTaskPort>(); portA.taskId = "A"; portA.demoMode = true;
                        var portB = root.AddComponent<ResearchTaskPort>(); portB.taskId = "B"; portB.demoMode = true;
                        controller.taskPorts = new[] { portA, portB };
                        var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                        view.controller = controller; controller.view = view;
                        activeUI = view.gameObject; activeUI.SetActive(false); view.enabled = false; view.transform.SetParent(null, false);
                        var head = NewObject("TutorialAffordanceCamera", preview, typeof(Camera)); head.transform.SetParent(root.transform, false);
                        var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                        camera.stereoTargetEye = StereoTargetEyeMask.None; camera.transform.position = new Vector3(100, 1.65f, 100); view.Head = camera.transform;
                        foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                            typeof(ResearchPanelMotion).GetField("environment", PrivateInstance).SetValue(panel, Array.Empty<Renderer>());
                        var credits = (HashSet<string>)Field(controller, "tutorialEvidence");
                        credits.Clear(); credits.UnionWith(new[] { "reference", "switch" });
                        typeof(ResearchFlowView).GetField("successUntil", PrivateInstance).SetValue(view, float.NegativeInfinity);
                        // A new fixture keeps hidden strings from the previous language out of Canvas rebuilds.
                        var unactivatedBody = (TMP_Text)Field(view, "body");
                        Check(unactivatedBody.isOrthographic, prefix + "Hidden uGUI body uses UI text units before its first activation");
                        view.Refresh(); activeUI.SetActive(true);
                        view.Refresh(); Canvas.ForceUpdateCanvases();
                        var cue = (TMP_Text)Field(view, "referenceTutorialCue"); cue.ForceMeshUpdate(true);
                        var expectedFont = language == ParticipantLanguage.Thai ? donor.thaiFont
                            : language == ParticipantLanguage.Japanese ? donor.japaneseFont : donor.englishFont;
                        Check(controller.Language == language.ToString() && view.Font == expectedFont && cue.font == expectedFont,
                            prefix + "Fresh fixture controller, authored View and Reader cue use the requested language and font");
                        if (language == ParticipantLanguage.Japanese)
                        {
                            var cueLines = cue.textInfo.characterInfo.Take(cue.textInfo.characterCount).Where(c => c.isVisible)
                                .GroupBy(c => c.lineNumber).OrderBy(g => g.Key).Select(g => new
                                {
                                    line = g.Key, text = new string(g.Select(c => c.character).ToArray()), visible_glyphs = g.Count(),
                                    letters_or_digits = g.Count(c => char.IsLetterOrDigit(c.character))
                                }).ToArray();
                            Check(controller.TutorialFact == "reposition" && AuthoredVisible(cue.transform, view.referencePanel.transform)
                                && cueLines.Length > 0 && cueLines.All(line => line.letters_or_digits >= 2),
                                prefix + "Rendered initial reposition Reader cue has at least two letters or digits on every visible line");
                            measurements.Add(new { language = language.ToString(), stage = "expanded_reposition", fact = controller.TutorialFact,
                                actual_text = cue.text, font_size = cue.fontSize, rendered_lines = cueLines, measurement = MeasureText(cue, camera) });
                        }
                        credits.Add("reposition"); view.Refresh(); Canvas.ForceUpdateCanvases(); cue.ForceMeshUpdate(true);
                        string progress = (string)typeof(ResearchFlowView).GetMethod("TutorialProgress", PrivateInstance).Invoke(view, new object[] { 3 });
                        Check(AuthoredVisible(cue.transform, view.referencePanel.transform) && view.referencePanel.GetComponent<Canvas>().enabled,
                            prefix + "Current instruction lives on the visible expanded Reader");
                        var readerProgress = (TMP_Text)Field(view, "referenceTutorialProgress"); readerProgress.ForceMeshUpdate(true);
                        Check(readerProgress.text == progress && AuthoredVisible(readerProgress.transform, view.referencePanel.transform),
                            prefix + "Visible Reader header keeps the actual Step 4 of 5 progress separately from the instruction");
                        int progressIndex = readerProgress.GetParsedText().IndexOf(progress, StringComparison.Ordinal);
                        var progressGlyphs = progressIndex < 0 ? Array.Empty<TMP_CharacterInfo>()
                            : readerProgress.textInfo.characterInfo.Skip(progressIndex).Take(progress.Length).Where(c => c.isVisible).ToArray();
                        float authoredProgressSize = ((TMP_Text)Field(view, "tutorialProgress")).fontSize;
                        Check(progressGlyphs.Length > 0 && progressGlyphs.All(c => c.pointSize >= authoredProgressSize - .1f),
                            prefix + "Rendered Reader progress preserves the existing authored progress glyph size");
                        Check(cue.text.Contains("×") && cue.textInfo.characterInfo.Take(cue.textInfo.characterCount).Any(c => c.character == '×' && c.isVisible),
                            prefix + "Close instruction names the rendered header × glyph");
                        Check(cue.fontSize >= 28 && cue.preferredHeight <= cue.rectTransform.rect.height + 1,
                            prefix + "Reader cue preserves its authored font and fits its actual text area");
                        measurements.Add(new { language = language.ToString(), stage = "expanded_close", fact = controller.TutorialFact,
                            progress, actual_text = cue.text, font_size = cue.fontSize, preferred_height = cue.preferredHeight,
                            rect_height = cue.rectTransform.rect.height, authored_progress_size = authoredProgressSize,
                            actual_progress_min_point_size = progressGlyphs.Length == 0 ? (float?)null : progressGlyphs.Min(c => c.pointSize),
                            measurement = MeasureText(cue, camera) });
                        var scroll = (ScrollRect)Field(view, "scroll"); var referenceBody = (TMP_Text)Field(view, "referenceBody");
                        float readPosition = scroll.verticalNormalizedPosition; int page = referenceBody.pageToDisplay;
                        var retained = credits.ToArray();
                        Set(controller, "ReferenceMinimized", true); view.Refresh(); Canvas.ForceUpdateCanvases();
                        var pill = (TMP_Text)Field(view, "pillText"); pill.ForceMeshUpdate(true);
                        string reopen = ResearchStrings.Get("reference.restore_short", language.ToString());
                        Check(reopen != "reference.restore_short" && pill.text == reopen && pill.font == expectedFont
                            && AuthoredVisible(pill.transform, view.referencePanel.transform),
                            prefix + "Visible minimized pill teaches the localized Open Reference action");
                        Check(pill.textInfo.characterCount > 0 && pill.preferredWidth <= pill.rectTransform.rect.width + 1
                            && pill.preferredHeight <= pill.rectTransform.rect.height + 1, prefix + "Localized restore action fits the existing pill");
                        Check(controller.TutorialFact == "restore" && !view.TutorialActionReady("close") && !cue.gameObject.activeInHierarchy,
                            prefix + "Minimized Reader cannot credit the hidden required Close instruction");
                        Check(controller.Flow.TutorialCompleted == 3 && credits.SetEquals(retained), prefix + "Minimize does not advance or discard practice credit");
                        measurements.Add(new { language = language.ToString(), stage = "minimized", actual_text = pill.text,
                            expected_action = reopen, fact = controller.TutorialFact, pending_step = controller.Flow.TutorialCompleted,
                            measurement = MeasureText(pill, camera) });
                        Set(controller, "ReferenceMinimized", false); view.Refresh(); Canvas.ForceUpdateCanvases();
                        Check(controller.TutorialFact == "close" && controller.Flow.TutorialCompleted == 3 && credits.SetEquals(retained),
                            prefix + "Reexpanded Reader retains the same required action and completed checks");
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - readPosition) < .001f && referenceBody.pageToDisplay == page,
                            prefix + "Reexpansion preserves the actual page and read position");
                        Set(controller, "ReferenceOpen", false); credits.Clear();
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 4));
                        view.Refresh(); Canvas.ForceUpdateCanvases();
                        var body = (TMP_Text)Field(view, "body"); body.ForceMeshUpdate(true);
                        string copy = ResearchStrings.Get("tutorial.examine_difference", language.ToString());
                        string[] tokens = language == ParticipantLanguage.English ? new[] { "task", "point", "Examine", "button" }
                            : language == ParticipantLanguage.Thai ? new[] { "งาน", "ชี้", "ส่องดู", "ปุ่ม" } : new[] { "課題", "指", "よく見る", "ボタン" };
                        Check(tokens.All(token => copy.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) && body.text.Contains(copy),
                            prefix + "Tutorial explains the task-only point-to-part and appearing Examine-button affordance");
                        Check(AuthoredVisible(body.transform, view.mainPanel.transform) && body.preferredHeight <= body.rectTransform.rect.height + 1,
                            prefix + "Current Inspect instruction and the neutral Examine definition remain visible and fit");
                        int lastBodyLine = body.textInfo.characterInfo.Take(body.textInfo.characterCount).Where(c => c.isVisible)
                            .Select(c => c.lineNumber).DefaultIfEmpty(-1).Max();
                        var lastBodyLineGlyphs = body.textInfo.characterInfo.Take(body.textInfo.characterCount)
                            .Where(c => c.isVisible && c.lineNumber == lastBodyLine).ToArray();
                        int lastBodyLineLetters = lastBodyLineGlyphs.Count(c => char.IsLetterOrDigit(c.character));
                        if (language == ParticipantLanguage.Japanese)
                            Check(lastBodyLine >= 0 && lastBodyLineLetters >= 2,
                                prefix + "Rendered Inspect definition last line has at least two letters or digits; punctuation cannot mask an orphan letter");
                        measurements.Add(new { language = language.ToString(), stage = "inspect_definition", actual_text = body.text,
                            required_procedural_tokens = tokens, font_size = body.fontSize, preferred_height = body.preferredHeight,
                            rect_height = body.rectTransform.rect.height, rect_width = body.rectTransform.rect.width,
                            body_margin = new[] { body.margin.x, body.margin.y, body.margin.z, body.margin.w },
                            active_in_hierarchy = body.gameObject.activeInHierarchy,
                            actual_text_visible = AuthoredVisible(body.transform, view.mainPanel.transform),
                            last_rendered_line = lastBodyLine, last_rendered_line_text = new string(lastBodyLineGlyphs.Select(c => c.character).ToArray()),
                            last_rendered_line_visible_glyphs = lastBodyLineGlyphs.Length, last_rendered_line_letters_or_digits = lastBodyLineLetters,
                            measurement = MeasureText(body, camera) });
                        string[] neutral = language == ParticipantLanguage.English
                            ? new[] { "Replace the designated component using the information in Reference.", "The equipment is operating abnormally.\nUse the information in Reference to diagnose and correct the problem." }
                            : language == ParticipantLanguage.Thai
                            ? new[] { "เปลี่ยนชิ้นส่วนที่กำหนดโดยใช้\nข้อมูลในเอกสารอ้างอิง", "อุปกรณ์ทำงานผิดปกติ\nใช้ข้อมูลในเอกสารอ้างอิง\nเพื่อวินิจฉัยและแก้ไขปัญหา" }
                            : new[] { "参照資料の情報を使い、\n指定された部品を交換してください。", "機器の動作に異常があります。\n参照資料の情報を使い、\n問題を診断して修正してください。" };
                        for (int task = 0; task < 2; task++)
                        {
                            Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, task == 0 ? ResearchFlowState.Task1Active : ResearchFlowState.Task2Active, 5));
                            view.Refresh();
                            Check(body.text == neutral[task], prefix + "Task " + (task == 0 ? "A" : "B") + " keeps its pre-feedback neutral instruction verbatim");
                            var taskState = controller.Flow.State; string taskId = controller.Flow.CurrentTask;
                            Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                            view.Refresh(); Canvas.ForceUpdateCanvases();
                            float taskReadPosition = scroll.verticalNormalizedPosition; int taskPage = referenceBody.pageToDisplay;
                            Set(controller, "ReferenceMinimized", true); view.Refresh(); Canvas.ForceUpdateCanvases(); pill.ForceMeshUpdate(true);
                            var pillRect = (RectTransform)view.referencePanel.transform.Find("MinimizedReference");
                            var panelRect = (RectTransform)view.referencePanel.transform;
                            var taskGlyphs = pill.textInfo.characterInfo.Take(pill.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                            bool glyphsFit = taskGlyphs.Length > 0 && taskGlyphs.All(c => new[] { c.bottomLeft, c.topLeft, c.topRight, c.bottomRight }
                                .Select(p => pillRect.InverseTransformPoint(pill.rectTransform.TransformPoint(p)))
                                .All(p => p.x >= pillRect.rect.xMin - 1 && p.x <= pillRect.rect.xMax + 1 && p.y >= pillRect.rect.yMin - 1 && p.y <= pillRect.rect.yMax + 1));
                            Check(pill.text == reopen && pill.font == expectedFont && taskGlyphs.All(c => c.fontAsset == expectedFont)
                                && AuthoredVisible(pill.transform, view.referencePanel.transform)
                                && !pill.isTextOverflowing && glyphsFit && pill.preferredWidth <= pill.rectTransform.rect.width + 1
                                && pill.preferredHeight <= pill.rectTransform.rect.height + 1,
                                prefix + "Task " + taskId + " minimized Reference visibly names its localized reopen action and fits the hit target");
                            Check(pillRect.rect.size == new Vector2(300, 76) && panelRect.rect.size == pillRect.rect.size
                                && pillRect.GetComponent<ResearchUIAction>().command == "reference_restore",
                                prefix + "Task " + taskId + " keeps its existing 300 x 76 restore target inside matching canvas bounds");
                            measurements.Add(new { language = language.ToString(), stage = "task_" + taskId + "_minimized_reference",
                                actual_text = pill.text, expected_action = reopen, glyphs_fit = glyphsFit,
                                controller_language = controller.Language, view_font = view.Font.name, actual_label_font = pill.font.name,
                                hit_size = new[] { pillRect.rect.width, pillRect.rect.height }, measurement = MeasureText(pill, camera) });
                            Set(controller, "ReferenceMinimized", false); view.Refresh(); Canvas.ForceUpdateCanvases();
                            Check(controller.ReferenceOpen && !controller.ReferenceMinimized && controller.ReferenceSection == "user_manual"
                                && controller.Flow.State == taskState && controller.Flow.CurrentTask == taskId && !controller.Flow.Paused
                                && body.text == neutral[task] && Mathf.Abs(scroll.verticalNormalizedPosition - taskReadPosition) < .001f
                                && referenceBody.pageToDisplay == taskPage,
                                prefix + "Task " + taskId + " visual minimize/restore retains task, neutral instruction, format, page and read position");
                            Set(controller, "ReferenceOpen", false); view.Refresh();
                        }
                    }
                    finally { if (activeUI) UnityEngine.Object.DestroyImmediate(activeUI); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
                }
            });
            string callbackResult = null;
            Case("Existing actual Tutorial reference callbacks and active writer", () =>
            {
                callbackResult = ResearchFlowPlayChecks.CheckTutorialReferenceEarlyClose(Path.Combine(directory, "c"));
                Check(callbackResult.StartsWith("EARLY_CLOSE_PASS", StringComparison.Ordinal), "Actual close/minimize/restore callbacks retain checks, enforce visible instructions and complete only after required actions");
            });
            bool preserved = sceneBefore.IsValid() && SceneManager.GetActiveScene() == sceneBefore && sceneBefore.isDirty == dirtyBefore;
            Check(preserved, "Caller active scene and dirty state preserved");
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow.ToString("o"), status = failures.Count == 0 ? "PASS" : "FAIL", checks, measurements, failures, callback_result = callbackResult,
                callback_logs = Directory.GetFiles(directory, "events.jsonl", SearchOption.AllDirectories), scene_before = sceneBefore.path, dirty_before = dirtyBefore, scene_preserved = preserved,
                setup = "Separate owned preview scene and detached authored View per language; inactive technical controller/Record=null is seeded before activation and font pairing is checked. Existing CheckTutorialReferenceEarlyClose supplies separate real Controller callbacks, technical registry and Active writer. Render-state seeding is not physical interaction. No new tutorial facts or approval.",
                copy_scope = "Procedural token checks and actual visible geometry, not human translation approval; Task A/B instruction baseline unchanged.",
                synthetic_input = true, native_stereo_aggregate_exercised = false, hardware_verified = false
            }, Formatting.Indented));
            if (failures.Count != 0) throw new InvalidOperationException("TUTORIAL_AFFORDANCE_FAIL " + path + " | " + string.Join(" | ", failures));
            return "TUTORIAL_AFFORDANCE_PASS " + path + "; three-language authored pixels/geometry and separate existing technical callback checks only";
        }

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run spatial readability fixtures outside Play Mode.");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("An explicit evidence directory is required.");
            string directory = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "spatial-readability.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior spatial evidence: " + path);
            var sceneBefore = SceneManager.GetActiveScene();
            bool dirtyBefore = sceneBefore.isDirty;
            var cases = new List<object>();
            var measurements = new List<object>();
            var failures = new List<string>();
            void Case(string name, Action action)
            {
                try { action(); cases.Add(new { name, pass = true }); }
                catch (Exception error)
                {
                    var cause = error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error;
                    failures.Add(name + ": " + cause.Message);
                    cases.Add(new { name, pass = false, error = cause.ToString() });
                }
            }
            Case("Examine_right_off_view_tries_valid_left", () => ExamineRightAlternative(measurements));
            Case("Examine_both_invalid_hides_and_clears_press", () => ExamineBothInvalid(measurements));
            Case("Examine_info_growth_keeps_visible_full_rect_in_view", () => ExamineInfoGrowth(measurements));
            Case("Actual_authored_TH_JP_near_far_measurements", () => AuthoredMeasurements(measurements, directory));
            Case("Actual_owned_TEST_TH_JP_Stop_ownership", () => AuthoredMeasurements(measurements, directory, true));
            bool scenePreserved = sceneBefore.IsValid() && SceneManager.GetActiveScene() == sceneBefore && sceneBefore.isDirty == dirtyBefore;
            if (!scenePreserved) failures.Add("Caller active scene/dirty state changed.");
            File.WriteAllText(path, JsonConvert.SerializeObject(new
            {
                utc = DateTime.UtcNow.ToString("o"), status = failures.Count == 0 ? "PASS" : "FAIL", cases, measurements, failures,
                setup = "Isolated preview scenes; inactive technical ResearchSessionFlowController with Record=null/no logger, allowed by the existing offline technical guard, owned Task clock started; added Stop case uses external TEST registry and actual Active writer; explicit actual ResearchExamineTarget.OnEnable/Update/OnPointerDown; disabled synthetic monoscopic Camera; active detached authored View with inactive controller and canonical font donor for TMP measurements.",
                scene_before = sceneBefore.path, dirty_before = dirtyBefore, scene_preserved = scenePreserved,
                measurement_policy = "Reports glyph ink/angular/projected pixel height and physical/angular button targets. No readability threshold or controller rule is inferred from hand guidelines.",
                synthetic_input = true, native_stereo_aggregate_exercised = false, hardware_verified = false
            }, Formatting.Indented));
            if (failures.Count != 0) throw new InvalidOperationException("SPATIAL_READABILITY_FAIL " + path + " | " + string.Join(" | ", failures));
            return "SPATIAL_READABILITY_PASS " + path + "; actual Editor geometry/callbacks only, native and wearer excluded";
        }

        static void ExamineRightAlternative(List<object> measurements)
        {
            using (var fixture = new ExamineFixture())
            {
                // At FOV60/aspect1: right x~.90 at z1 is outside; left x~-.10 is inside.
                var bounds = new Bounds(fixture.Camera.transform.position + new Vector3(.40f, 0, 1), new Vector3(.60f, .30f, .10f));
                fixture.SetTargetBounds(bounds);
                float halfWidth = fixture.ActualHalfWidth;
                var expectedLeft = PositionCandidate(fixture.Target, bounds, -fixture.Camera.transform.right, fixture.Camera.transform.forward, halfWidth);
                var expectedRight = PositionCandidate(fixture.Target, bounds, fixture.Camera.transform.right, fixture.Camera.transform.forward, halfWidth);
                Require(!PoseFits(fixture.Canvas.transform as RectTransform, fixture.Camera, expectedRight), "Fixture right candidate must be outside the viewport.");
                Require(PoseFits(fixture.Canvas.transform as RectTransform, fixture.Camera, expectedLeft), "Fixture left candidate must be a valid alternative.");
                fixture.UpdateTarget();
                var actual = fixture.Canvas.transform.position;
                measurements.Add(new { name = "right_alternative", actual_half_width_m = halfWidth, expected_right = XYZ(expectedRight), expected_left = XYZ(expectedLeft), actual = XYZ(actual), canvas_enabled = fixture.Canvas.enabled,
                    button = MeasureButton(fixture.Button, fixture.Camera) });
                Require(Vector3.Distance(actual, expectedLeft) < .001f, "Examine accepted an off-viewport right candidate instead of the valid left candidate.");
                Require(fixture.Canvas.enabled && fixture.Button.IsInteractable(), "Valid alternative must remain visible and interactive.");
            }
        }

        static void ExamineBothInvalid(List<object> measurements)
        {
            using (var fixture = new ExamineFixture())
            {
                fixture.SetTargetBounds(new Bounds(fixture.Camera.transform.position + Vector3.forward, new Vector3(.2f, .2f, .1f)));
                fixture.UpdateTarget();
                Require(fixture.Canvas.enabled && fixture.Target.ChipVisible, "Fixture must first acquire a visible task chip.");
                fixture.Target.OnPointerDown(new PointerEventData(null) { pointerId = -921177 });
                Require(fixture.PendingPresses == 1, "A real synthetic pointer-down must seed exactly one pending press before concealment.");
                var bounds = new Bounds(fixture.Camera.transform.position - Vector3.forward, new Vector3(.2f, .2f, .1f));
                fixture.SetTargetBounds(bounds);
                var right = PositionCandidate(fixture.Target, bounds, fixture.Camera.transform.right, fixture.Camera.transform.forward, fixture.ActualHalfWidth);
                var left = PositionCandidate(fixture.Target, bounds, -fixture.Camera.transform.right, fixture.Camera.transform.forward, fixture.ActualHalfWidth);
                Require(!PoseFits(fixture.Canvas.transform as RectTransform, fixture.Camera, right) && !PoseFits(fixture.Canvas.transform as RectTransform, fixture.Camera, left),
                    "Both fixture candidates must be invalid before the production callback.");
                fixture.UpdateTarget();
                bool hidden = !fixture.Canvas.enabled && !fixture.Group.blocksRaycasts && !fixture.Group.interactable && !fixture.Button.IsInteractable();
                measurements.Add(new { name = "both_invalid", canvas_enabled = fixture.Canvas.enabled, group_alpha = fixture.Group.alpha,
                    blocks_raycasts = fixture.Group.blocksRaycasts, interactable = fixture.Button.IsInteractable(), pending_presses = fixture.PendingPresses,
                    actual = XYZ(fixture.Canvas.transform.position) });
                Require(hidden, "Examine with no valid candidate retained a visible/interactable invalid fallback.");
                Require(fixture.PendingPresses == 0, "Examine concealment retained the acquired pending click.");

                fixture.SetTargetBounds(new Bounds(fixture.Camera.transform.position + Vector3.forward, new Vector3(.2f, .2f, .1f)));
                fixture.UpdateTarget();
                Require(fixture.Canvas.enabled && fixture.Target.ChipVisible && fixture.Group.alpha > .99f
                    && fixture.Group.blocksRaycasts && fixture.Group.interactable && fixture.Button.IsInteractable(),
                    "A newly valid candidate did not restore the hidden Examine chip on the next Update.");
                Require(PoseFits(fixture.Canvas.transform as RectTransform, fixture.Camera, fixture.Canvas.transform.position),
                    "The restored Examine chip is outside the actual fixture viewport.");
                Require(fixture.PendingPresses == 0, "Restoration revived the old pending press.");
                fixture.Target.OnPointerUp(new PointerEventData(null) { pointerId = -921177 });
                fixture.Button.onClick.Invoke();
                Require((int)Field(fixture.Target, "pressedPointer") == int.MinValue && Field(fixture.Target, "releasedContext") == null
                    && Field(fixture.Target, "observedInformation") == null,
                    "An old release/click after restoration acquired or displayed Examine information.");
                fixture.Target.OnPointerDown(new PointerEventData(null) { pointerId = -921177 });
                Require(fixture.PendingPresses == 1, "The restored chip cannot acquire a new pointer press.");
                fixture.Target.OnPointerUp(new PointerEventData(null) { pointerId = -921177 });
                Require(fixture.PendingPresses == 0 && (int)Field(fixture.Target, "pressedPointer") == -921177,
                    "The restored chip cannot release a newly acquired pointer press.");
                measurements.Add(new { name = "both_invalid_then_valid_restored", canvas_enabled = fixture.Canvas.enabled,
                    group_alpha = fixture.Group.alpha, blocks_raycasts = fixture.Group.blocksRaycasts, interactable = fixture.Button.IsInteractable(),
                    stale_release_accepted = false, fresh_press_released = true, pending_presses = fixture.PendingPresses,
                    button = MeasureButton(fixture.Button, fixture.Camera) });
            }
        }

        static void AuthoredMeasurements(List<object> measurements, string evidenceDirectory, bool ownedStopSession = false)
        {
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            Require(donor, "Load the canonical ResearchFlow scene with TH/JP/EN fonts before authored measurements.");
            var preview = EditorSceneManager.NewPreviewScene();
            var root = NewObject("ReadabilityAuthoredFixture", preview); root.SetActive(false);
            GameObject activeUI = null;
            try
            {
                int measuredTextRows = 0;
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var portA = root.AddComponent<ResearchTaskPort>(); portA.taskId = "A"; portA.demoMode = true;
                var portB = root.AddComponent<ResearchTaskPort>(); portB.taskId = "B"; portB.demoMode = true;
                controller.taskPorts = new[] { portA, portB };
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.controller = controller; controller.view = view;
                // TMP executes its Editor lifecycle only on an active hierarchy. Keep the controller inactive.
                activeUI = view.gameObject; view.enabled = false; view.transform.SetParent(null, false); activeUI.SetActive(true);
                var head = NewObject("ReadabilityMeasurementCamera", preview, typeof(Camera));
                head.transform.SetParent(root.transform, false);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                camera.stereoTargetEye = StereoTargetEyeMask.None; camera.transform.position = new Vector3(100, 1.65f, 100);
                view.Head = camera.transform;
                ((ResearchTaskTiming)Field(controller, "taskTiming")).Start(0);
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                {
                    string ownedPath = Path.Combine(evidenceDirectory, "owned-stop-" + language);
                    ResearchParticipantRegistry registry = null; ParticipantRecord record = null;
                    if (ownedStopSession)
                    {
                        if (Directory.Exists(ownedPath)) throw new IOException("Fresh owned TEST registry required.");
                        registry = new ResearchParticipantRegistry(Path.Combine(ownedPath, "registry"));
                        var created = registry.CreateNext(ParticipantGroup.Test);
                        record = registry.StartOrResume(created.participant_id, language, ExperimentInputMode.Controller, TaskOrder.AB);
                    }
                    using var writer = ownedStopSession ? new HandsLabLogger(Path.Combine(ownedPath, "writer"), "None", "Stopped Editor spatial Stop ownership",
                        executionEnvironment: "Editor", technicalTest: true, testData: true, runId: record.run_id) : null;
                    Set(controller, "Registry", registry); Set(controller, "Logger", writer);
                    Set(controller, "Record", record); Set(controller, "SetupLanguage", language);
                    Require(!ownedStopSession || writer.State == HandsLabLogState.Active, "Owned Stop fixture requires an actual Active TEST writer.");
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceSection", "");
                    view.Refresh(); Canvas.ForceUpdateCanvases();
                    ReferenceUtilityTabSeparation(view, language, "Task_neutral_choice", measurements);
                    Set(controller, "ReferenceSection", "user_manual");
                    view.Refresh();
                    Canvas.ForceUpdateCanvases();
                    ReferenceUtilityTabSeparation(view, language, "Task_selected_manual", measurements);
                    foreach (var distance in new[] { 1.2f, 2f })
                    {
                        foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                        {
                            float angle = panel == view.referencePanel ? -20 : 28;
                            var direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                            panel.transform.SetPositionAndRotation(camera.transform.position + direction * distance + Vector3.down * .1f,
                                Quaternion.LookRotation(direction, Vector3.up));
                            int panelGlyphRows = 0;
                            foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true).Where(t => AuthoredVisible(t.transform, panel.transform) && !string.IsNullOrWhiteSpace(t.text)))
                            {
                                var measurement = MeasureText(text, camera);
                                if ((int)JObject.FromObject(measurement)["measured_glyph_count"] > 0) { measuredTextRows++; panelGlyphRows++; }
                                measurements.Add(new { name = "authored_text", language = language.ToString(), panel = panel.panelId, synthetic_distance_m = distance, measurement });
                            }
                            foreach (var button in panel.GetComponentsInChildren<Button>(true).Where(b => AuthoredVisible(b.transform, panel.transform)))
                                measurements.Add(new { name = "authored_button", language = language.ToString(), panel = panel.panelId, synthetic_distance_m = distance, measurement = MeasureButton(button, camera) });
                            Require(panelGlyphRows > 0, "Actual visible glyph metrics required for " + language + "/" + panel.panelId + "/" + distance + "m.");
                        }
                    }
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                    view.Refresh(); Canvas.ForceUpdateCanvases();
                    ReferenceUtilityTabSeparation(view, language, "Tutorial_integrated_reader", measurements);
                }
                Require(measuredTextRows >= 4, "Authored TH/JP near/far fixture must produce actual visible glyph metrics, not empty rows.");
            }
            finally { if (activeUI) UnityEngine.Object.DestroyImmediate(activeUI); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void ExamineInfoGrowth(List<object> measurements)
        {
            using (var fixture = new ExamineFixture(withLabel: true))
            {
                int examineEmissions = 0;
                fixture.Target.task.Behavior += (kind, target, before, after) => { if (kind == "EXAMINE") examineEmissions++; };
                Set(fixture.Target.controller, "SetupLanguage", ParticipantLanguage.Thai);
                var targetBounds = new Bounds(fixture.Camera.transform.position + new Vector3(0, .36f, 1), new Vector3(.2f, .2f, .1f));
                fixture.SetTargetBounds(targetBounds);
                fixture.UpdateTarget();
                var rect = (RectTransform)fixture.Canvas.transform;
                Require(fixture.Target.ChipVisible && PoseFits(rect, fixture.Camera, rect.position),
                    "Info-growth fixture must first expose its actual small chip wholly inside the viewport.");
                measurements.Add(new { name = "examine_info_growth", stage = "before_click", canvas_enabled = fixture.Canvas.enabled,
                    size = new[] { rect.rect.width, rect.rect.height }, full_rect_fits = true, synthetic_input = true,
                    examine_emission_count = examineEmissions, input_source = fixture.Target.controller.InputSourceForPointer(-921177), button = MeasureButton(fixture.Button, fixture.Camera) });
                Require(((ResearchTaskTiming)Field(fixture.Target.controller, "taskTiming")).CanManipulate,
                    "Info-growth synthetic task must have started its own clock before acquiring input.");
                fixture.Target.OnPointerDown(new PointerEventData(null) { pointerId = -921177 });
                Require(fixture.PendingPresses == 1, "Info growth must start from a genuinely acquired synthetic pointer press.");
                fixture.Target.OnPointerUp(new PointerEventData(null) { pointerId = -921177 });
                Require((int)Field(fixture.Target, "pressedPointer") == -921177, "Info-growth pointer release was not acquired.");
                fixture.Button.onClick.Invoke();
                bool infoDisplayed = !string.IsNullOrWhiteSpace(Field(fixture.Target, "observedInformation") as string);
                bool callbackVisible = fixture.Canvas.enabled;
                bool callbackFits = PoseFits(rect, fixture.Camera, rect.position);
                int acceptedCallbackEmissions = examineEmissions;
                bool callbackPressConsumed = fixture.PendingPresses == 0;
                bool hiddenCallbackSafe = callbackVisible || (callbackPressConsumed && !fixture.Group.blocksRaycasts
                    && !fixture.Group.interactable && !fixture.Button.IsInteractable() && !infoDisplayed);
                measurements.Add(new { name = "examine_info_growth", stage = "immediately_after_actual_click_callback",
                    canvas_enabled = callbackVisible, info_displayed = infoDisplayed, size = new[] { rect.rect.width, rect.rect.height },
                    examine_emission_count = examineEmissions, hidden_callback_safe = hiddenCallbackSafe,
                    full_rect_fits = callbackFits, pending_presses = fixture.PendingPresses, button = MeasureButton(fixture.Button, fixture.Camera) });
                bool validLowerCandidate = false;
                var savedSize = rect.sizeDelta; var savedPosition = rect.position; var savedRotation = rect.rotation;
                try
                {
                    // Probe the actual requested info footprint, beside the same target; no head/state injection.
                    rect.sizeDelta = new Vector2(340, 220);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        var candidate = PositionCandidate(fixture.Target, targetBounds, fixture.Camera.transform.right * side,
                            fixture.Camera.transform.forward, fixture.ActualHalfWidth) + Vector3.down * .20f;
                        rect.SetPositionAndRotation(candidate, Quaternion.LookRotation(candidate - fixture.Camera.transform.position, Vector3.up));
                        bool independentFits = PoseFits(rect, fixture.Camera, candidate);
                        bool productionReadable = (bool)typeof(ResearchExamineTarget).GetMethod("ReadablePlacement", PrivateInstance)
                            .Invoke(fixture.Target, new object[] { fixture.Camera, rect });
                        validLowerCandidate |= independentFits && productionReadable;
                        measurements.Add(new { name = "examine_info_growth_lower_candidate", side = side > 0 ? "right" : "left",
                            actual_full_size = new[] { rect.rect.width, rect.rect.height }, world_shift_y_m = -.20f, position = XYZ(candidate),
                            full_rect_fits = independentFits, production_readable_placement = productionReadable,
                            scope = "Same actual fixture target and production viewport/panel-overlap predicate; fixture View has no overlapping task panels.",
                            button = MeasureButton(fixture.Button, fixture.Camera) });
                    }
                }
                finally { rect.sizeDelta = savedSize; rect.SetPositionAndRotation(savedPosition, savedRotation); }
                fixture.Button.onClick.Invoke(); // An already consumed acquisition must not revive the function.
                fixture.UpdateTarget();
                bool nextFits = PoseFits(rect, fixture.Camera, rect.position);
                bool nextInfoDisplayed = !string.IsNullOrWhiteSpace(Field(fixture.Target, "observedInformation") as string);
                measurements.Add(new { name = "examine_info_growth", stage = "after_next_actual_Update", canvas_enabled = fixture.Canvas.enabled,
                    size = new[] { rect.rect.width, rect.rect.height }, full_rect_fits = nextFits,
                    examine_emission_count = examineEmissions, info_displayed = nextInfoDisplayed,
                    pending_presses = fixture.PendingPresses, button = MeasureButton(fixture.Button, fixture.Camera) });
                Require(acceptedCallbackEmissions == 1 && callbackPressConsumed,
                    "The actual synthetic callback must emit exactly one EXAMINE and consume its acquired press; incidental fixture failure.");
                Require(!callbackVisible || callbackFits, "Examine info growth retained a visible full rectangle outside the viewport immediately after the click callback.");
                Require(!fixture.Canvas.enabled || nextFits, "The next actual Update retained an invalid visible Examine rectangle.");
                Require(hiddenCallbackSafe, "A concealed info-growth callback retained input acquisition or interactive information.");
                Require(examineEmissions == 1 && fixture.PendingPresses == 0
                    && (int)Field(fixture.Target, "pressedPointer") == int.MinValue && Field(fixture.Target, "releasedContext") == null
                    && (callbackVisible || !nextInfoDisplayed), "Consumed or concealed Examine acquisition revived its function/information.");
                Require(!validLowerCandidate || (callbackVisible && infoDisplayed && callbackFits),
                    "Examine concealed requested information although a contextual lower beside-target candidate is readable.");
            }
        }

        static void ReferenceUtilityTabSeparation(ResearchFlowView view, ParticipantLanguage language, string context, List<object> measurements)
        {
            var panel = (RectTransform)view.referencePanel.transform;
            var visible = panel.GetComponentsInChildren<Button>(true).Where(b => AuthoredVisible(b.transform, panel)).ToArray();
            var utilities = visible.Where(b => b.name == "Pause" || b.name == "Stop" || b.name == "Minimize" || b.name == "Close").ToArray();
            var tabs = visible.Where(b => b.name.StartsWith("Tab_", StringComparison.Ordinal)).ToArray();
            bool tutorial = context == "Tutorial_integrated_reader";
            Require(tutorial || context == "Task_neutral_choice" || context == "Task_selected_manual", "Unknown separation fixture context.");
            bool session = view.controller.Record != null;
            Require(view.controller.ParticipantCanStop == session, "Stop eligibility must match this explicit owned-session fixture.");
            // Owner 20 Oct: the Reader offers Pause only; an eligible Stop waits on the pause card.
            var expectedUtilities = new[] { "Pause", "Minimize", "Close" };
            Require(utilities.Length == expectedUtilities.Length && new HashSet<string>(utilities.Select(b => b.name)).SetEquals(expectedUtilities),
                "Actual open readable Reference utilities must own Pause and show no Stop: " + language + "/" + context);
            Require(tabs.Length == 4 && new HashSet<string>(tabs.Select(b => b.GetComponent<ResearchUIAction>().command))
                .SetEquals(ResearchReferenceScheme.Active.Select(section => "reference_tab." + section)),
                "All four distinct format identities remain visible, including unavailable formats: " + language + "/" + context);
            var mainPanel = (RectTransform)view.mainPanel.transform;
            var mainPauses = mainPanel.GetComponentsInChildren<Button>(true).Where(b => AuthoredVisible(b.transform, mainPanel)
                && b.GetComponent<ResearchUIAction>() && b.GetComponent<ResearchUIAction>().command == "pause").ToArray();
            Require(mainPauses.Length == 0 && utilities.Count(b => b.name == "Pause") == 1,
                "Only the actual readable owner exposes Pause: " + language + "/" + context);
            var mainStops = mainPanel.GetComponentsInChildren<Button>(true).Where(b => AuthoredVisible(b.transform, mainPanel)
                && b.GetComponent<ResearchUIAction>() && b.GetComponent<ResearchUIAction>().command == "stop").ToArray();
            Require(mainStops.Length == 0 && utilities.Count(b => b.name == "Stop") == 0,
                "Neither the readable Reader nor the Task card exposes Stop during a Task: " + language + "/" + context);
            measurements.Add(new { name = "utility_ownership", language = language.ToString(), context, owned_TEST_session = session,
                main_pause_count = mainPauses.Length, reference_pause_count = utilities.Count(b => b.name == "Pause"),
                main_stop_count = mainStops.Length, reference_stop_count = utilities.Count(b => b.name == "Stop") });
            foreach (var utility in utilities.Concat(mainPauses))
            {
                var buttonRect = (RectTransform)utility.transform;
                var ownerPanel = mainPauses.Contains(utility) ? mainPanel : panel;
                var icon = utility.transform.Find("UtilityIcon") as RectTransform;
                Require(icon && AuthoredVisible(icon, ownerPanel), "Active utility needs its actual icon: " + utility.name);
                var iconBounds = BoundsInPanel(icon, buttonRect);
                Require(iconBounds.xMin >= buttonRect.rect.xMin - .5f && iconBounds.xMax <= buttonRect.rect.xMax + .5f
                    && iconBounds.yMin >= buttonRect.rect.yMin - .5f && iconBounds.yMax <= buttonRect.rect.yMax + .5f,
                    "Actual visible utility icon exceeds its button bounds: " + language + "/" + context + "/" + utility.name);
                var caption = utility.transform.Find("Label")?.GetComponent<TMP_Text>();
                bool pause = utility.GetComponent<ResearchUIAction>().command == "pause";
                bool stop = utility.GetComponent<ResearchUIAction>().command == "stop";
                Rect captionBounds = default;
                if (pause || stop)
                {
                    Require(caption && AuthoredVisible(caption.transform, ownerPanel)
                        && caption.text == ResearchStrings.Get(stop ? "action.stop_short" : "action.pause_caption", language.ToString()),
                        "The single Pause owner keeps its visible localized caption: " + language + "/" + context);
                    caption.ForceMeshUpdate(true); captionBounds = BoundsInPanel(caption.rectTransform, buttonRect);
                    Require(captionBounds.xMin >= buttonRect.rect.xMin - .5f && captionBounds.xMax <= buttonRect.rect.xMax + .5f
                        && captionBounds.yMin >= buttonRect.rect.yMin - .5f && captionBounds.yMax <= buttonRect.rect.yMax + .5f
                        && caption.preferredWidth <= caption.rectTransform.rect.width + 1 && caption.preferredHeight <= caption.rectTransform.rect.height + 1,
                        "Actual Pause caption must fit its active target: " + language + "/" + context);
                    Require(!iconBounds.Overlaps(captionBounds), "Active Pause caption and icon rectangles overlap: " + language + "/" + context);
                }
                else Require(!caption || !AuthoredVisible(caption.transform, ownerPanel), "Icon utility retained an unintended active caption: " + utility.name);
                measurements.Add(new { name = "active_utility_bounds", language = language.ToString(), context, utility = utility.name,
                    owner_panel = ownerPanel.name, pause_caption = pause ? caption.text : null, stop_caption = stop ? caption.text : null,
                    icon_bounds = new[] { iconBounds.xMin, iconBounds.yMin, iconBounds.xMax, iconBounds.yMax },
                    caption_bounds = pause ? new[] { captionBounds.xMin, captionBounds.yMin, captionBounds.xMax, captionBounds.yMax } : null });
            }
            foreach (var utility in utilities)
                foreach (var tab in tabs)
                {
                    var utilityBounds = BoundsInPanel((RectTransform)utility.transform, panel);
                    var tabBounds = BoundsInPanel((RectTransform)tab.transform, panel);
                    float xOverlap = Mathf.Min(utilityBounds.xMax, tabBounds.xMax) - Mathf.Max(utilityBounds.xMin, tabBounds.xMin);
                    float yOverlap = Mathf.Min(utilityBounds.yMax, tabBounds.yMax) - Mathf.Max(utilityBounds.yMin, tabBounds.yMin);
                    bool overlaps = utilityBounds.Overlaps(tabBounds);
                    measurements.Add(new { name = "reference_control_separation", language = language.ToString(), context,
                        utility = utility.name, tab = tab.name, tab_interactable = tab.IsInteractable(), overlaps,
                        x_overlap_panel_units = xOverlap, y_overlap_panel_units = yOverlap,
                        utility_bounds = new[] { utilityBounds.xMin, utilityBounds.yMin, utilityBounds.xMax, utilityBounds.yMax },
                        tab_bounds = new[] { tabBounds.xMin, tabBounds.yMin, tabBounds.xMax, tabBounds.yMax } });
                    Require(!overlaps, "Reference header utility overlaps its format tab: " + language + "/" + context
                        + "/" + utility.name + "/" + tab.name + " overlap=" + xOverlap + "x" + yOverlap + " panel units.");
                }
        }

        static Rect BoundsInPanel(RectTransform rect, RectTransform panel)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var first = panel.InverseTransformPoint(corners[0]);
            float xMin = first.x, xMax = first.x, yMin = first.y, yMax = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                var point = panel.InverseTransformPoint(corners[i]);
                xMin = Mathf.Min(xMin, point.x); xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y); yMax = Mathf.Max(yMax, point.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // Uses actual TMP glyph metrics scaled by the parsed character, excluding the SDF quad padding.
        // Combining marks/punctuation are reported separately; no per-mark body-size threshold is imposed.
        public static object MeasureText(TMP_Text text, Camera camera)
        {
            if (!text || !camera) throw new ArgumentException("Actual TMP text and camera are required.");
            text.ForceMeshUpdate(true, true);
            var glyphs = new List<object>();
            foreach (var eye in Eyes(camera))
            {
                var eyePosition = EyePosition(camera, eye);
                for (int i = 0; i < text.textInfo.characterCount; i++)
                {
                    var character = text.textInfo.characterInfo[i];
                    if (!character.isVisible || character.textElement?.glyph == null || character.elementType != TMP_TextElementType.Character) continue;
                    if (text.overflowMode == TextOverflowModes.Page && character.pageNumber != text.pageToDisplay - 1) continue;
                    var glyph = character.alternativeGlyph ?? character.textElement.glyph;
                    var metrics = glyph.metrics;
                    float height = metrics.height * character.scale;
                    float x = character.origin + (metrics.horizontalBearingX + metrics.width / 2) * character.scale;
                    float y = character.baseLine + metrics.horizontalBearingY * character.scale;
                    var top = text.transform.TransformPoint(new Vector3(x, y, 0));
                    var bottom = text.transform.TransformPoint(new Vector3(x, y - height, 0));
                    var a = camera.WorldToViewportPoint(top, eye); var b = camera.WorldToViewportPoint(bottom, eye);
                    var category = CharUnicodeInfo.GetUnicodeCategory(character.character);
                    glyphs.Add(new
                    {
                        index = character.index, character = character.character.ToString(), category = category.ToString(), base_letter_or_digit = char.IsLetterOrDigit(character.character),
                        font = character.fontAsset ? character.fontAsset.name : null, parsed_point_size = character.pointSize, eye = eye.ToString(),
                        ink_world_height_m = Vector3.Distance(top, bottom), angular_height_deg = Vector3.Angle(top - eyePosition, bottom - eyePosition),
                        projected_pixel_height = Mathf.Abs(a.y - b.y) * camera.pixelHeight,
                        viewport_top = XYZ(a), viewport_bottom = XYZ(b), projected_finite_and_in_front = Finite(a) && Finite(b) && a.z > 0 && b.z > 0
                    });
                }
            }
            return new { text_name = text.name, font = text.font ? text.font.name : null, authored_font_size = text.fontSize, page = text.pageToDisplay,
                camera_stereo_enabled = camera.stereoEnabled, camera_pixel_width = camera.pixelWidth, camera_pixel_height = camera.pixelHeight,
                eye_pixel_scope = camera.stereoEnabled ? "Camera.pixelHeight reported; confirm native eye texture dimensions independently before physical pixel conclusions" : "Actual synthetic mono Camera.pixelHeight",
                glyph_ink_model = "Glyph metrics height/bearing times TMP_CharacterInfo.scale; excludes SDF quad padding, not a measurement of retinal optical sharpness", measured_glyph_count = glyphs.Count, glyphs };
        }

        public static object MeasureButton(Button button, Camera camera)
        {
            if (!button || !camera) throw new ArgumentException("Actual Button and camera are required.");
            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
            var rows = new List<object>();
            foreach (var eye in Eyes(camera))
            {
                var origin = EyePosition(camera, eye);
                var viewport = corners.Select(point => camera.WorldToViewportPoint(point, eye)).ToArray();
                rows.Add(new { eye = eye.ToString(), physical_width_m = Vector3.Distance(corners[0], corners[3]), physical_height_m = Vector3.Distance(corners[0], corners[1]),
                    angular_width_deg = Vector3.Angle((corners[0] + corners[1]) / 2 - origin, (corners[2] + corners[3]) / 2 - origin),
                    angular_height_deg = Vector3.Angle((corners[0] + corners[3]) / 2 - origin, (corners[1] + corners[2]) / 2 - origin),
                    viewport_corners = viewport.Select(XYZ).ToArray(), all_finite_and_inside_2_percent_margin = viewport.All(InView) });
            }
            return new { button_name = button.name, interactable = button.IsInteractable(), camera_stereo_enabled = camera.stereoEnabled,
                world_corners = corners.Select(XYZ).ToArray(), eyes = rows, threshold_applied = false };
        }

        sealed class ExamineFixture : IDisposable
        {
            readonly Scene scene;
            readonly GameObject root, head, chip, physical;
            readonly object oldFocus, oldRays;
            readonly IList targets;
            readonly object[] oldTargets;
            public readonly ResearchExamineTarget Target;
            public readonly Camera Camera;
            public readonly Canvas Canvas;
            public readonly CanvasGroup Group;
            public readonly Button Button;
            readonly BoxCollider physicalCollider;
            public int PendingPresses => ((IDictionary)Field(Target, "presses")).Count;
            public float ActualHalfWidth => ((RectTransform)Canvas.transform).rect.width * Mathf.Abs(Canvas.transform.lossyScale.x) / 2;

            public ExamineFixture(bool withLabel = false)
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = NewObject("ExamineLayoutInactiveController", scene); root.SetActive(false);
                head = NewObject("ExamineLayoutSyntheticCamera", scene, typeof(Camera));
                Camera = head.GetComponent<Camera>(); Camera.enabled = false; Camera.fieldOfView = 60; Camera.aspect = 1; Camera.nearClipPlane = .05f;
                Camera.stereoTargetEye = StereoTargetEyeMask.None; head.transform.position = new Vector3(100, 1.65f, 100);
                var controller = root.AddComponent<ResearchSessionFlowController>();
                controller.origin = root.AddComponent<XROrigin>(); controller.origin.Camera = Camera;
                var view = root.AddComponent<ResearchFlowView>(); controller.view = view; view.controller = controller; view.Head = head.transform;
                if (withLabel)
                {
                    var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
                    Require(donor, "Load canonical TH/JP/EN fonts for the actual Examine label fixture.");
                    view.thaiFont = donor.thaiFont; view.japaneseFont = donor.japaneseFont; view.englishFont = donor.englishFont;
                }
                var port = root.AddComponent<ResearchTaskPort>(); port.demoMode = true; port.taskId = "A"; port.SetInteractionAllowed(true);
                Set(controller, "Record", null); // Existing isolated technical no-session path; no fictional Active writer.
                Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                ((ResearchTaskTiming)Field(controller, "taskTiming")).Start(0);
                chip = NewObject("ExamineLayoutChip", scene, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image), typeof(Button)); chip.SetActive(false);
                Canvas = chip.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.WorldSpace; Canvas.worldCamera = Camera;
                chip.transform.localScale = Vector3.one * .001f; Group = chip.GetComponent<CanvasGroup>(); Button = chip.GetComponent<Button>(); Button.targetGraphic = chip.GetComponent<Image>();
                if (withLabel)
                {
                    var labelRoot = NewObject("Label", scene, typeof(RectTransform), typeof(TextMeshProUGUI)); labelRoot.transform.SetParent(chip.transform, false);
                    var actualLabel = labelRoot.GetComponent<TextMeshProUGUI>(); actualLabel.font = view.thaiFont; actualLabel.raycastTarget = false;
                }
                physical = NewObject("ExamineLayoutPhysicalBounds", scene, typeof(BoxCollider)); physicalCollider = physical.GetComponent<BoxCollider>();
                oldFocus = StaticField("focused").GetValue(null); oldRays = StaticField("rays").GetValue(null);
                targets = (IList)StaticField("Targets").GetValue(null); oldTargets = targets.Cast<object>().ToArray();
                Target = chip.AddComponent<ResearchExamineTarget>(); Target.controller = controller; Target.task = port; Target.stableTarget = "A.module";
                Target.stationCanvas = Canvas; Target.stationGroup = Group; Target.ConfigureFocus(new Collider[] { physicalCollider }, physical.transform);
                chip.SetActive(true);
                // This non-ExecuteAlways component does not receive OnEnable merely by activation in EditMode.
                Invoke(Target, "OnEnable");
                // Only the isolated fixture is focused. Skip global UpdateFocus discovery, then execute the real consumer Update.
                targets.Clear(); targets.Add(null); targets.Add(Target); StaticField("focused").SetValue(null, Target);
                Invoke(Target, "SetVisible", true);
                Require(Target.Available, "Task/target setup must be genuinely available to the production callback.");
            }
            public void SetTargetBounds(Bounds bounds) { physical.transform.position = bounds.center; physicalCollider.size = bounds.size; Physics.SyncTransforms(); }
            public void UpdateTarget() => Invoke(Target, "Update");
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(chip); UnityEngine.Object.DestroyImmediate(physical); UnityEngine.Object.DestroyImmediate(head); UnityEngine.Object.DestroyImmediate(root);
                targets.Clear(); foreach (var target in oldTargets) targets.Add(target);
                StaticField("focused").SetValue(null, oldFocus); StaticField("rays").SetValue(null, oldRays);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Vector3 PositionCandidate(ResearchExamineTarget target, Bounds bounds, Vector3 right, Vector3 forward, float halfWidth)
            => (Vector3)typeof(ResearchExamineTarget).GetMethod("PositionBesideBounds", PrivateStatic).Invoke(null,
                new object[] { bounds, right, forward, halfWidth, (Vector3)Field(target, "chipOffset") });
        static bool PoseFits(RectTransform rect, Camera camera, Vector3 position)
        {
            var rotation = Quaternion.LookRotation(position - camera.transform.position, Vector3.up);
            var local = rect.rect;
            foreach (var eye in Eyes(camera))
                foreach (float x in new[] { local.xMin, local.xMax })
                    foreach (float y in new[] { local.yMin, local.yMax })
                        if (!InView(camera.WorldToViewportPoint(position + rotation * Vector3.Scale(new Vector3(x, y, 0), rect.lossyScale), eye))) return false;
            return true;
        }
        static Camera.MonoOrStereoscopicEye[] Eyes(Camera camera) => camera.stereoEnabled
            ? new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right } : new[] { Camera.MonoOrStereoscopicEye.Mono };
        static Vector3 EyePosition(Camera camera, Camera.MonoOrStereoscopicEye eye) => eye == Camera.MonoOrStereoscopicEye.Mono
            ? camera.transform.position : camera.GetStereoViewMatrix(eye == Camera.MonoOrStereoscopicEye.Left ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right).inverse.MultiplyPoint(Vector3.zero);
        static bool InView(Vector3 value) => Finite(value) && value.z > 0 && value.x >= .02f && value.x <= .98f && value.y >= .02f && value.y <= .98f;
        static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        static float[] XYZ(Vector3 value) => new[] { value.x, value.y, value.z };
        static bool AuthoredVisible(Transform value, Transform panel)
        { for (var current = value; current && current != panel; current = current.parent) if (!current.gameObject.activeSelf) return false; return panel.gameObject.activeSelf; }
        static GameObject NewObject(string name, Scene scene, params Type[] components)
        { var result = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(result, scene); return result; }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Set(object instance, string property, object value) => instance.GetType().GetProperty(property).GetSetMethod(true).Invoke(instance, new[] { value });
        static object Field(object instance, string name) => instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
        static FieldInfo StaticField(string name) => typeof(ResearchExamineTarget).GetField(name, PrivateStatic);
        static void Invoke(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, PrivateInstance).Invoke(instance, args);
    }
}
#endif
