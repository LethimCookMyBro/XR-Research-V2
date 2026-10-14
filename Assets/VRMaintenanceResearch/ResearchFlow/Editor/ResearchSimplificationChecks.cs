#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Explicit offline fixture: no registry writes, runtime input, or hardware claims.
    public static class ResearchSimplificationChecks
    {
        public static string Run(string evidenceDirectory = null)
        {
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>()
                .FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load ResearchFlow integration scene fonts first.");
            var root = new GameObject("ResearchFlow_SimplificationFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var checks = new List<string>();
            string path = Path.Combine(Path.GetFullPath(evidenceDirectory ?? ("Logs/QuestFinalIntegration/ui-storyboard-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture))), "storyboard-checks.json");
            Exception failure = null;
            void Check(bool value, string description)
            {
                if (!value) throw new InvalidOperationException(description);
                checks.Add(description);
            }
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var ui = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = ui;
                ui.controller = fixture;
                var content = Field<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
                var actions = Field<RectTransform>(ui, "actionsRoot");
                bool Visible(Component component)
                {
                    for (var current = component.transform; current && current != root.transform; current = current.parent)
                        if (!current.gameObject.activeSelf) return false;
                    return true;
                }
                ResearchUIAction[] VisibleActions() => ui.GetComponentsInChildren<ResearchUIAction>(true).Where(Visible).ToArray();
                void State(ResearchFlowState state, int completed = 0, bool? inspected = null)
                {
                    Set(fixture, "Flow", new ResearchSessionFlow(TaskOrder.AB, state, completed, false, inspected));
                    Set(fixture, "ReferenceOpen", false);
                    Set(fixture, "Error", null);
                    ui.Refresh();
                }
                void Size(float width, float height, string label)
                {
                    Check(content.rect.width > 0 && content.rect.height > 0 && content.rect.width <= width && content.rect.height <= height,
                        label + " has positive bounded content size <= " + width + "x" + height);
                }

                var fresh = new ResearchSessionFlow(TaskOrder.AB);
                Check(fresh.StartSession() && fresh.State == ResearchFlowState.TutorialWelcome, "Session gate enters TutorialWelcome directly");
                State(ResearchFlowState.TutorialWelcome);
                Check(VisibleActions().Any(a => a.command == "tutorial_start"), "Welcome exposes tutorial start");
                Check(Visible(Field<TMP_Text>(ui, "body")) && Field<TMP_Text>(ui, "body").text
                    == ui.Text("welcome.body") + "\n\n" + ui.Text("welcome.controller"),
                    "Welcome retains authored practice, two tasks, discomfort safety and the controller input cue");
                Check(ResearchUITheme.Content.r > .9f && ResearchUITheme.Content.a == 1 && ResearchUITheme.Glass.a == 1 && ResearchUITheme.Ink.r < .2f,
                    "Participant reading surfaces are opaque white/pale-blue with dark text");

                for (int step = 0; step < 5; step++)
                {
                    State(step < 3 ? ResearchFlowState.TutorialInteractions : ResearchFlowState.TutorialReferenceInspect, step);
                    var rows = Field<RectTransform[]>(ui, "stepRows");
                    Check(rows.All(r => r.gameObject.activeSelf) && rows[step].gameObject.activeSelf,
                        "Tutorial step " + step + " shows all five progress steps");
                    var checksVisible = Field<GameObject[]>(ui, "stepChecks");
                    Check(checksVisible.Where((r, i) => r.activeSelf != (i < step)).Count() == 0,
                        "Tutorial step " + step + " marks only completed steps");
                    var backgrounds = Field<VRMaintenanceResearch.TrainingV2.TrainingRoundedGraphic[]>(ui, "stepBackgrounds");
                    Check(backgrounds[step].color != backgrounds[(step + 1) % 5].color,
                        "Tutorial step " + step + " visually distinguishes current progress");
                    var progress = ui.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "TutorialProgress");
                    Check(Visible(progress) && !string.IsNullOrWhiteSpace(progress.text), "Tutorial step " + step + " has numbered progress");
                    Check(!ui.helperCanvas.transform.Find("Message").gameObject.activeSelf, "Tutorial helper bubble is not persistent at step " + step);
                }

                foreach (var state in new[] { ResearchFlowState.Task1Active, ResearchFlowState.Task2Active })
                {
                    State(state, 5);
                    var primary = VisibleActions().Where(a => a.transform.IsChildOf(actions)).ToArray();
                    Check(primary.Length == 2 && primary.Select(a => a.command).OrderBy(c => c).SequenceEqual(new[] { "inspect", "reference" }),
                        state + " has neutral Reference and Inspect peer actions");
                    var first = primary[0].GetComponent<UnityEngine.UI.Button>();
                    var second = primary[1].GetComponent<UnityEngine.UI.Button>();
                    Check(first.colors.Equals(second.colors) && first.targetGraphic.color == second.targetGraphic.color,
                        state + " peer actions have equal visual styling");
                    var pause = VisibleActions().Single(a => a.command == "pause");
                    var pauseRect = (RectTransform)pause.transform;
                    Check(!pause.transform.IsChildOf(actions) && pauseRect.rect.width >= 64 && pauseRect.rect.height >= 64
                        && pause.GetComponentInChildren<ResearchUtilityIcon>(true), state + " keeps Pause as a generous corner utility icon");
                    Check(pause.GetComponent<ResearchControlHint>()?.key == "action.pause_short", state + " Pause has a localized tooltip");
                    Check(!ui.helperCanvas.gameObject.activeSelf && !VisibleActions().Any(a => a.command == "helper"), state + " has no tutorial helper");
                    var taskBody = Field<TMP_Text>(ui, "body").rectTransform;
                    var brief = content.Find("ContentSurface").GetComponent<RectTransform>();
                    Check(taskBody.anchoredPosition.y - taskBody.rect.height / 2 >= brief.anchoredPosition.y - brief.rect.height / 2 + 20,
                        state + " body stays within the separate brief surface with a bottom inset");
                    Size(760, 520, state.ToString());
                }

                foreach (var focus in ui.GetComponentsInChildren<ResearchSlateFocus>(true))
                {
                    if (!focus.toolbar) continue;
                    Invoke(focus, "OnDisable");
                    Invoke(focus, "Update");
                    var utilities = new[] { "Minimize", "Recenter", "Close" }.Select(n => focus.toolbar.transform.Find(n)).Where(t => t).ToArray();
                    var revealedUtilities = utilities.Where(t => t.gameObject.activeSelf).ToArray();
                    Check(revealedUtilities.All(t => t.GetComponentInChildren<ResearchUtilityIcon>(true) && !t.Find("Label").gameObject.activeSelf), focus.name + " utilities use consistent icons without permanent captions");
                    Check(revealedUtilities.All(t => { var g = t.GetComponent<CanvasGroup>(); return g && g.alpha == 0 && !g.interactable && !g.blocksRaycasts; }),
                        focus.name + " utility controls hidden and noninteractive by default");
                    Check(focus.toolbar.alpha == 1 && focus.toolbar.blocksRaycasts, focus.name + " grab handle remains available");
                    var pointer = new PointerEventData(null) { pointerId = -73029 };
                    focus.OnPointerEnter(pointer);
                    Invoke(focus, "Update");
                    Check(revealedUtilities.All(t => { var g = t.GetComponent<CanvasGroup>(); return g.alpha == 1 && g.interactable && g.blocksRaycasts; }),
                        focus.name + " controls reveal on deliberate pointer focus");
                    focus.OnPointerExit(pointer);
                    Invoke(focus, "OnDisable");
                    Invoke(focus, "Update");
                }
                var referenceExpanded = Field<GameObject>(ui, "referenceExpanded").transform;
                foreach (string key in new[] { "Minimize", "Close" })
                {
                    var control = referenceExpanded.Find(key);
                    var group = control ? control.GetComponent<CanvasGroup>() : null;
                    Check(control && control.gameObject.activeSelf && (!group || (group.alpha == 1 && group.interactable && group.blocksRaycasts)),
                        "Reference " + key + " stays visible and interactive in the header without focus");
                }

                foreach (var checkpoint in new[] { ResearchFlowState.PostTask1Checkpoint, ResearchFlowState.PostTask2Checkpoint })
                {
                    State(checkpoint, 5);
                    Check(!VisibleActions().Any(a => a.command.Contains("continue") || a.command.Contains("resume")), checkpoint + " has no participant Continue/Resume");
                    Check(!ui.mainPanel.movable, checkpoint + " is fixed");
                    Size(780, 900, checkpoint.ToString());
                }
                foreach (bool pass in new[] { false, true })
                {
                    State(ResearchFlowState.Task1Active, 5, pass);
                    Check(VisibleActions().Where(a => a.transform.IsChildOf(actions)).Select(a => a.command).SequenceEqual(new[] { "inspect_continue" }),
                        "Inspect " + pass + " has one Continue action");
                    Check(!ui.mainPanel.movable, "Inspect " + pass + " is fixed");
                    Size(780, 800, "Inspect " + pass);
                }
                State(ResearchFlowState.ParticipantComplete, 5);
                Check(!ui.mainPanel.movable && !VisibleActions().Any(a => a.transform.IsChildOf(actions)), "Completion is fixed without task actions");
                Size(780, 800, "Completion");
                foreach (var language in new[] { "Thai", "Japanese" })
                {
                    string examine = ResearchStrings.Get("action.examine", language), inspect = ResearchStrings.Get("action.inspect", language);
                    Check(examine == (language == "Thai" ? "ตรวจดู" : "確認") && inspect == (language == "Thai" ? "ตรวจผล" : "検査"), language + " Examine/Inspect terminology stays distinct");
                    string fail = ResearchStrings.Get("inspect.fail.body", language);
                    Check(!string.IsNullOrWhiteSpace(fail) && !fail.Contains(ResearchStrings.Get("action.reference", language)), language + " Fail does not direct participant to Reference");
                }

                State(ResearchFlowState.TutorialReferenceInspect, 3);
                Set(fixture, "ReferenceOpen", true);
                ui.Refresh();
                var scroll = Field<UnityEngine.UI.ScrollRect>(ui, "scroll");
                var reader = Field<GameObject>(ui, "referenceExpanded").GetComponent<RectTransform>();
                var tabs = Field<TMP_Text[]>(ui, "tabLabels");
                Check(tabs.Length == 4 && tabs.Select(t => t.transform.parent.GetComponent<RectTransform>().anchoredPosition.x).Distinct().Count() == 4
                    && tabs.Select(t => t.transform.parent.GetComponent<RectTransform>().anchoredPosition.y).Distinct().Count() == 1,
                    "Reference preserves four equivalent format identities in one tab row");
                Check(!scroll.verticalScrollbar.gameObject.activeSelf, "Reference has no required visible scrollbar");
                Check(reader.rect.width == 780 && reader.rect.height > 0 && reader.rect.height <= 830
                    && !ui.mainPanel.gameObject.activeSelf && reader.Find("TutorialCue")?.gameObject.activeSelf == true,
                    "Tutorial current cue and Reference share one bounded reader surface");
                var previousPage = Field<UnityEngine.UI.Button>(ui, "previousPage");
                var nextPage = Field<UnityEngine.UI.Button>(ui, "nextPage");
                Check(previousPage && nextPage, "Reference retains optional page navigation controls");
                if (scroll.content.rect.height <= scroll.viewport.rect.height + 1)
                    Check(!previousPage.gameObject.activeSelf && !nextPage.gameObject.activeSelf && !Field<TMP_Text>(ui, "pageLabel").gameObject.activeSelf,
                        "Single-page Reference hides Previous, Next and page count");
                // Deterministic three-page geometry exercises the actual page commands without TMP layout on an inactive Canvas.
                scroll.viewport.sizeDelta = new Vector2(710, 300);
                Field<RectTransform>(ui, "referenceContent").sizeDelta = new Vector2(-20, 900);
                scroll.verticalNormalizedPosition = 1;
                ui.LocalCommand("reference_next", "ProgrammaticOfflineFixture");
                Check(Mathf.Approximately(scroll.verticalNormalizedPosition, .5f), "Reference Next advances one viewport");
                ui.LocalCommand("reference_next", "ProgrammaticOfflineFixture");
                ui.LocalCommand("reference_next", "ProgrammaticOfflineFixture");
                Check(Mathf.Abs(scroll.verticalNormalizedPosition) < .00001f && !Field<UnityEngine.UI.Button>(ui, "nextPage").interactable,
                    "Reference navigation clamps at final page; normalized=" + scroll.verticalNormalizedPosition.ToString("R") +
                    "; next=" + Field<UnityEngine.UI.Button>(ui, "nextPage").interactable + "; content=" + scroll.content.rect.height + "; viewport=" + scroll.viewport.rect.height);
                ui.LocalCommand("reference_previous", "ProgrammaticOfflineFixture");
                Check(Mathf.Approximately(scroll.verticalNormalizedPosition, .5f), "Reference Previous returns one viewport");
                Set(fixture, "Error", "PRIVATE_FIXTURE_ERROR_NOT_FOR_PARTICIPANT");
                ui.Refresh();
                var body = Field<TMP_Text>(ui, "body");
                var main = Field<CanvasGroup>(ui, "mainGroup");
                Check(!Field<RectTransform>(ui, "checklist").gameObject.activeSelf, "Error hides tutorial instruction");
                Check(body.gameObject.activeSelf && !string.IsNullOrWhiteSpace(body.text) && !body.text.Contains("PRIVATE_FIXTURE"), "Error shows participant safety copy without diagnostics");
                Check(!ui.referencePanel.gameObject.activeSelf && main.alpha == 1 && main.interactable && main.blocksRaycasts, "Error hides Reference and exposes safety panel");
                typeof(ResearchSessionFlowController).GetMethod("BlockInteractions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fixture, new object[] { "fixture_error" });
                Check(!fixture.ReferenceOpen && !fixture.ReferenceMinimized, "Blocked error clears Reference state");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failure == null ? "Passed" : "Failed", technical_test = true,
                    execution_environment = "EditorOfflineInactiveFixture", hardware_verified = false, assertions = checks,
                    limitation = "Structural and simulated focus checks; no rendered-pixel, ray-acquisition, tracked-hand, or hardware proof.", error = failure?.ToString() }, Formatting.Indented));
            }
            if (failure != null) throw new InvalidOperationException("Simplification checks failed; evidence: " + path, failure);
            return "SIMPLIFICATION_CHECKS_PASS " + checks.Count + " " + path;
        }

        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetProperty(name).GetSetMethod(true).Invoke(target, new[] { value });
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
#endif
