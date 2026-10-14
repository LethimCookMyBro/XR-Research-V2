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
    public static class ResearchFocusedOperatorUICapture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<object> fixtures = new List<object>(), rows = new List<object>();
        static Dictionary<Oculus.Interaction.RayInteractorCursorVisual, Color> cursorColors;
        static string directory;
        static double began;
        static Scene caller;
        static bool dirty;
        static int previews;
        static int phase;
        static IDisposable legacyPolicy; // this capture exercises the retired panel-drag lesson
        static readonly string[][] facts = {
            new[] { "teleport", "snap", "roomscale" }, new[] { "select" },
            new[] { "grab", "move", "rotate", "release" },
            new[] { "reference", "switch", "reposition", "close" }, new[] { "inspect" }
        };
        static readonly Dictionary<object, object> readerGeometry = new Dictionary<object, object>();
        public static string Status { get; private set; } = "Idle";
        static T Field<T>(object fixture, string name) => (T)fixture.GetType().GetField(name).GetValue(fixture);
        static void Set(ResearchSessionFlowController controller, string name, object value)
            => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        static object AdditionalProperty(ResearchSessionFlowController controller, string name)
            => typeof(ResearchSessionFlowController).GetProperty(name)?.GetValue(controller);
        static void SeedLastCompleted(ResearchSessionFlowController controller, string fact)
            => typeof(ResearchSessionFlowController).GetProperty("TutorialLastCompletedFact")?.GetSetMethod(true)?.Invoke(controller, new object[] { fact });
        static object Capture(object fixture, string name, ResearchPanelMotion panel = null) => fixture.GetType().GetMethod("Capture").Invoke(fixture,
            new object[] { name, panel ? panel : Field<ResearchFlowView>(fixture, "View").mainPanel });
        static void Refresh(object fixture) => fixture.GetType().GetMethod("Refresh").Invoke(fixture, null);
        static object SequenceState(ResearchFlowView view)
        {
            Canvas.ForceUpdateCanvases();
            var graphic = view.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>(true).FirstOrDefault(g => g.GetType().Name == "ResearchTutorialSequenceGraphic");
            if (!graphic) return new { found = false };
            var renderer = graphic.GetComponent<CanvasRenderer>(); var mesh = renderer ? renderer.GetMesh() : null;
            return new { found = true, active_self = graphic.gameObject.activeSelf, active_in_hierarchy = graphic.gameObject.activeInHierarchy,
                    canvas_renderer_present = !!renderer, renderer_cull = renderer ? (bool?)renderer.cull : null,
                    renderer_depth = renderer ? (int?)renderer.absoluteDepth : null, renderer_alpha = renderer ? (float?)renderer.GetInheritedAlpha() : null,
                    material_shader = graphic.materialForRendering ? graphic.materialForRendering.shader?.name : null,
                    mesh_vertex_count = mesh ? mesh.vertexCount : 0, current_fact = graphic.GetType().GetField("fact")?.GetValue(graphic) };
        }
        static HashSet<string> Credits(object fixture) => (HashSet<string>)fixture.GetType().GetProperty("Credits").GetValue(fixture);
        static void ChangeFlow(object fixture, ResearchFlowState state, int step) => fixture.GetType().GetMethod("ChangeFlow")
            .Invoke(fixture, new object[] { state, step });
        static void Shot(object fixture, string name, ResearchPanelMotion panel = null)
        {
            var view = Field<ResearchFlowView>(fixture, "View"); var controller = Field<ResearchSessionFlowController>(fixture, "Controller");
            rows.Add(new { language = Field<ParticipantLanguage>(fixture, "Language").ToString(), phase = name,
                seeded_TEST_UI_state = true, current_fact = controller.TutorialFact, instruction_fact = controller.TutorialInstructionFact,
                completed_microfacts = AdditionalProperty(controller, "TutorialCompletedFactCount"),
                last_completed = AdditionalProperty(controller, "TutorialLastCompletedFact"),
                current_task = controller.Flow?.CurrentTask, paused = controller.Flow?.Paused,
                active_text = view.GetComponentsInChildren<TMP_Text>(true).Where(t => t.gameObject.activeInHierarchy)
                    .Select(t => new { name = t.name, text = t.text, mesh_overflow = t.isTextOverflowing, font_size = t.fontSize }).ToArray(),
                sequence = SequenceState(view), capture = Capture(fixture, name, panel) });
        }

        static void StageFact(object fixture, int group, string fact)
        {
            var controller = Field<ResearchSessionFlowController>(fixture, "Controller");
            Set(controller, "ReferenceOpen", false); Set(controller, "ReferenceMinimized", false);
            ((HashSet<int>)fixture.GetType().GetField("heldPointers", Private).GetValue(fixture)).Clear();
            ChangeFlow(fixture, group < 3 ? ResearchFlowState.TutorialInteractions : ResearchFlowState.TutorialReferenceInspect, group);
            var before = facts[group].Take(Array.IndexOf(facts[group], fact)).ToArray(); Credits(fixture).UnionWith(before);
            SeedLastCompleted(controller, before.LastOrDefault() ?? (group > 0 ? facts[group - 1].Last() : null));
            // Select a rendering state only; do not call ObjectPointer, locomotion or evidence commit callbacks.
            if (group == 2 && fact != "grab")
                ((HashSet<int>)fixture.GetType().GetField("heldPointers", Private).GetValue(fixture)).Add(73019);
            if (group == 3 && fact != "reference")
            {
                Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceSection", "user_manual");
            }
            Refresh(fixture);
        }

        static object ReaderGeometry(object fixture)
        {
            var view = Field<ResearchFlowView>(fixture, "View");
            var root = view.referencePanel.GetComponent<RectTransform>();
            var scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", Private).GetValue(view);
            return new { size = new[] { root.rect.width, root.rect.height }, position = new[] { root.position.x, root.position.y, root.position.z },
                viewport_size = new[] { scroll.viewport.rect.width, scroll.viewport.rect.height },
                page = typeof(ResearchFlowView).GetProperty("ReferencePageIndex", Private).GetValue(view) };
        }

        public static string Start(string evidenceDirectory)
        {
            if (fixtures.Count != 0 || EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Isolated capture requires an idle capture and no running XR/Play Mode.");
            directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            if (File.Exists(Path.Combine(directory, "temporal-capture.json"))) throw new IOException("Capture evidence already exists.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the authored ResearchFlow scene and fonts.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            caller = SceneManager.GetActiveScene(); dirty = caller.isDirty; previews = EditorSceneManager.previewSceneCount;
            cursorColors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            rows.Clear(); readerGeometry.Clear(); phase = 0; Status = "Running";
            legacyPolicy?.Dispose(); legacyPolicy = ResearchUIPolicy.LegacyRegressionScope();
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                {
                    var fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                    fixtures.Add(fixture);
                    var controller = Field<ResearchSessionFlowController>(fixture, "Controller");
                    var view = Field<ResearchFlowView>(fixture, "View");
                    Set(controller, "TechnicalSelfSetup", true); Set(controller, "Flow", null); view.Refresh();
                    var setup = ResearcherSetupView.Build(view.transform, donor.englishFont); setup.controller = controller;
                    setup.transform.SetParent(controller.transform, false);
                    setup.OperatorDirectoryOverride = Path.Combine(directory, language.ToString(), "operator");
                    bool opened = typeof(ResearchFlowView).GetMethod("OpenResearcherSetup") is MethodInfo open && (bool)open.Invoke(view, null);
                    rows.Add(new { language = language.ToString(), phase = "pre_session_operator", opened, capture = Capture(fixture, "operator") });
                    typeof(ResearchFlowView).GetMethod("CloseResearcherSetup")?.Invoke(view, null);
                    ChangeFlow(fixture, ResearchFlowState.TutorialInteractions, 0);
                    rows.Add(new { language = language.ToString(), phase = "teleport_before", sequence = SequenceState(view), capture = Capture(fixture, "teleport_before") });
                    Credits(fixture).Add("teleport"); SeedLastCompleted(controller, "teleport");
                    view.TutorialFactCompleted("teleport"); Refresh(fixture);
                    rows.Add(new { language = language.ToString(), phase = "snap_after_credit", sequence = SequenceState(view), capture = Capture(fixture, "snap_after_credit") });
                    var camera = Field<Camera>(fixture, "Camera"); var rotation = camera.transform.rotation;
                    camera.transform.rotation = Quaternion.Euler(65, rotation.eulerAngles.y, 0);
                    rows.Add(new { language = language.ToString(), phase = "gaze_away", readable = view.mainPanel.IsReadableFromHead,
                        capture = GazeCapture(fixture, language.ToString()) });
                    camera.transform.rotation = rotation;
                }
                began = EditorApplication.timeSinceStartup; EditorApplication.update += Tick;
                return "FOCUSED_UI_CAPTURE_STARTED " + directory;
            }
            catch (Exception error) { Finish(error.ToString()); throw; }
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - began < .45) return;
            try
            {
                if (phase == 0) foreach (var fixture in fixtures)
                {
                    var view = Field<ResearchFlowView>(fixture, "View");
                    typeof(ResearchFlowView).GetField("successUntil", Private).SetValue(view, Time.unscaledTime - .001f);
                    Refresh(fixture);
                    var cue = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.transform.parent.name == "TutorialSuccess");
                    var progress = view.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == "TutorialProgress");
                    rows.Add(new { language = Field<ParticipantLanguage>(fixture, "Language").ToString(), phase = "returned_after_animation",
                        editor_elapsed_seconds = EditorApplication.timeSinceStartup - began, feedback_window_expired_by_fixture = true,
                        cue = cue.text, cue_visible = cue.transform.parent.gameObject.activeInHierarchy, progress = progress.text,
                        capture = Capture(fixture, "returned_after_animation") });
                    // Every Thai micro-skill; long handling/reader layouts additionally rendered in JP and EN.
                    bool thai = Field<ParticipantLanguage>(fixture, "Language") == ParticipantLanguage.Thai;
                    for (int group = 0; group < facts.Length; group++) foreach (string fact in facts[group])
                    {
                        if (fact == "teleport" || fact == "snap") continue; // already captured above in every language
                        if (!thai && !(fact == "move" || fact == "rotate" || fact == "release" || fact == "switch" || fact == "reposition" || fact == "close")) continue;
                        StageFact(fixture, group, fact);
                        Shot(fixture, "skill_" + fact, Field<ResearchSessionFlowController>(fixture, "Controller").ReferenceOpen ? view.referencePanel : view.mainPanel);
                    }
                    StageFact(fixture, 3, "reposition");
                    readerGeometry.Add(fixture, ReaderGeometry(fixture)); Shot(fixture, "reader_stable_before", view.referencePanel);
                }
                if (phase == 0) { phase = 1; began = EditorApplication.timeSinceStartup; return; }
                foreach (var fixture in fixtures)
                {
                    var view = Field<ResearchFlowView>(fixture, "View"); var controller = Field<ResearchSessionFlowController>(fixture, "Controller");
                    Refresh(fixture);
                    rows.Add(new { language = Field<ParticipantLanguage>(fixture, "Language").ToString(), phase = "reader_stable_after",
                        editor_elapsed_seconds = EditorApplication.timeSinceStartup - began, before = readerGeometry[fixture], after = ReaderGeometry(fixture),
                        scope = "Stationary seeded reader state after real Editor wait; not native hand drag/scroll acceptance.", capture = Capture(fixture, "reader_stable_after", view.referencePanel) });
                    var scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", Private).GetValue(view);
                    scroll.verticalNormalizedPosition = 0;
                    typeof(ResearchFlowView).GetMethod("RefreshPageControls", Private).Invoke(view, null);
                    rows.Add(new { language = Field<ParticipantLanguage>(fixture, "Language").ToString(), phase = "reader_last_page",
                        before = readerGeometry[fixture], after = ReaderGeometry(fixture), synthetic_scroll_value = 0,
                        capture = Capture(fixture, "reader_last_page", view.referencePanel) });
                    Set(controller, "ReferenceOpen", false); ((HashSet<int>)fixture.GetType().GetField("heldPointers", Private).GetValue(fixture)).Clear();
                    foreach (var state in new[] { ResearchFlowState.Task1Active, ResearchFlowState.Task2Active })
                    {
                        ChangeFlow(fixture, state, 5); Shot(fixture, "neutral_task_" + controller.Flow.CurrentTask);
                    }
                    ChangeFlow(fixture, ResearchFlowState.TutorialReferenceInspect, 4); controller.Flow.Pause(); Refresh(fixture); Shot(fixture, "pause");
                    StageFact(fixture, 2, "move");
                    var camera = Field<Camera>(fixture, "Camera"); var rotation = camera.transform.rotation;
                    camera.transform.rotation = Quaternion.Euler(65, rotation.eulerAngles.y, 0);
                    view.RefreshRecoveryUtility();
                    var recovery = view.RecoveryCanvas.GetComponent<ResearchPanelMotion>();
                    rows.Add(new { language = Field<ParticipantLanguage>(fixture, "Language").ToString(), phase = "secondary_recovery",
                        main_readable = view.mainPanel.IsReadableFromHead, recovery_visible = recovery.gameObject.activeInHierarchy,
                        recovery_centered = recovery.centered, size = new[] { ((RectTransform)recovery.transform).rect.width, ((RectTransform)recovery.transform).rect.height },
                        capture = Capture(fixture, "secondary_recovery", recovery) });
                    camera.transform.rotation = rotation;
                    ChangeFlow(fixture, ResearchFlowState.TutorialComplete, 5); Shot(fixture, "tutorial_complete");
                    ChangeFlow(fixture, ResearchFlowState.ParticipantComplete, 5); Shot(fixture, "completion");
                }
                Finish(null);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        static object GazeCapture(object fixture, string language)
        {
            var camera = Field<Camera>(fixture, "Camera"); var preview = (Scene)fixture.GetType().GetField("preview", Private).GetValue(fixture);
            var target = new RenderTexture(1280, 800, 24); var pixels = new Texture2D(1280, 800, TextureFormat.RGBA32, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; var oldScene = camera.scene;
            var oldParent = camera.transform.parent; var oldClear = camera.clearFlags; var oldBackground = camera.backgroundColor;
            ulong oldMask = camera.overrideSceneCullingMask;
            string path = Path.Combine(directory, language, "synthetic-mono-preview", language + "_gaze_away.png");
            try
            {
                camera.scene = preview; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
                camera.transform.SetParent(null, true); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.88f, .92f, .96f, 1);
                camera.targetTexture = target; target.Create(); Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG()); return new { path, pitch = 65, scope = "Actual Editor render with fixture camera looking down, not tracked head input." };
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive; camera.scene = oldScene; camera.overrideSceneCullingMask = oldMask;
                camera.transform.SetParent(oldParent, true); camera.clearFlags = oldClear; camera.backgroundColor = oldBackground;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            legacyPolicy?.Dispose(); legacyPolicy = null;
            foreach (var fixture in fixtures) ((IDisposable)fixture).Dispose(); fixtures.Clear();
            foreach (var entry in cursorColors) if (entry.Key) entry.Key.HoverColor = entry.Value;
            bool preserved = SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews;
            Status = error == null && preserved ? "Complete" : "Failed";
            File.WriteAllText(Path.Combine(directory, "temporal-capture.json"), JsonConvert.SerializeObject(new { status = Status, rows, error,
                caller_scene_preserved = preserved, scope = "Actual Camera.Render and authored TMP fonts on isolated seeded TEST UI. All 13 Thai micro-skills; JP/EN long handling/reader layouts, menu, neutral Task A/B, pause, recovery and completion. Real Editor waits >=0.45s with explicitly expired feedback window; stationary reader then synthetic scroll-value rendering. No committed input, study result, Quest, consent or wearer acceptance.", hardware_verified = false }, Formatting.Indented));
        }
    }
}
#endif
