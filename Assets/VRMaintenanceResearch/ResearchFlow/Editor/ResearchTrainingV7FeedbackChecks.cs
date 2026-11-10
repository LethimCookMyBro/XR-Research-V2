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
    public static class ResearchTrainingV7FeedbackChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static void Put(object value, string name, object next) => value.GetType().GetField(name, Hidden).SetValue(value, next);
        static void Set(object value, string name, object next) => value.GetType().GetProperty(name).GetSetMethod(true).Invoke(value, new[] { next });
        static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden).Invoke(value, args);
        static Rect Bounds(RectTransform rect, Transform parent)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(parent.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty;
        }).ToArray();
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed
                || !Path.IsPathRooted(evidenceDirectory) || Directory.Exists(evidenceDirectory))
                throw new InvalidOperationException("Stopped Editor, no XR and fresh absolute evidence directory required.");
            Directory.CreateDirectory(evidenceDirectory);
            var rows = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed = null) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            string[] before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
            object[] donorArgs = { null }; GameObject ownedDonor = null;
            try
            {
                var donor = (ResearchFlowView)typeof(ResearchReaderWireframeChecks).GetMethod("FontDonor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, donorArgs);
                ownedDonor = donorArgs[0] as GameObject;
                var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                {
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(evidenceDirectory, language.ToString()), donor, language });
                        var controller = (ResearchSessionFlowController)fixtureType.GetField("Controller").GetValue(fixture);
                        var view = (ResearchFlowView)fixtureType.GetField("View").GetValue(fixture);
                        Set(controller, "Record", new ParticipantRecord { language = language, training_version = ResearchUIPolicy.TrainingTrainerV7,
                            ui_policy = ResearchUIPolicy.OwnerMovable, test_data = true });
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                        Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                        Field<HashSet<string>>(controller, "tutorialEvidence").UnionWith(new[] { "reference", "switch" });
                        view.Refresh();
                        var reader = view.ReferenceViewer; reader.UseRegistry("ResearchReaderDemo/package");
                        reader.Bind("V7-feedback-fixture", "user_manual", true, ""); reader.SetPresentation(true, true);
                        Put(reader, "entries", new[] { new ResearchReferenceViewer.Entry { id = "fixture-topic", index = 1, format = "manual", title_th = "หัวข้อฝึก", title_ja = "練習項目" } });
                        Call(reader, "RenderContents");
                        var contents = Field<UnityEngine.UI.Button>(reader, "contentsButton");
                        var progress = Field<TMP_Text>(view, "referenceTutorialProgress");
                        var cue = Field<TMP_Text>(view, "referenceTutorialCue");
                        var badge = Field<RectTransform>(view, "tutorialSuccess");
                        var parent = Field<GameObject>(view, "referenceExpanded").transform;
                        string counter = (string)Call(view, "TutorialProgress");
                        void CheckText(string stage, string expected)
                        {
                            Canvas.ForceUpdateCanvases(); progress.ForceMeshUpdate(true, true); cue.ForceMeshUpdate(true, true);
                            Check(progress.gameObject.activeInHierarchy && progress.text == expected && !badge.gameObject.activeSelf, language + "/" + stage + " visible exact feedback", progress.text);
                            Check(progress.preferredWidth <= progress.rectTransform.rect.width + 1 && progress.preferredHeight <= progress.rectTransform.rect.height + 1
                                && !progress.isTextOverflowing, language + "/" + stage + " actual TMP fit", new { progress.preferredWidth, progress.preferredHeight });
                            Check(contents && contents.gameObject.activeInHierarchy && !Bounds(progress.rectTransform, parent).Overlaps(Bounds(contents.GetComponent<RectTransform>(), parent)), language + "/" + stage + " Contents nonoverlap");
                            var rect = Bounds(progress.rectTransform, parent); var panel = ((RectTransform)parent).rect;
                            var cueRect = Bounds(cue.rectTransform, parent);
                            var viewport = Field<UnityEngine.UI.ScrollRect>(view, "scroll").viewport;
                            var viewportRect = Bounds(viewport, parent);
                            var headerRect = Bounds(parent.Find("HeaderRule").GetComponent<RectTransform>(), parent);
                            Check(cue.gameObject.activeInHierarchy && !rect.Overlaps(cueRect), language + "/" + stage + " instruction remains separate", new { feedback = rect.ToString(), cue = cueRect.ToString() });
                            Check(panel.Contains(cueRect.min) && panel.Contains(cueRect.max) && cue.preferredHeight <= cue.rectTransform.rect.height + 1
                                && !cue.isTextOverflowing, language + "/" + stage + " instruction fits inside Reader", new { cue = cueRect.ToString(), cue.preferredHeight });
                            Check(!cueRect.Overlaps(viewportRect) && cueRect.yMin >= viewportRect.yMax, language + "/" + stage + " document below instruction", new { cue = cueRect.ToString(), viewport = viewportRect.ToString() });
                            Check(rect.yMax < headerRect.yMin && progress.fontSize == 24 && cue.fontSize == 25, language + "/" + stage + " feedback below header with unchanged fonts", new { feedback = rect.ToString(), header = headerRect.ToString() });
                            Check(panel.Contains(rect.min) && panel.Contains(rect.max), language + "/" + stage + " inside Reader");
                            Check(progress.textInfo.characterInfo.Take(progress.textInfo.characterCount).All(c => char.IsWhiteSpace(c.character) || c.textElement != null), language + "/" + stage + " glyph elements present");
                            fixtureType.GetMethod("Capture").Invoke(fixture, new object[] { stage, view.referencePanel });
                        }
                        Set(controller, "TutorialLastCompletedFact", "manual_contents");
                        view.TutorialFactCompleted("manual_contents");
                        CheckText("completion", ResearchStrings.Get("tutorial.done", language.ToString()));
                        Put(view, "factFeedbackUntil", Time.unscaledTime - 1); Call(view, "RefreshTutorialReaderFeedback");
                        CheckText("completion-expired", counter);
                        Call(view, "ShowNotice", "tutorial.notice.not_yet", "TUTORIAL_NOT_COUNTED_FEEDBACK", "manual_contents", "fixture");
                        CheckText("notice", ResearchStrings.Get("tutorial.notice.not_yet", language.ToString()));
                        Put(view, "noticeUntil", Time.unscaledTime - 1); Call(view, "RefreshTutorialReaderFeedback");
                        CheckText("notice-expired", counter);
                        Set(controller, "ReferenceMinimized", true); view.Refresh();
                        Check(!progress.gameObject.activeInHierarchy, language + "/minimized feedback hidden");
                        Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceOpen", false); view.Refresh();
                        Check(!progress.gameObject.activeInHierarchy, language + "/closed feedback hidden");
                    }
                    catch (Exception error) { Check(false, language + "/exception", error.ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            catch (Exception error) { Check(false, "setup_exception", error.ToString()); }
            finally
            {
                if (ownedDonor) UnityEngine.Object.DestroyImmediate(ownedDonor);
                Check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "scene and preview state preserved");
            }
            string status = failures == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(evidenceDirectory, "training-feedback.json"), JsonConvert.SerializeObject(new {
                status, evidence = "STOPPED_EDITOR_SYNTHETIC_UI_NOT_QUEST", checks = rows.Count, failures, rows
            }, Formatting.Indented));
            return "TRAINING_V7_FEEDBACK " + status + " checks=" + rows.Count + " failures=" + failures;
        }
    }
}
#endif
