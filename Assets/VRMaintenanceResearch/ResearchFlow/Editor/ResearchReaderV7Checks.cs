#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchReaderV7Checks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static T Public<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
        static void Set(object value, string name, object next) => value.GetType().GetProperty(name).GetSetMethod(true).Invoke(value, new[] { next });
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty;
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
            var registryField = typeof(ResearchReferenceViewer).GetField("registries", BindingFlags.Static | BindingFlags.NonPublic);
            var cache = (IDictionary)registryField.GetValue(null); var prior = new List<DictionaryEntry>(); foreach (DictionaryEntry item in cache) prior.Add(item);
            object[] donorArgs = { null }; GameObject ownedDonor = null;
            try
            {
                var donor = (ResearchFlowView)typeof(ResearchReaderWireframeChecks).GetMethod("FontDonor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, donorArgs);
                ownedDonor = donorArgs[0] as GameObject;
                var original = JsonUtility.FromJson<ResearchReferenceViewer.Package>(Resources.Load<TextAsset>("ResearchReaderDemo/package").text);
                original.reader_features = new[] { "steps_view_all", "image_viewport" };
                original.steps = Enumerable.Range(1, 23).Select(n => new ResearchReferenceViewer.Step {
                    step_id = "test-step-" + n, topic_id = "test-topic-" + ((n - 1) / 2), text_th = "ขั้นฝึก " + n, text_ja = "練習手順 " + n,
                    picture = JsonUtility.FromJson<ResearchReferenceViewer.Picture>(JsonUtility.ToJson(original.images[0]))
                }).ToArray();
                original.images = Enumerable.Range(1, 2).Select(n => {
                    var p = JsonUtility.FromJson<ResearchReferenceViewer.Picture>(JsonUtility.ToJson(original.images[0])); p.id = "test-image-" + n;
                    p.regions = new[] { new ResearchReferenceViewer.Region { id = "top", x = 0, y = 0, width = 1, height = .5f },
                        new ResearchReferenceViewer.Region { id = "bottom", x = 0, y = .5f, width = 1, height = .5f } }; return p;
                }).ToArray();
                original.contents = Enumerable.Range(0, 12).Select(n => new ResearchReferenceViewer.Entry {
                    id = "topic-" + n, format = "steps", index = n * 2 + 1, title_th = "หัวข้อ " + n, title_ja = "項目 " + n
                }).Concat(Enumerable.Range(1, 2).Select(n => new ResearchReferenceViewer.Entry {
                    id = "image-" + n, format = "images", index = n, title_th = "ภาพ " + n, title_ja = "画像 " + n
                })).ToArray();
                Check(ResearchReferenceViewer.ValidatePackage(original) == null, "capability fixture package valid");
                var registryType = typeof(ResearchReferenceViewer).GetNestedType("Registry", BindingFlags.NonPublic);
                var registry = Activator.CreateInstance(registryType); registryType.GetField("package").SetValue(registry, original);
                registryType.GetField("hash").SetValue(registry, "v7-isolated-fixture");
                var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                {
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(evidenceDirectory, language.ToString()), donor, language });
                        var controller = Public<ResearchSessionFlowController>(fixture, "Controller"); var view = Public<ResearchFlowView>(fixture, "View");
                        Set(controller, "Record", new ParticipantRecord {
                            session_id = "V7-fixture", run_id = language.ToString(), group = ParticipantGroup.Dev, language = language,
                            test_data = true, session_state = RegistryState.InProgress, task_order = TaskOrder.AB,
                            training_version = ResearchUIPolicy.TrainingTrainerV7, ui_policy = ResearchUIPolicy.OwnerMovable
                        });
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                        cache[controller.ReaderResource] = registry;
                        Field<ResearchTaskTiming>(controller, "taskTiming").Start(controller.Logger.Timestamp);
                        Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "step_by_step");
                        fixtureType.GetMethod("Refresh").Invoke(fixture, null); Canvas.ForceUpdateCanvases();
                        var reader = view.ReferenceViewer;
                        reader.SetPresentation(true, true); Canvas.ForceUpdateCanvases();
                        JObject Meta() => JObject.Parse(JsonConvert.SerializeObject(reader.Metadata));
                        var documentScroll = Field<ScrollRect>(reader, "scroll");
                        var focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
                        var pointer = new PointerEventData(EventSystem.current) { pointerId = -73401, button = PointerEventData.InputButton.Left };
                        var writer = Public<HandsLabLogger>(fixture, "Writer");
                        JObject[] Events()
                        {
                            using (var snapshot = new StreamReader(new FileStream(writer.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                                return snapshot.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
                        }
                        JObject[] ImageActions() => Events().Where(e => (string)e["event"] == "REFERENCE_IMAGE_VIEW_CHANGED" && (string)e["source"] != "System").ToArray();
                        void LateScroll() { Canvas.ForceUpdateCanvases(); typeof(ScrollRect).GetMethod("LateUpdate", Hidden).Invoke(documentScroll, null); }
                        focus.OnPointerEnter(pointer);
                        void C(bool pass, string name, object data = null) => Check(pass, language + "/" + name, data);
                        C(reader.ContentsOpen && !(bool)Meta()["active_reading"] && !Meta()["visible_step_ids"].Any(), "initial contents is not content exposure", Meta());
                        C(Field<ScrollRect>(reader, "contentsScroll").content.rect.height > Field<ScrollRect>(reader, "contentsScroll").viewport.rect.height, "long contents uses scroll not tiny fonts");
                        float hiddenTop = documentScroll.verticalNormalizedPosition;
                        var nestedScroll = Field<ScrollRect>(reader, "contentsScroll");
                        float contentsTop = nestedScroll.verticalNormalizedPosition;
                        focus.Scroll(-1, .2f); LateScroll();
                        C(nestedScroll.verticalNormalizedPosition < contentsTop && Mathf.Approximately(documentScroll.verticalNormalizedPosition, hiddenTop), "contents thumbstick moves nested list not hidden document");
                        C(reader.Command("reference_media.contents", "SimulatedController") && !reader.ContentsOpen, "initial contents closes explicitly");
                        C(documentScroll.vertical == reader.CanScrollDocument && reader.GuardedScrollbar.gameObject.activeSelf == reader.CanScrollDocument, "initial contents close refreshes scroll eligibility");
                        C(reader.Command("reference_media.view_all", "SimulatedController"), "view-all accepts explicit choice");
                        var content = Field<RectTransform>(reader, "content");
                        C(content.Cast<Transform>().Count(t => t.name.StartsWith("Step_")) == 23 && reader.CachedPageCount == 23, "view-all contains every pictured step");
                        for (int repeat = 0; repeat < 3; repeat++) { reader.Command("reference_media.view_all", "SimulatedController"); reader.Command("reference_media.view_all", "SimulatedController"); }
                        C(content.Cast<Transform>().Count(t => t.name.StartsWith("Step_")) == 23, "repeated view-all has no duplicate cards");
                        C(!reader.NavigationVisible && reader.SectionId == null, "view-all does not invent one selected topic");
                        C(reader.Command("reference_media.step.23", "SimulatedController") && reader.PageIndex == 23 && reader.CachedPageCount == 1, "arbitrary last step selection releases other pictures");
                        reader.SetPresentation(false, false);
                        C(!reader.Command("reference_media.view_all", "SimulatedController"), "paused hidden reader rejects command");
                        reader.SetPresentation(true, true); C(reader.PageIndex == 23, "pause reentry keeps selected step");
                        C(reader.Navigate(1, "SimulatedController") && reader.ContentsOpen && reader.PageIndex == 23, "topic end opens contents without forced next topic");
                        Set(controller, "ReferenceSection", "illustrated"); view.RefreshReference(); Canvas.ForceUpdateCanvases();
                        C(reader.ContentsOpen && (float)Meta()["image_viewport"]["width"] == 0 && !Meta()["visible_region_ids"].Any(), "covered infographic exposes no viewport or regions");
                        C(reader.Command("reference_media.section.image-2", "SimulatedController"), "arbitrary detail chosen"); Canvas.ForceUpdateCanvases();
                        C((string)Meta()["image_id"] == "test-image-2" && (float)Meta()["image_viewport"]["width"] > 0, "viewport belongs to selected actual image", Meta());
                        documentScroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 200);
                        typeof(ResearchReferenceViewer).GetMethod("Update", Hidden).Invoke(reader, null); LateScroll();
                        int actionsBeforeZoom = ImageActions().Length;
                        C(reader.Command("reference_media.zoom_in", "SimulatedController") && (float)Meta()["image_zoom"] == 1.25f, "infographic zoom command changes its own state");
                        C(reader.CanScrollDocument && documentScroll.vertical && reader.GuardedScrollbar.gameObject.activeSelf, "first zoom enables underlying axis and scrollbar without visibility toggle");
                        LateScroll();
                        C(ImageActions().Length == actionsBeforeZoom + 1, "zoom plus actual ScrollRect LateUpdate credits only one explicit operation");
                        int beforeProgrammatic = ImageActions().Length;
                        documentScroll.verticalNormalizedPosition = .9f; LateScroll();
                        C(ImageActions().Length == beforeProgrammatic && Events().Any(e => (string)e["event"] == "REFERENCE_IMAGE_VIEW_CHANGED" && (string)e["source"] == "System"), "programmatic late callback remains System and noncredit");
                        int beforeStick = ImageActions().Length;
                        focus.Scroll(-1, .03f);
                        var stickAction = ImageActions().LastOrDefault();
                        C(ImageActions().Length == beforeStick + 1 && stickAction != null && JToken.DeepEquals(stickAction["after"]["image_viewport"], Meta()["image_viewport"]), "actual focused thumbstick records post-movement viewport before late callback");
                        LateScroll(); C(ImageActions().Length == beforeStick + 1, "thumbstick late callback adds no second deliberate action");
                        int beforeInvalid = ImageActions().Length;
                        focus.Scroll(0, .1f); focus.Scroll(float.NaN, .1f); focus.OnPointerExit(pointer); focus.Scroll(-1, .1f);
                        C(ImageActions().Length == beforeInvalid, "inactive invalid and unfocused thumbstick input adds no action");
                        focus.OnPointerEnter(pointer);
                        C(reader.Command("reference_media.zoom_reset", "SimulatedController"), "fit reset accepted"); LateScroll();
                        C(!documentScroll.vertical && !reader.GuardedScrollbar.gameObject.activeSelf, "fit reset disables axis and scrollbar");
                        reader.Command("reference_media.zoom_in", "SimulatedController");
                        reader.Command("reference_media.contents", "SimulatedController");
                        C(!documentScroll.vertical && !reader.GuardedScrollbar.gameObject.activeSelf, "contents opening disables hidden document controls");
                        reader.Command("reference_media.contents", "SimulatedController");
                        C(documentScroll.vertical && reader.GuardedScrollbar.gameObject.activeSelf, "contents closing restores zoomed document controls");
                        reader.SetPresentation(false, false); reader.SetPresentation(true, true);
                        C((float)Meta()["image_zoom"] == 1.25f && reader.PageIndex == 2, "image pause reentry preserves pan zoom selection");
                        Set(controller, "ReferenceSection", "step_by_step"); view.RefreshReference(); C(reader.PageIndex == 23, "format return preserves selected step");
                    }
                    catch (Exception error) { Check(false, language + "/exception", error.ToString()); }
                    finally { (fixture as IDisposable)?.Dispose(); }
                }
            }
            catch (Exception error) { Check(false, "setup_exception", error.ToString()); }
            finally
            {
                cache.Clear(); foreach (var pair in prior) cache.Add(pair.Key, pair.Value);
                if (ownedDonor) UnityEngine.Object.DestroyImmediate(ownedDonor);
                Check(before.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "scene and preview state preserved");
            }
            var status = failures == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(evidenceDirectory, "reader-v7.json"), JsonConvert.SerializeObject(new {
                status, evidence = "STOPPED_EDITOR_UI_CALLBACKS_NOT_PLAY_OR_QUEST", checks = rows.Count, failures, rows
            }, Formatting.Indented));
            return "READER_V7 " + status + " checks=" + rows.Count + " failures=" + failures;
        }
    }
}
#endif
