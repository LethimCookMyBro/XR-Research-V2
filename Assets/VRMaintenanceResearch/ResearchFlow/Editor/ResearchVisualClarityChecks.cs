#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Opt-in authored layout and real controller callback checks; no native input or wearer acceptance.
    public static class ResearchVisualClarityChecks
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const int PracticePointer = 73019;
        // Master brief §15.1 asks for 1x and 1.5x renders; the framing is panel-fitted, so only resolution changes.
        internal static float CaptureScale = 1f;

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run visual clarity fixtures outside Play Mode.");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("An explicit evidence directory is required.");
            if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Stop the active XR subsystem before isolated visual fixtures.");
            string directory = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "visual-clarity.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior visual clarity evidence: " + path);
            var activeScene = SceneManager.GetActiveScene();
            var sceneStates = LoadedSceneStates();
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the canonical ResearchFlow scene with its three authored fonts first.");
            string authoredScenePath = string.IsNullOrEmpty(activeScene.path) ? null
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", activeScene.path));
            string authoredSceneHash = File.Exists(authoredScenePath) ? HashFile(authoredScenePath) : null;
            string existingController = ControllerState(donor.controller);
            var existingRaw = WriterHashes(donor.controller ? donor.controller.Logger : null);
            var evidence = new Evidence();
            foreach (var language in new[] { ParticipantLanguage.English, ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
            {
                try
                {
                    using (var fixture = new Fixture(directory, donor, language))
                    {
                        InitialPractice(fixture, evidence);
                        Handling(fixture, evidence);
                        Reference(fixture, evidence);
                        Tasks(fixture, evidence);
                        evidence.Measurements.Add(new { language = language.ToString(), technical_writer = fixture.Writer.EventsPath,
                            writer_state = fixture.Writer.State.ToString(), raw_hashes = WriterHashes(fixture.Writer) });
                    }
                }
                catch (Exception error)
                {
                    var cause = error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error;
                    evidence.Check(false, language + "/Fixture completed", cause.ToString());
                }
            }
            evidence.Check(SceneManager.GetActiveScene() == activeScene && sceneStates.SequenceEqual(LoadedSceneStates()),
                "Loaded caller scenes, active scene and dirty flags remain unchanged");
            evidence.Check(authoredScenePath == null || (File.Exists(authoredScenePath) && authoredSceneHash == HashFile(authoredScenePath)),
                "Authored caller scene file remains byte-identical");
            evidence.Check(existingController == ControllerState(donor.controller) && existingRaw.SequenceEqual(WriterHashes(donor.controller ? donor.controller.Logger : null)),
                "Existing controller state and its raw writer files remain unchanged");
            File.WriteAllText(path, JsonConvert.SerializeObject(new {
                utc = DateTime.UtcNow.ToString("o"), status = evidence.Failures.Count == 0 ? "PASS" : "FAIL",
                checks = evidence.Checks.Count, failures = evidence.Failures, rows = evidence.Checks, measurements = evidence.Measurements,
                captures = evidence.Captures,
                scope = "Separate preview scene and detached authored View per language, inactive Record=null controller, empty owned registry for actual LoggerReady/Pause ownership, owned TEST writer, synthetic mono camera. No participant/session is created. Seeded credits select initial rendering stages without injecting input; seeded held pointers support actual ObjectPointer/Evidence callbacks; a temporary state-only hand-mode record checks artwork and is restored before further callbacks. No SDK acquisition, native stereo, physical input, language approval or study acceptance.",
                research_scope = "Original Task A/B instruction literals are checked verbatim; tutorial fact order, retained credit and rejection of premature move remain unchanged.",
                synthetic_input = true, hardware_verified = false
            }, Formatting.Indented));
            if (evidence.Failures.Count != 0) throw new InvalidOperationException("VISUAL_CLARITY_FAIL " + path + " | " + string.Join(" | ", evidence.Failures));
            return "VISUAL_CLARITY_PASS " + evidence.Checks.Count + " " + path;
        }

        static void InitialPractice(Fixture fixture, Evidence evidence)
        {
            var facts = new[] { "teleport", "snap", "roomscale", "select" };
            for (int index = 0; index < facts.Length; index++)
            {
                string fact = facts[index];
                fixture.ChangeFlow(ResearchFlowState.TutorialInteractions, fact == "select" ? 1 : 0);
                if (fact != "select") fixture.Credits.UnionWith(facts.Take(index));
                fixture.Refresh();
                var ui = fixture.View;
                var body = Get<TMP_Text>(ui, "body");
                var title = Get<TMP_Text>(ui, "title");
                var progress = Get<TMP_Text>(ui, "tutorialProgress");
                var content = Get<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
                var demo = Get<RectTransform>(ui, "tutorialDemo");
                AnimateDemo(ui);
                evidence.Check(fixture.Controller.TutorialFact == fact && ui.TutorialActionReady(fact)
                    && title.GetParsedText() == fixture.Text("tutorial.fact." + fact)
                    && body.GetParsedText() == fixture.Text(fixture.Controller.TutorialInstructionKey)
                    && progress.GetParsedText() == (fact == "select" ? "2 / 5" : "1 / 5"),
                    fixture.Name("Initial " + fact + ": actual current instruction, action heading and progress match the authored fact"));
                CheckText(fixture, evidence, title, "Initial " + fact + " heading");
                CheckText(fixture, evidence, progress, "Initial " + fact + " progress");
                CheckText(fixture, evidence, body, "Initial " + fact + " instruction");
                CheckText(fixture, evidence, Get<Button>(ui, "helpUtility").GetComponentInChildren<TMP_Text>(), "Initial " + fact + " Pause caption");
                evidence.Check(body.textInfo.lineCount <= 3 && Contains(content.rect, BoundsIn(body.rectTransform, content))
                    && demo.gameObject.activeSelf && !demo.Find("InstructionArt").gameObject.activeSelf
                    && !demo.Find("ControlName").gameObject.activeSelf
                    && demo.Find("Person").gameObject.activeSelf == (fact == "roomscale")
                    && demo.Find("Controller").gameObject.activeSelf == (fact != "roomscale"),
                    fixture.Name("Initial " + fact + ": at most three rendered instruction lines accompany the relevant existing control demonstration"),
                    new { lines = body.textInfo.lineCount, text = body.text });
                CheckInstructionGrouping(fixture, evidence, body, "Initial " + fact);
                CheckAnimatedClearance(fixture, evidence, content, body, demo, "Initial " + fact);
                if (fact == "select")
                {
                    var practice = Get<Button[]>(ui, "actions").FirstOrDefault(button => button.gameObject.activeSelf
                        && button.GetComponent<ResearchUIAction>().command == "tutorial_select");
                    evidence.Check(practice && !Overlap(BoundsIn(body.rectTransform, content), BoundsIn((RectTransform)practice.transform, content))
                        && practice.GetComponentInChildren<TMP_Text>().GetParsedText() == fixture.Text("action.practice"),
                        fixture.Name("Initial Select: actual Practice action remains visible and separate from its instruction"));
                    if (practice) CheckText(fixture, evidence, practice.GetComponentInChildren<TMP_Text>(), "Initial Select Practice caption");
                }
                evidence.Captures.Add(fixture.Capture("coach_" + fact, ui.mainPanel));
            }
        }

        static void Handling(Fixture fixture, Evidence evidence)
        {
            fixture.ChangeFlow(ResearchFlowState.TutorialInteractions, 2);
            var ui = fixture.View;
            var controller = fixture.Controller;
            var body = Get<TMP_Text>(ui, "body");
            var title = Get<TMP_Text>(ui, "title");
            var progress = Get<TMP_Text>(ui, "tutorialProgress");
            var content = Get<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
            Vector2 coachSize = content.rect.size;
            evidence.Check(controller.TutorialFact == "grab" && ui.TutorialActionReady("grab"), fixture.Name("Grab is the initial visible actionable fact"));
            evidence.Check(progress.GetParsedText().Trim() == "3 / 5", fixture.Name("Progress is the short step fraction without repeating the action"), progress.text);
            CheckText(fixture, evidence, progress, "main Tutorial header progress");
            CheckText(fixture, evidence, title, "grab heading");
            CheckText(fixture, evidence, body, "grab instruction");
            CheckCoachGeometry(fixture, evidence, coachSize, "grab");

            fixture.Pointer(Oculus.Interaction.PointerEventType.Select);
            evidence.Check(controller.TutorialFact == "move" && fixture.Credits.SetEquals(new[] { "grab" }) && ui.TutorialActionReady("move"),
                fixture.Name("Actual Grab callback exposes Lift and credits only Grab"));
            CheckCoachGeometry(fixture, evidence, coachSize, "move");
            fixture.Pointer(Oculus.Interaction.PointerEventType.Unselect);
            evidence.Check(controller.Flow.TutorialCompleted == 2 && controller.TutorialFact == "move" && controller.TutorialNeedsGrab
                && fixture.Credits.SetEquals(new[] { "grab" }) && !ui.TutorialActionReady("move"),
                fixture.Name("Early release retains Grab and withholds pending Lift credit"));
            evidence.Check(title.GetParsedText() == fixture.Text("tutorial.fact.regrab") && Get<string>(ui, "shownFact") == "grab",
                fixture.Name("Regrab has a distinct heading while the instruction gate remains Grab"), title.text);
            evidence.Check(body.GetParsedText() == fixture.Text("tutorial.try.regrab.controller") && body.textInfo.lineCount <= 2,
                fixture.Name("Regrab instruction has at most two rendered lines and no retained-credit paragraph"), body.text);
            var retained = Get<TMP_Text>(ui, "retainedLabel");
            evidence.Check(retained.GetParsedText() == fixture.Text("tutorial.retained") && retained.gameObject.activeSelf
                && !Get<RectTransform>(ui, "tutorialSuccess").gameObject.activeSelf,
                fixture.Name("Retained practice is visible once in its secondary status line"), retained.text);
            controller.Evidence("move");
            evidence.Check(controller.TutorialFact == "move" && fixture.Credits.SetEquals(new[] { "grab" }) && !fixture.Credits.Contains("release"),
                fixture.Name("Premature Lift Evidence cannot earn Move or Release after early release"));
            CheckText(fixture, evidence, title, "regrab heading");
            CheckText(fixture, evidence, body, "regrab instruction");
            CheckText(fixture, evidence, retained, "retained secondary status");
            CheckCoachGeometry(fixture, evidence, coachSize, "regrab before move");

            fixture.Pointer(Oculus.Interaction.PointerEventType.Select);
            evidence.Check(controller.TutorialFact == "move" && ui.TutorialActionReady("move") && fixture.Credits.SetEquals(new[] { "grab" }),
                fixture.Name("Regrab returns directly to pending Lift without repeating earned credit"));
            controller.Evidence("move");
            evidence.Check(controller.TutorialFact == "rotate" && fixture.Credits.SetEquals(new[] { "grab", "move" }) && ui.TutorialActionReady("rotate"),
                fixture.Name("Actual Move Evidence exposes Rotate and keeps fact order"));
            CheckCoachGeometry(fixture, evidence, coachSize, "rotate");
            fixture.Pointer(Oculus.Interaction.PointerEventType.Cancel);
            evidence.Check(controller.TutorialFact == "rotate" && controller.TutorialNeedsGrab
                && fixture.Credits.SetEquals(new[] { "grab", "move" }) && !fixture.Credits.Contains("release"),
                fixture.Name("System cancellation retains earned facts and cannot earn participant Release"));
            CheckCoachGeometry(fixture, evidence, coachSize, "regrab before rotate");
            fixture.Pointer(Oculus.Interaction.PointerEventType.Select);
            controller.Evidence("rotate");
            evidence.Check(controller.TutorialFact == "release" && fixture.Credits.SetEquals(new[] { "grab", "move", "rotate" }) && ui.TutorialActionReady("release"),
                fixture.Name("Actual Rotate Evidence exposes Release only after taught prerequisites"));
            CheckCoachGeometry(fixture, evidence, coachSize, "release instruction");

            var toolbar = Get<GameObject>(ui, "mainToolbar").transform;
            evidence.Check(!Visible(toolbar.Find("Recenter"), ui.mainPanel.transform) && !Visible(toolbar.Find("Minimize"), ui.mainPanel.transform)
                && Visible(toolbar.Find("DragHandle"), ui.mainPanel.transform),
                fixture.Name("Tutorial coach keeps its move handle without unrelated bottom utility buttons"));
            CheckHandArtwork(fixture, evidence);
        }

        static void CheckCoachGeometry(Fixture fixture, Evidence evidence, Vector2 expectedSize, string stage)
        {
            fixture.Refresh();
            var ui = fixture.View;
            var content = Get<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
            var body = Get<TMP_Text>(ui, "body");
            var demo = Get<RectTransform>(ui, "tutorialDemo");
            AnimateDemo(ui);
            var art = demo.Find("InstructionArt").GetComponent<Image>();
            var expectedSprite = Resources.Load<Sprite>("ResearchFlowTutorial/" + (fixture.Controller.TutorialInstructionFact == "release" ? "Release" : "Grasp"));
            evidence.Check((content.rect.size - expectedSize).sqrMagnitude < .01f,
                fixture.Name(stage + ": coach bounds remain stable across actions"), Size(content));
            evidence.Check(Contains(content.rect, BoundsIn(body.rectTransform, content)) && !Overlap(BoundsIn(body.rectTransform, content), BoundsIn(demo, content)),
                fixture.Name(stage + ": written instruction stays inside the slate and clear of the demonstration"));
            evidence.Check(demo.gameObject.activeSelf && demo.localScale.x >= 1.3f && !demo.Find("ControlName").gameObject.activeSelf
                && expectedSprite && art.sprite == expectedSprite && art.gameObject.activeSelf && art.preserveAspect && !art.raycastTarget
                && demo.Find("Controller").gameObject.activeSelf && !demo.Find("Hand").gameObject.activeSelf,
                fixture.Name(stage + ": actual manipulation artwork and controller cue replace the duplicate caption"), new {
                    scale = demo.localScale.x, sprite = art.sprite ? art.sprite.name : null, expected_sprite = expectedSprite ? expectedSprite.name : null
                });
            CheckText(fixture, evidence, body, stage + " instruction");
            CheckInstructionGrouping(fixture, evidence, body, stage);
            CheckAnimatedClearance(fixture, evidence, content, body, demo, stage);
            evidence.Measurements.Add(new { language = fixture.Language.ToString(), stage, coach_size = Size(content),
                body_text = body.text, body_bounds = RectValues(BoundsIn(body.rectTransform, content)), demo_bounds = RectValues(BoundsIn(demo, content)),
                body_measurement = ResearchSpatialReadabilityChecks.MeasureText(body, fixture.Camera) });
            evidence.Captures.Add(fixture.Capture("coach_" + stage.Replace(' ', '_'), ui.mainPanel));
        }

        static void CheckAnimatedClearance(Fixture fixture, Evidence evidence, RectTransform content, TMP_Text body, RectTransform demo, string stage)
        {
            const int samples = 31;
            const float duration = 3;
            float minimumGap = float.PositiveInfinity, worstTime = 0;
            Rect worstBounds = default;
            bool contained = true;
            var instructionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, body.rectTransform);
            try
            {
                for (int frame = 0; frame < samples; frame++)
                {
                    float time = duration * frame / (samples - 1);
                    AnimateDemo(fixture.View, time);
                    var hierarchy = RectTransformUtility.CalculateRelativeRectTransformBounds(content, demo);
                    var bounds = Rect.MinMaxRect(hierarchy.min.x, hierarchy.min.y, hierarchy.max.x, hierarchy.max.y);
                    contained &= Contains(content.rect, bounds);
                    float gap = instructionBounds.min.x - hierarchy.max.x;
                    if (gap < minimumGap) { minimumGap = gap; worstTime = time; worstBounds = bounds; }
                }
            }
            finally { AnimateDemo(fixture.View); }
            evidence.Check(contained && minimumGap >= ResearchUITheme.SpaceL && demo.GetComponentsInChildren<Graphic>().Length > 0,
                fixture.Name(stage + ": actual animated demo hierarchy stays inside the slate with at least 16 units clear of the instruction"),
                new { samples, duration_seconds = duration, minimum_gap = minimumGap, required_gap = ResearchUITheme.SpaceL,
                    worst_time = worstTime, worst_bounds = RectValues(worstBounds), contained });
        }

        static void CheckInstructionGrouping(Fixture fixture, Evidence evidence, TMP_Text body, string stage)
        {
            body.ForceMeshUpdate(true, true);
            var characters = body.textInfo.characterInfo.Take(body.textInfo.characterCount).ToArray();
            var lines = characters.Where(character => character.character != '\n' && character.character != '\r')
                .GroupBy(character => character.lineNumber).Select(line => new string(line.Select(character => character.character).ToArray()).Trim())
                .Where(line => line.Length != 0).ToArray();
            if (fixture.Language == ParticipantLanguage.Thai && fixture.Controller.TutorialInstructionFact == "rotate")
            {
                int[] TokenLines(string token)
                {
                    int offset = body.text.IndexOf(token, StringComparison.Ordinal);
                    return offset < 0 ? Array.Empty<int>() : characters.Where(character => character.index >= offset && character.index < offset + token.Length)
                        .Select(character => character.lineNumber).Distinct().ToArray();
                }
                var number = TokenLines("30"); var unit = TokenLines("องศา");
                evidence.Check(number.Length == 1 && unit.Length == 1 && number[0] == unit[0],
                    fixture.Name(stage + ": rendered Thai 30 and its degree unit remain on the same line"), new { lines, number_lines = number, unit_lines = unit });
            }
            if (fixture.Language == ParticipantLanguage.Japanese)
            {
                var isolatedEndings = new HashSet<string> { "す", "す。", "ます", "ます。", "る", "る。", "います。", "しました。" };
                evidence.Check(lines.Length > 0 && lines.All(line => !isolatedEndings.Contains(line)),
                    fixture.Name(stage + ": rendered Japanese instruction has no isolated verb ending from the observed regressions"), lines);
            }
        }

        static void CheckHandArtwork(Fixture fixture, Evidence evidence)
        {
            var ui = fixture.View;
            var demo = Get<RectTransform>(ui, "tutorialDemo");
            var art = demo.Find("InstructionArt").GetComponent<Image>();
            string[] retained = fixture.Credits.ToArray();
            bool graspVisible = false, releaseVisible = false;
            try
            {
                Set(fixture.Controller, "Record", new ParticipantRecord { language = fixture.Language, input_mode = ExperimentInputMode.HandTracking, test_data = true });
                fixture.Refresh(); AnimateDemo(ui);
                var release = Resources.Load<Sprite>("ResearchFlowTutorial/Release");
                releaseVisible = fixture.Controller.TutorialUsesHands && release && art.sprite == release && art.gameObject.activeSelf
                    && !demo.Find("Controller").gameObject.activeSelf && !demo.Find("Hand").gameObject.activeSelf;
                fixture.Credits.Remove("rotate");
                fixture.Refresh(); AnimateDemo(ui);
                var grasp = Resources.Load<Sprite>("ResearchFlowTutorial/Grasp");
                graspVisible = fixture.Controller.TutorialUsesHands && grasp && art.sprite == grasp && art.gameObject.activeSelf
                    && !demo.Find("Controller").gameObject.activeSelf && !demo.Find("Hand").gameObject.activeSelf;
            }
            finally
            {
                Set(fixture.Controller, "Record", null); fixture.Credits.Clear(); fixture.Credits.UnionWith(retained); fixture.Refresh();
            }
            evidence.Check(graspVisible && releaseVisible,
                fixture.Name("State-only hand mode displays actual grasp/release sprites without an unrelated controller or old hand circles"),
                new { grasp_visible = graspVisible, release_visible = releaseVisible });
        }

        static void Reference(Fixture fixture, Evidence evidence)
        {
            using var legacyPolicy = ResearchUIPolicy.LegacyRegressionScope(); // exercises the retired panel-drag lesson
            fixture.ReleaseSeededHold();
            fixture.ChangeFlow(ResearchFlowState.TutorialReferenceInspect, 3);
            Set(fixture.Controller, "ReferenceOpen", true);
            Set(fixture.Controller, "ReferenceMinimized", false);
            Set(fixture.Controller, "ReferenceSection", "user_manual");
            fixture.Credits.Add("reference");
            foreach (string fact in new[] { "switch", "reposition", "close" })
            {
                fixture.Refresh();
                var ui = fixture.View;
                var cue = Get<TMP_Text>(ui, "referenceTutorialCue");
                var expanded = Get<GameObject>(ui, "referenceExpanded").GetComponent<RectTransform>();
                var tabs = Get<TMP_Text[]>(ui, "tabLabels").Select(label => label.transform.parent.GetComponent<RectTransform>())
                    .Where(tab => Visible(tab, expanded)).ToArray();
                var cueBounds = BoundsIn(cue.rectTransform, expanded);
                evidence.Check(fixture.Controller.TutorialFact == fact && ui.TutorialActionReady(fact),
                    fixture.Name("Reader " + fact + ": actual current fact remains actionable"));
                evidence.Check(cue.GetParsedText() == fixture.Text(fixture.Controller.TutorialInstructionKey)
                    && cue.rectTransform.rect.height <= 116 && Get<float>(ui, "tutorialCueHeight") <= 116,
                    fixture.Name("Reader " + fact + ": one short instruction replaces repeated progress, heading and body"), cue.text);
                CheckText(fixture, evidence, cue, "Reader " + fact + " instruction");
                var progress = Get<TMP_Text>(ui, "referenceTutorialProgress");
                evidence.Check(progress.GetParsedText() == "4 / 5" && !Overlap(BoundsIn(progress.rectTransform, expanded), cueBounds)
                    && !Get<RectTransform>(ui, "tutorialDemo").gameObject.activeSelf,
                    fixture.Name("Reader " + fact + ": quiet header progress and real controls replace the duplicate controller drawing"));
                CheckText(fixture, evidence, progress, "Reader " + fact + " header progress");
                var expectedFormats = new[] { "user_manual", "step_by_step", "illustrated", "video" };
                evidence.Check(ResearchReferenceScheme.Active.SequenceEqual(expectedFormats) && tabs.Length == expectedFormats.Length
                    && new HashSet<string>(tabs.Select(tab => tab.GetComponent<ResearchUIAction>().command))
                        .SetEquals(expectedFormats.Select(format => "reference_tab." + format))
                    && tabs.All(tab => !Overlap(cueBounds, BoundsIn(tab, expanded))
                    && BoundsIn(tab, expanded).yMax < cueBounds.yMin && cueBounds.yMin - BoundsIn(tab, expanded).yMax >= 12),
                    fixture.Name("Reader " + fact + ": original four format identities remain separated from the cue by a clear gap"));
                var viewport = Get<ScrollRect>(ui, "scroll").viewport;
                evidence.Check(tabs.All(tab => !Overlap(BoundsIn(tab, expanded), BoundsIn(viewport, expanded))),
                    fixture.Name("Reader " + fact + ": format selectors do not overlap document content"));
                foreach (var tab in tabs)
                {
                    var label = tab.GetComponentInChildren<TMP_Text>();
                    var icon = tab.Find("FormatIcon") as RectTransform;
                    string format = tab.GetComponent<ResearchUIAction>().command.Substring("reference_tab.".Length);
                    bool available = fixture.Controller.ReferenceAvailable(format);
                    string expectedLabel = fixture.Text("reference." + format) + (available ? "" : "\n" + fixture.Text("reference.unavailable"));
                    evidence.Check(icon && Visible(icon, expanded) && Contains(tab.rect, BoundsIn(icon, tab))
                        && !Overlap(BoundsIn(icon, tab), BoundsIn(label.rectTransform, tab))
                        && tab.GetComponent<Button>().interactable == available && label.GetParsedText() == expectedLabel,
                        fixture.Name("Reader " + fact + ": " + tab.name + " retains its identity and availability with a separate format icon"));
                    CheckText(fixture, evidence, label, "Reader " + fact + " " + tab.name + " caption");
                }
                var toolbar = expanded.Find("Toolbar");
                evidence.Check(!Visible(toolbar.Find("Recenter"), expanded), fixture.Name("Reader " + fact + ": redundant bottom Recenter remains hidden"));
                if (fact == "reposition")
                {
                    var handle = toolbar.Find("DragHandle").GetComponent<ResearchDragHandle>();
                    var caption = fixture.Text("tutorial.control.trigger");
                    bool namedTrigger = cue.GetParsedText().Contains(caption) || cue.GetParsedText().Contains("Trigger");
                    evidence.Check(cue.GetParsedText() == fixture.Text("tutorial.try.reposition.controller") && namedTrigger
                        && handle.panel == ui.referencePanel && handle.highlight
                        && Visible(handle.transform, expanded) && ui.referencePanel.movable && ((RectTransform)handle.transform).rect.height >= 80,
                        fixture.Name("Panel drag names Trigger and highlights the existing owned drag handle"));
                }
                evidence.Measurements.Add(new { language = fixture.Language.ToString(), stage = "reader_" + fact,
                    current_fact = fixture.Controller.TutorialFact, instruction_text = cue.text, cue_height = cue.rectTransform.rect.height,
                    cue_bounds = RectValues(cueBounds), tabs = tabs.Select(tab => new { name = tab.name, bounds = RectValues(BoundsIn(tab, expanded)) }).ToArray(),
                    glyph_measurement = ResearchSpatialReadabilityChecks.MeasureText(cue, fixture.Camera) });
                evidence.Captures.Add(fixture.Capture("reader_" + fact, ui.referencePanel));
                ui.TutorialFactCompleted(fact);
                Canvas.ForceUpdateCanvases();
                var confirmation = Get<RectTransform>(ui, "tutorialSuccess");
                var confirmationLabel = Get<TMP_Text>(ui, "successLabel");
                confirmationLabel.ForceMeshUpdate(true, true);
                var confirmationBounds = BoundsIn(confirmation, expanded);
                evidence.Check(confirmation.parent == expanded && Visible(confirmation, expanded)
                    && confirmation.GetComponent<CanvasGroup>().alpha > .99f
                    && confirmationLabel.GetParsedText() == fixture.Text("tutorial.success") + "   ·   " + fixture.Text("tutorial.fact." + fact)
                    && Contains(expanded.rect, confirmationBounds) && !Overlap(confirmationBounds, cueBounds)
                    && tabs.All(tab => !Overlap(confirmationBounds, BoundsIn(tab, expanded)))
                    && !Overlap(confirmationBounds, BoundsIn(viewport, expanded)),
                    fixture.Name("Reader " + fact + ": actual confirmation fits its reserved band clear of the cue, formats and document"));
                CheckText(fixture, evidence, confirmationLabel, "Reader " + fact + " actual confirmation");
                ui.CancelTutorialConfirmation();
                fixture.Credits.Add(fact);
            }
            InspectAfterReader(fixture, evidence);
        }

        static void InspectAfterReader(Fixture fixture, Evidence evidence)
        {
            var ui = fixture.View;
            var success = Get<RectTransform>(ui, "tutorialSuccess");
            var content = Get<GameObject>(ui, "mainContent").GetComponent<RectTransform>();
            var expanded = Get<GameObject>(ui, "referenceExpanded");
            var demo = Get<RectTransform>(ui, "tutorialDemo");
            evidence.Check(success.parent == expanded.transform && demo.parent == content,
                fixture.Name("Reader exit starts with confirmation in the reader while the demonstration stays in the coach"));
            Set(fixture.Controller, "ReferenceOpen", false);
            Set(fixture.Controller, "ReferenceMinimized", false);
            fixture.ChangeFlow(ResearchFlowState.TutorialReferenceInspect, 4);
            AnimateDemo(ui);
            var body = Get<TMP_Text>(ui, "body");
            string expectedBody = fixture.Text(fixture.Controller.TutorialInstructionKey) + "\n\n" + fixture.Text("tutorial.examine_difference");
            evidence.Check(fixture.Controller.TutorialFact == "inspect" && ui.TutorialActionReady("inspect")
                && body.GetParsedText() == expectedBody && success.parent == content
                && success.Find("Chip").gameObject.activeSelf && !ui.referencePanel.gameObject.activeSelf
                && !demo.gameObject.activeSelf && !demo.Find("InstructionArt").gameObject.activeSelf,
                fixture.Name("Closing the reader restores Inspect instruction, context and confirmation ownership without manipulation artwork"),
                new { body = body.text, confirmation_parent = success.parent.name });
            var inspect = Get<Button[]>(ui, "actions").FirstOrDefault(button => button.gameObject.activeSelf
                && button.GetComponent<ResearchUIAction>().command == "inspect");
            evidence.Check(inspect && Contains(content.rect, BoundsIn(body.rectTransform, content))
                && !Overlap(BoundsIn(body.rectTransform, content), BoundsIn((RectTransform)inspect.transform, content)),
                fixture.Name("Inspect action and its authored instruction stay inside separate coach regions"));
            CheckText(fixture, evidence, Get<TMP_Text>(ui, "title"), "Inspect heading");
            CheckText(fixture, evidence, Get<TMP_Text>(ui, "tutorialProgress"), "Inspect header progress");
            CheckText(fixture, evidence, body, "Inspect instruction and authored experiment context");
            CheckInstructionGrouping(fixture, evidence, body, "Inspect instruction and authored experiment context");
            if (inspect) CheckText(fixture, evidence, inspect.GetComponentInChildren<TMP_Text>(), "Inspect action label");
            ui.TutorialFactCompleted("close");
            Canvas.ForceUpdateCanvases();
            var label = Get<TMP_Text>(ui, "successLabel");
            label.ForceMeshUpdate(true, true);
            evidence.Check(Visible(success, ui.mainPanel.transform) && success.parent == content
                && success.GetComponent<CanvasGroup>().alpha > .99f
                && label.GetParsedText() == fixture.Text("tutorial.success") + "   ·   " + fixture.Text("tutorial.fact.close")
                && Contains(content.rect, BoundsIn(success, content))
                && !Overlap(BoundsIn(body.rectTransform, content), BoundsIn(success, content))
                && inspect && !Overlap(BoundsIn((RectTransform)inspect.transform, content), BoundsIn(success, content)),
                fixture.Name("Actual close confirmation is visible in the main coach after reader exit without covering Inspect"), label.text);
            CheckText(fixture, evidence, label, "Reader exit confirmation");
            evidence.Captures.Add(fixture.Capture("coach_inspect", ui.mainPanel));
            ui.CancelTutorialConfirmation();
        }

        static void Tasks(Fixture fixture, Evidence evidence)
        {
            string[] neutral = fixture.Language == ParticipantLanguage.Thai
                ? new[] { "เปลี่ยนชิ้นส่วนที่กำหนดโดยใช้\nข้อมูลในเอกสารอ้างอิง", "อุปกรณ์ทำงานผิดปกติ\nใช้ข้อมูลในเอกสารอ้างอิง\nเพื่อวินิจฉัยและแก้ไขปัญหา" }
                : fixture.Language == ParticipantLanguage.Japanese
                ? new[] { "参照資料の情報を使い、\n指定された部品を交換してください。", "機器の動作に異常があります。\n参照資料の情報を使い、\n問題を診断して修正してください。" }
                : new[] { "Replace the designated component using the information in Reference.", "The equipment is operating abnormally.\nUse the information in Reference to diagnose and correct the problem." };
            Set(fixture.Controller, "ReferenceOpen", false);
            Set(fixture.Controller, "ReferenceMinimized", false);
            for (int i = 0; i < 2; i++)
            {
                fixture.ChangeFlow(i == 0 ? ResearchFlowState.Task1Active : ResearchFlowState.Task2Active, 5);
                var body = Get<TMP_Text>(fixture.View, "body");
                AnimateDemo(fixture.View);
                var art = Get<RectTransform>(fixture.View, "tutorialDemo").Find("InstructionArt").GetComponent<Image>();
                string task = i == 0 ? "A" : "B";
                evidence.Check(body.text == neutral[i] && fixture.Text("task." + task + ".body") == neutral[i],
                    fixture.Name("Task " + task + ": original authored neutral instruction remains verbatim without tutorial hints"), body.text);
                evidence.Check(!Get<RectTransform>(fixture.View, "tutorialDemo").gameObject.activeSelf
                    && !Get<TMP_Text>(fixture.View, "tutorialProgress").gameObject.activeSelf
                    && !Visible(Get<TMP_Text>(fixture.View, "referenceTutorialCue").transform, fixture.View.referencePanel.transform)
                    && !art.gameObject.activeSelf,
                    fixture.Name("Task " + task + ": tutorial diagrams and teaching cues remain hidden"));
                var toolbar = Get<GameObject>(fixture.View, "mainToolbar").transform;
                evidence.Check(Visible(toolbar.Find("Recenter"), fixture.View.mainPanel.transform)
                    && Visible(toolbar.Find("Minimize"), fixture.View.mainPanel.transform)
                    && ((RectTransform)toolbar.Find("Recenter")).rect.height >= 80,
                    fixture.Name("Task " + task + ": existing panel utility actions and hit targets remain available"));
                CheckText(fixture, evidence, body, "Task " + task + " neutral instruction");
                evidence.Captures.Add(fixture.Capture("task_" + task, fixture.View.mainPanel));
            }
        }

        static void CheckText(Fixture fixture, Evidence evidence, TMP_Text text, string name)
        {
            text.ForceMeshUpdate(true, true);
            var glyphs = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(character => character.isVisible).ToArray();
            bool fit = glyphs.Length > 0 && glyphs.All(character => new[] { character.bottomLeft, character.topLeft, character.topRight, character.bottomRight }
                .All(point => Inside(text.rectTransform.rect, point)));
            evidence.Check(Visible(text.transform, text.transform.GetComponentInParent<Canvas>().transform) && !text.enableAutoSizing
                && !text.isTextOverflowing && fit && text.preferredHeight <= text.rectTransform.rect.height + 1,
                fixture.Name(name + ": actual glyphs fit their visible authored bounds without autosizing"), new {
                    text = text.text, rect = Size(text.rectTransform), text.fontSize, text.preferredHeight, text.isTextOverflowing, glyphs_fit = fit,
                    minimum_visible_point_size = glyphs.Length == 0 ? 0 : glyphs.Min(character => character.pointSize)
                });
        }

        sealed class Evidence
        {
            public readonly List<object> Checks = new List<object>();
            public readonly List<object> Measurements = new List<object>();
            public readonly List<object> Captures = new List<object>();
            public readonly List<string> Failures = new List<string>();
            public void Check(bool condition, string name, object measurement = null)
            {
                Checks.Add(new { name, pass = condition, measurement });
                if (!condition) Failures.Add(name);
            }
        }

        sealed class Fixture : IDisposable
        {
            readonly Scene preview;
            readonly GameObject root;
            readonly HandsLabObject practiceObject;
            readonly HashSet<int> heldPointers;
            readonly string captureDirectory;
            public readonly ResearchSessionFlowController Controller;
            public readonly ResearchFlowView View;
            public readonly HandsLabLogger Writer;
            public readonly Camera Camera;
            public readonly ParticipantLanguage Language;
            public HashSet<string> Credits => Get<HashSet<string>>(Controller, "tutorialEvidence");

            public Fixture(string directory, ResearchFlowView donor, ParticipantLanguage language)
            {
                Language = language;
                captureDirectory = Path.Combine(directory, "synthetic-mono-preview");
                Directory.CreateDirectory(captureDirectory);
                preview = EditorSceneManager.NewPreviewScene();
                root = NewObject("VisualClarity_" + language, preview); root.SetActive(false);
                try
                {
                    Controller = root.AddComponent<ResearchSessionFlowController>();
                    Controller.registryRoot = Path.Combine(directory, "g", language.ToString());
                    Set(Controller, "Registry", new ResearchParticipantRegistry(Controller.registryRoot));
                    Set(Controller, "Record", null); Set(Controller, "SetupLanguage", language);
                    Set(Controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2));
                    var portA = root.AddComponent<ResearchTaskPort>(); portA.taskId = "A"; portA.demoMode = true;
                    var portB = root.AddComponent<ResearchTaskPort>(); portB.taskId = "B"; portB.demoMode = true;
                    Controller.taskPorts = new[] { portA, portB };
                    practiceObject = NewObject("VisualClarityPracticeObject", preview).AddComponent<HandsLabObject>();
                    practiceObject.transform.SetParent(root.transform, false);
                    Controller.trainingObjects = new[] { practiceObject };
                    heldPointers = Get<HashSet<int>>(practiceObject, "pointers");
                    View = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                    View.controller = Controller; Controller.view = View;
                    View.gameObject.SetActive(false); View.enabled = false; View.transform.SetParent(null, false);
                    var head = NewObject("VisualClarityCamera", preview, typeof(Camera)); head.transform.SetParent(root.transform, false);
                    Camera = head.GetComponent<Camera>(); Camera.enabled = false; Camera.fieldOfView = 90; Camera.aspect = 16f / 9;
                    Camera.stereoTargetEye = StereoTargetEyeMask.None; Camera.transform.position = new Vector3(100, 1.65f, 100); View.Head = Camera.transform;
                    foreach (var panel in new[] { View.mainPanel, View.referencePanel }) SetField(panel, "environment", Array.Empty<Renderer>());
                    Writer = new HandsLabLogger(Path.Combine(directory, "l"), "None", "Visual clarity " + language, technicalTest: true, testData: true);
                    Set(Controller, "Logger", Writer);
                    View.Refresh(); View.gameObject.SetActive(true); Refresh();
                }
                catch { Dispose(); throw; }
            }

            public string Name(string name) => Language + "/" + name;
            public string Text(string key) => ResearchStrings.Get(key, Language.ToString());
            public void Refresh() { View.Refresh(); Canvas.ForceUpdateCanvases(); foreach (var text in View.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true); }
            public void ChangeFlow(ResearchFlowState state, int step)
            {
                Credits.Clear(); View.CancelTutorialConfirmation();
                Set(Controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, state, step)); Refresh();
            }
            public void Pointer(Oculus.Interaction.PointerEventType type)
            {
                if (type == Oculus.Interaction.PointerEventType.Select) heldPointers.Add(PracticePointer); else heldPointers.Remove(PracticePointer);
                typeof(ResearchSessionFlowController).GetMethod("ObjectPointer", PrivateInstance).Invoke(Controller,
                    new object[] { practiceObject, new Oculus.Interaction.PointerEvent(PracticePointer, type, new Pose(practiceObject.transform.position, practiceObject.transform.rotation)) });
                Refresh();
            }
            public void ReleaseSeededHold() => heldPointers.Clear();
            public object Capture(string stage, ResearchPanelMotion panel)
            {
                int width = Mathf.RoundToInt(1280 * CaptureScale), height = Mathf.RoundToInt(800 * CaptureScale);
                string path = Path.Combine(captureDirectory, Language + "_" + stage + ".png");
                if (File.Exists(path)) throw new IOException("Refusing to overwrite prior pixel evidence: " + path);
                var camera = Camera;
                var priorTarget = camera.targetTexture;
                var priorActive = RenderTexture.active;
                var priorScene = camera.scene;
                ulong priorSceneMask = camera.overrideSceneCullingMask;
                var priorParent = camera.transform.parent;
                var priorPosition = camera.transform.position;
                var priorRotation = camera.transform.rotation;
                var priorClear = camera.clearFlags;
                var priorBackground = camera.backgroundColor;
                float priorAspect = camera.aspect, priorFov = camera.fieldOfView;
                bool priorHdr = camera.allowHDR, priorMsaa = camera.allowMSAA;
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    // Unity 6000.3: Camera.scene accepts NewPreviewScene only; the matching scene mask isolates owned UI.
                    camera.scene = preview; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
                    camera.transform.SetParent(null, true); // The fixture Controller stays inactive; its owned Camera can render explicitly.
                    var corners = new Vector3[4]; panel.GetComponent<RectTransform>().GetWorldCorners(corners);
                    var center = (corners[0] + corners[2]) / 2;
                    camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
                    camera.aspect = width / (float)height;
                    float tangent = corners.Select(point => camera.transform.InverseTransformPoint(point)).Max(point =>
                        Mathf.Max(Mathf.Abs(point.y), Mathf.Abs(point.x) / camera.aspect) / Mathf.Max(.01f, point.z));
                    camera.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(tangent * 1.18f) * Mathf.Rad2Deg, 15, 100);
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f, .14f, .18f, 1);
                    camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = target;
                    target.Create(); Canvas.ForceUpdateCanvases();
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(false, false);
                    File.WriteAllBytes(path, pixels.EncodeToPNG());
                    var colors = new HashSet<uint>();
                    foreach (var pixel in pixels.GetPixels32())
                    {
                        colors.Add((uint)pixel.r << 24 | (uint)pixel.g << 16 | (uint)pixel.b << 8 | pixel.a);
                        if (colors.Count > 32) break;
                    }
                    if (colors.Count <= 16) throw new InvalidOperationException("Owned preview render contains insufficient pixel variation: " + path);
                    return new { path, language = Language.ToString(), stage, width, height, pixel_variation_lower_bound = colors.Count,
                        camera_field_of_view = camera.fieldOfView, scene_culling_mask = camera.overrideSceneCullingMask.ToString(),
                        panel = panel.name, panel_world_position = new[] { panel.transform.position.x, panel.transform.position.y, panel.transform.position.z },
                        scope = "Synthetic mono framed world-space UI, isolated owned preview scene. Camera aims at the authored panel and fits its unchanged world corners for qualitative pixel review. Not native head pose, eye-buffer resolution, comfort or Quest acceptance.",
                        hardware_verified = false };
                }
                finally
                {
                    camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
                    camera.scene = priorScene; camera.overrideSceneCullingMask = priorSceneMask;
                    camera.transform.SetParent(priorParent, true); camera.transform.SetPositionAndRotation(priorPosition, priorRotation);
                    camera.clearFlags = priorClear; camera.backgroundColor = priorBackground;
                    camera.aspect = priorAspect; camera.fieldOfView = priorFov; camera.allowHDR = priorHdr; camera.allowMSAA = priorMsaa;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                }
            }
            public void Dispose()
            {
                if (Controller) Set(Controller, "Logger", null);
                Writer?.Dispose();
                if (View) UnityEngine.Object.DestroyImmediate(View.gameObject);
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static GameObject NewObject(string name, Scene scene, params Type[] components)
        { var result = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(result, scene); return result; }
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
        static void AnimateDemo(ResearchFlowView view, float time = 0f) => typeof(ResearchFlowView).GetMethod("AnimateDemo", PrivateInstance).Invoke(view, new object[] { time });
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        static void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
        static bool Visible(Transform value, Transform panel)
        { if (!value || !panel) return false; for (var current = value; current && current != panel; current = current.parent) if (!current.gameObject.activeSelf) return false; return panel.gameObject.activeSelf; }
        static bool Inside(Rect rect, Vector3 point) => point.x >= rect.xMin - 1 && point.x <= rect.xMax + 1 && point.y >= rect.yMin - 1 && point.y <= rect.yMax + 1;
        static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMin >= outer.yMin - 1 && inner.yMax <= outer.yMax + 1;
        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - 1 && a.xMax > b.xMin + 1 && a.yMin < b.yMax - 1 && a.yMax > b.yMin + 1;
        static Rect BoundsIn(RectTransform rect, RectTransform owner)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var local = corners.Select(owner.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(local.Min(point => point.x), local.Min(point => point.y), local.Max(point => point.x), local.Max(point => point.y));
        }
        static float[] Size(RectTransform rect) => new[] { rect.rect.width, rect.rect.height };
        static float[] RectValues(Rect rect) => new[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax };
        static string[] LoadedSceneStates() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty;
        }).ToArray();
        static string ControllerState(ResearchSessionFlowController controller) => controller ? JsonConvert.SerializeObject(new {
            controller.Language, controller.Record, controller.Flow, controller.ReferenceOpen,
            controller.ReferenceMinimized, controller.ReferenceSection, controller.Error
        }) : null;
        static string[] WriterHashes(HandsLabLogger writer) => writer == null ? Array.Empty<string>()
            : new[] { writer.EventsPath, writer.MovementPath, writer.MetadataPath }.Where(File.Exists).Select(path => path + ":" + HashFile(path)).ToArray();
        static string HashFile(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
