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
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFocusedOperatorUIChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string RunCompletionTypography(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Completion typography fixture requires stopped Play Mode and no running XR subsystem.");
            string directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "completion-typography.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior typography evidence: " + path);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the authored ResearchFlow scene with all three fonts.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty;
            int previews = EditorSceneManager.previewSceneCount;
            var cursorColors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed)
            { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
            {
                object fixture = null;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                    var controller = (ResearchSessionFlowController)type.GetField("Controller").GetValue(fixture);
                    var view = (ResearchFlowView)type.GetField("View").GetValue(fixture);
                    typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true).Invoke(controller,
                        new object[] { new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.ParticipantComplete, 5) });
                    type.GetMethod("Refresh").Invoke(fixture, null);
                    var body = (TMP_Text)typeof(ResearchFlowView).GetField("body", Private).GetValue(view);
                    Canvas.ForceUpdateCanvases(); body.ForceMeshUpdate(true, true);
                    string literal = ResearchStrings.Get("completion.body", language.ToString());
                    Check(body.GetParsedText() == literal, language + "/completion typography preserves every translated character",
                        new { literal, rendered = body.GetParsedText(), formatted = body.text });
                    Check(body.fontSize == 32 && !body.enableAutoSizing && body.richText && !body.isTextOverflowing,
                        language + "/completion retains native font size without clipping or auto-shrink",
                        new { body.fontSize, body.enableAutoSizing, body.richText, body.isTextOverflowing,
                            width = body.rectTransform.rect.width, height = body.rectTransform.rect.height, body.textInfo.lineCount });
                    if (language == ParticipantLanguage.Japanese)
                    {
                        const string phrase = "ください。";
                        var characters = body.textInfo.characterInfo.Take(body.textInfo.characterCount).ToArray();
                        string rendered = new string(characters.Select(c => c.character).ToArray());
                        int start = rendered.IndexOf(phrase, StringComparison.Ordinal);
                        var ending = start >= 0 ? characters.Skip(start).Take(phrase.Length).ToArray() : Array.Empty<TMP_CharacterInfo>();
                        Check(ending.Length == phrase.Length && ending.All(c => c.isVisible) && ending.Select(c => c.lineNumber).Distinct().Count() == 1,
                            "Japanese/completion keeps the complete courtesy ending on one rendered line",
                            new { phrase, start, glyphs = ending.Select(c => new { glyph = c.character.ToString(), line = c.lineNumber, c.isVisible }).ToArray() });
                    }
                    captures.Add(type.GetMethod("Capture").Invoke(fixture, new object[] { "completion_typography", view.mainPanel }));
                }
                catch (Exception exception) { Check(false, language + "/typography fixture completes", exception.ToString()); }
                finally
                {
                    (fixture as IDisposable)?.Dispose();
                    foreach (var entry in cursorColors) if (entry.Key) entry.Key.HoverColor = entry.Value;
                }
            }
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews,
                "caller scene and preview count unchanged", new { caller.path, dirty, current_dirty = caller.isDirty, current_previews = EditorSceneManager.previewSceneCount });
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows, captures,
                scope = "Actual TMP glyph line information and Camera.Render on an isolated seeded TEST completion screen with authored fonts. No completed participant task, questionnaire, approval, Quest or wearer acceptance.", hardware_verified = false }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("FOCUSED_COMPLETION_TYPOGRAPHY_FAIL " + failures + "; " + path);
            return "FOCUSED_COMPLETION_TYPOGRAPHY_PASS " + rows.Count + "; " + path;
        }

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Isolated UI checks require stopped Play Mode and no running XR subsystem.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the authored ResearchFlow scene with all three fonts.");
            string directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "operator-ui.json");
            if (File.Exists(path)) throw new IOException("Evidence already exists: " + path);
            var before = SceneManager.GetActiveScene(); bool dirty = before.isDirty;
            int previews = EditorSceneManager.previewSceneCount;
            var cursorColors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed)
            { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
            {
                var preview = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("FocusedOperatorUI_ExplicitFixture") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview); root.SetActive(false);
                ResearchFlowView view = null;
                try
                {
                    var controller = root.AddComponent<ResearchSessionFlowController>(); controller.enabled = false;
                    void Set(string name, object value) => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
                    var registry = new ResearchParticipantRegistry(Path.Combine(directory, language.ToString(), "registry"));
                    var record = registry.CreateNext(ParticipantGroup.Test);
                    Set("Registry", registry); Set("TechnicalSelfSetup", true); Set("SetupLanguage", language);
                    view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                    view.controller = controller; controller.view = view;
                    var cameraObject = new GameObject("FocusedOperatorUICamera_ExplicitMono", typeof(Camera));
                    SceneManager.MoveGameObjectToScene(cameraObject, preview); cameraObject.transform.SetParent(root.transform, false);
                    var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                    camera.stereoTargetEye = StereoTargetEyeMask.None; camera.transform.position = new Vector3(100, 1.65f, 100);
                    view.Head = camera.transform; view.mainPanel.GetComponent<Canvas>().worldCamera = camera;
                    foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                        typeof(ResearchPanelMotion).GetField("environment", Private).SetValue(panel, Array.Empty<Renderer>());
                    var setup = ResearcherSetupView.Build(root.transform, donor.englishFont); setup.controller = controller;
                    setup.OperatorDirectoryOverride = Path.Combine(directory, language.ToString(), "operator");
                    view.Refresh(); setup.Refresh();
                    var open = typeof(ResearchFlowView).GetMethod("OpenResearcherSetup", BindingFlags.Public | BindingFlags.Instance);
                    bool opened = open != null && (bool)open.Invoke(view, null);
                    var canvas = setup.GetComponent<Canvas>();
                    object CanvasState() => new {
                        canvas.enabled, canvas.targetDisplay, canvas_active = canvas.gameObject.activeInHierarchy,
                        parent_enabled = view.mainPanel.GetComponent<Canvas>().enabled,
                        parent_active = view.mainPanel.gameObject.activeInHierarchy,
                        parent_name = canvas.transform.parent?.name, root_name = canvas.rootCanvas ? canvas.rootCanvas.name : null,
                        controller_active = controller.gameObject.activeInHierarchy, main_placement_blocked = view.mainPanel.PlacementBlocked
                    };
                    var inactiveState = CanvasState();
                    // Unity's native rootCanvas/targetDisplay getters resolve active Canvas ancestors only.
                    // Activate the owned UI alone, as the real runtime does; never activate the session controller or enable a blocked Canvas.
                    view.enabled = false; view.transform.SetParent(null, false); Canvas.ForceUpdateCanvases(); view.Refresh();
                    rows.Add(new { name = language + "/native Canvas activation diagnostic", diagnostic_only = true,
                        before_activate_owned_UI = inactiveState, after_activate_owned_UI = CanvasState() });
                    Check(opened && canvas.enabled && canvas.renderMode == RenderMode.WorldSpace && canvas.targetDisplay == 0
                        && canvas.rootCanvas == view.mainPanel.GetComponent<Canvas>(), language + "/single-display reachable operator setup",
                        new { opened, canvas.enabled, canvas.targetDisplay, render_mode = canvas.renderMode.ToString(), display_count = Display.displays.Length,
                            uses_existing_meta_canvas = canvas.rootCanvas == view.mainPanel.GetComponent<Canvas>() });
                    var dropdowns = new[] { "group", "participant", "language", "order", "input" }
                        .Select(n => (TMP_Dropdown)typeof(ResearcherSetupView).GetField(n, Private).GetValue(setup)).ToArray();
                    Check(!opened || (!dropdowns[4].interactable && dropdowns[4].value == 0), language + "/Quest controller mode locked",
                        new { dropdowns[4].interactable, dropdowns[4].value });
                    var lockedInput = setup.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "QuestLockedInputMode");
                    // The setup card speaks the session language (ResearcherSetupView.cs:39,743; ResearchStrings.cs:97).
                    string lockedText = ResearchStrings.Get("researcher.locked_controller", language.ToString());
                    Check(!opened || (lockedInput && lockedInput.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(lockedText) && lockedInput.text == lockedText && !lockedInput.isTextOverflowing),
                        language + "/locked Controller mode is visible rather than a blank row", new { visible = lockedInput && lockedInput.gameObject.activeInHierarchy, text = lockedInput ? lockedInput.text : null });
                    Set("Record", record);
                    Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    setup.Refresh();
                    Check(dropdowns.All(d => !d.interactable), language + "/attached identity settings frozen",
                        dropdowns.Select(d => new { d.name, d.interactable, d.value }).ToArray());
                    bool taskOpened = open != null && (bool)open.Invoke(view, null);
                    Check(!taskOpened && (!opened || canvas.renderMode == RenderMode.ScreenSpaceOverlay || !canvas.enabled),
                        language + "/no operator panel during active task", new { taskOpened, canvas.enabled, render_mode = canvas.renderMode.ToString() });
                    Set("Record", null); Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 0));
                    var evidence = (HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", Private).GetValue(controller);
                    evidence.Add("teleport");
                    view.Refresh(); view.TutorialFactCompleted("teleport");
                    typeof(ResearchFlowView).GetField("successUntil", Private).SetValue(view, Time.unscaledTime - 1);
                    view.Refresh();
                    var cue = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.transform.parent.name == "TutorialSuccess");
                    Check(cue.gameObject.activeInHierarchy || (cue.gameObject.activeSelf && cue.transform.parent.gameObject.activeSelf),
                        language + "/last completed remains after feedback animation", new { cue.text, active = cue.transform.parent.gameObject.activeSelf });
                    var progress = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "TutorialProgress");
                    // Training shows four presentation parts over the five recorded groups; nothing completed is part 1 (ResearchFlowView.cs:1476-1480).
                    Check(progress.text == string.Format(ResearchStrings.Get("tutorial.part_progress", language.ToString()), 1, 4),
                        language + "/four-part Training progress rather than five groups", progress.text);
                    var body = (TMP_Text)typeof(ResearchFlowView).GetField("body", Private).GetValue(view);
                    Check(body.text == ResearchStrings.Get(controller.TutorialInstructionKey, language.ToString()), language + "/next instruction remains distinct", body.text);
                    Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                    evidence.Clear(); evidence.Add("reference"); Set("ReferenceOpen", true); Set("ReferenceMinimized", false); Set("ReferenceSection", "user_manual");
                    view.Refresh(); Canvas.ForceUpdateCanvases();
                    var readerProgress = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "TutorialReaderProgress");
                    var readerCue = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "TutorialCue");
                    var expanded = view.referencePanel.transform.Find("Expanded");
                    Rect Bounds(Transform t)
                    {
                        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(expanded, t);
                        return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
                    }
                    float[] RectValues(Rect bounds) => new[] { bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax };
                    var controls = new[] { "Pause", "Minimize", "Close" }.Select(n => expanded.Find(n)).Where(t => t && t.gameObject.activeInHierarchy).ToArray();
                    Rect Glyph(TMP_Text text, Vector3 min, Vector3 max)
                    {
                        Vector3 a = expanded.InverseTransformPoint(text.transform.TransformPoint(min)), b = expanded.InverseTransformPoint(text.transform.TransformPoint(max));
                        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    }
                    readerProgress.ForceMeshUpdate(true, true); readerCue.ForceMeshUpdate(true, true);
                    var progressGlyphs = Glyph(readerProgress, readerProgress.textBounds.min, readerProgress.textBounds.max);
                    var cueGlyphs = readerCue.textInfo.characterInfo.Take(readerCue.textInfo.characterCount).Where(c => c.isVisible)
                        .Select(c => Glyph(readerCue, c.bottomLeft, c.topRight)).ToArray();
                    // The cue rect may grow up beside the right-aligned progress label (ResearchFlowView.cs:2097), so the rule is on rendered glyphs.
                    Check(readerProgress.gameObject.activeInHierarchy && readerProgress.fontSize >= 24 && !readerProgress.isTextOverflowing && cueGlyphs.Length > 0
                        && controls.All(t => !Bounds(readerProgress.transform).Overlaps(Bounds(t)))
                        && cueGlyphs.All(g => !g.Overlaps(progressGlyphs)), language + "/reader progress owns a row clear of header controls and next instruction",
                        new { readerProgress.text, readerProgress.fontSize, progress_bounds = RectValues(Bounds(readerProgress.transform)), progress_glyphs = RectValues(progressGlyphs),
                            cue_bounds = RectValues(Bounds(readerCue.transform)), cue_glyphs = cueGlyphs.Length == 0 ? null : RectValues(cueGlyphs.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)))),
                            cue_lines = readerCue.textInfo.lineCount, controls = controls.Select(t => new { name = t.name, bounds = RectValues(Bounds(t)) }).ToArray() });
                }
                catch (Exception exception) { Check(false, language + "/fixture completes", exception.ToString()); }
                finally
                {
                    if (view) UnityEngine.Object.DestroyImmediate(view.gameObject);
                    UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview);
                    foreach (var entry in cursorColors) if (entry.Key) entry.Key.HoverColor = entry.Value;
                }
            }
            Check(SceneManager.GetActiveScene() == before && before.isDirty == dirty && EditorSceneManager.previewSceneCount == previews,
                "caller scene and preview count unchanged", new { before.path, dirty, dirty_after = before.isDirty, previews_after = EditorSceneManager.previewSceneCount });
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows,
                scope = "Actual Build/Refresh/identity guard on isolated TEST registry and authored fonts. No native input or wearer acceptance.", hardware_verified = false }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("FOCUSED_OPERATOR_UI_FAIL " + failures + "; " + path);
            return "FOCUSED_OPERATOR_UI_PASS " + rows.Count + "; " + path;
        }
    }
}
#endif
