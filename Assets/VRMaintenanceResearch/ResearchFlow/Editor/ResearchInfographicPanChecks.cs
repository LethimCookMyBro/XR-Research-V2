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
    public static class ResearchInfographicPanChecks
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
                foreach (var scenario in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese }.SelectMany(language => new[] { new { language, tutorial = false }, new { language, tutorial = true } }))
                {
                    var language = scenario.language;
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(evidenceDirectory, language + (scenario.tutorial ? "_Training" : "_Task")), donor, language });
                        var controller = Public<ResearchSessionFlowController>(fixture, "Controller"); var view = Public<ResearchFlowView>(fixture, "View");
                        Set(controller, "Record", new ParticipantRecord {
                            session_id = "V7-fixture", run_id = language.ToString(), group = ParticipantGroup.Dev, language = language,
                            test_data = true, session_state = RegistryState.InProgress, task_order = TaskOrder.AB,
                            training_version = ResearchUIPolicy.TrainingTrainerV7, ui_policy = ResearchUIPolicy.OwnerMovable
                        });
                        Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, scenario.tutorial ? ResearchFlowState.TutorialReferenceInspect : ResearchFlowState.Task1Active, scenario.tutorial ? 3 : 5));
                        if (scenario.tutorial) Field<HashSet<string>>(controller, "tutorialEvidence").UnionWith(new[] { "reference", "switch", "manual_contents", "manual_select", "manual_next", "steps_next", "steps_back", "steps_contents", "steps_select", "steps_all", "steps_choose", "image_zoom" });
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
                        void C(bool pass, string name, object data = null) => Check(pass, language + (scenario.tutorial ? "/Training/" : "/Task/") + name, data);
                        Set(controller, "ReferenceSection", "illustrated"); view.RefreshReference(); Canvas.ForceUpdateCanvases();
                        reader.SetPresentation(true, true); Canvas.ForceUpdateCanvases();
                        C(reader.Command("reference_media.section.image-2", "SimulatedController"), "actual Reader fixture selects infographic");
                        var images = Field<Dictionary<int, Texture2D>>(reader, "pageCache");
                        var graphicRect = Field<RectTransform>(reader, "infographic");
                        var graphic = graphicRect.GetComponent<RawImage>();
                        var originalTexture = images[reader.PageIndex];

                        foreach (var dimensions in new[] { new Vector2Int(1024, 1024), new Vector2Int(2560, 3450), new Vector2Int(2560, 2650), new Vector2Int(1280, 720) })
                        {
                            var texture = new Texture2D(dimensions.x, dimensions.y, TextureFormat.RGBA32, false);
                            try
                            {
                                images[reader.PageIndex] = texture; graphic.texture = texture;
                                C(reader.IsReady && (bool)Meta()["visible"] && (bool)Meta()["in_view"], dimensions + "/fixture presentation ready before geometry commands", Meta());
                                reader.Command("reference_media.zoom_reset", "SimulatedController");
                                typeof(ResearchReferenceViewer).GetMethod("LayoutInfographic", Hidden).Invoke(reader, null); LateScroll();
                                C(reader.Command("reference_media.zoom_in", "SimulatedController"), dimensions + "/first zoom accepted"); LateScroll();
                                typeof(ResearchReferenceViewer).GetMethod("RefreshPdfControls", Hidden).Invoke(reader, null);
                                float actualViewportWidth = documentScroll.viewport.rect.width, actualViewportHeight = documentScroll.viewport.rect.height;
                                float expectedViewportHeight = 596;
                                if (scenario.tutorial)
                                {
                                    var cue = Field<TMPro.TMP_Text>(view, "referenceTutorialCue"); cue.ForceMeshUpdate(true, true);
                                    expectedViewportHeight = 500 - Mathf.Max(0, cue.rectTransform.rect.height - 32);
                                    C(controller.TutorialFact == "image_pan" && cue.text == ResearchStrings.Get("tutorial.try.image_pan", language.ToString())
                                        && cue.preferredHeight <= cue.rectTransform.rect.height + 1 && !cue.isTextOverflowing,
                                        dimensions + "/actual image-pan Training cue visible and fitted", new { cue.text, cue.preferredHeight, allocatedHeight = cue.rectTransform.rect.height });
                                }
                                C(Mathf.Abs(actualViewportWidth - 810) < .1f && Mathf.Abs(actualViewportHeight - expectedViewportHeight) < .1f
                                    && (!scenario.tutorial || actualViewportHeight < 500), dimensions + "/actual Task or Training-cue layout dimensions", new { actualViewportWidth, actualViewportHeight, expectedViewportHeight, captureReferenceHeight = 476.22f });
                                float expectedWidth = dimensions.x * Mathf.Min((actualViewportWidth - 24f) / dimensions.x, (actualViewportHeight - 24f) / dimensions.y) * 1.25f;
                                C(Mathf.Abs(graphicRect.rect.width - expectedWidth) < .1f, dimensions + "/actual UGUI size matches source aspect", new { expectedWidth, imageWidth = graphicRect.rect.width, viewport = documentScroll.viewport.rect.ToString() });
                                if (expectedWidth <= documentScroll.viewport.rect.width)
                                {
                                    C(Mathf.Abs((float)Meta()["image_viewport"]["width"] - 1) < .0001f, dimensions + "/fit-width image is fully visible without horizontal padding clip", Meta());
                                    var beforePan = Meta()["image_viewport"].DeepClone(); int count = ImageActions().Length;
                                    bool left = reader.Command("reference_media.pan_left", "SimulatedController");
                                    bool right = reader.Command("reference_media.pan_right", "SimulatedController"); LateScroll();
                                    C(!left && !right, dimensions + "/width-fit pan commands reject no-op", new { left, right, beforePan, afterPan = Meta()["image_viewport"] });
                                    C(JToken.DeepEquals(beforePan, Meta()["image_viewport"]) && ImageActions().Length == count, dimensions + "/width-fit pan emits no deliberate event", new { beforeCount = count, afterCount = ImageActions().Length });
                                    C(!view.GetComponentsInChildren<ResearchUIAction>(false).Any(a => (a.command == "reference_media.pan_left" || a.command == "reference_media.pan_right") && a.GetComponent<Button>().interactable), dimensions + "/width-fit pan controls not interactable");
                                }
                                int extraZooms = 0;
                                while ((float)Meta()["image_viewport"]["width"] >= .999f && extraZooms++ < 8)
                                { C(reader.Command("reference_media.zoom_in", "SimulatedController"), dimensions + "/additional zoom changes actual geometry"); LateScroll(); }
                                C((float)Meta()["image_viewport"]["width"] < .999f, dimensions + "/real horizontal image overflow is reached", Meta());
                                float x = (float)Meta()["image_viewport"]["x"]; int eventsBeforePan = ImageActions().Length;
                                var viewportBeforePan = Meta()["image_viewport"].DeepClone(); float normalizedBefore = documentScroll.horizontalNormalizedPosition;
                                string direction = x > .001f ? "reference_media.pan_left" : "reference_media.pan_right";
                                C(reader.Command(direction, "SimulatedController"), dimensions + "/overflow pan accepted", new { direction, normalizedBefore, normalizedAfter = documentScroll.horizontalNormalizedPosition, viewportBeforePan, viewportAfterPan = Meta()["image_viewport"] }); LateScroll();
                                C(!Mathf.Approximately(x, (float)Meta()["image_viewport"]["x"]) && ImageActions().Length == eventsBeforePan + 1, dimensions + "/one pan changes actual viewport and emits exactly one action", new { beforeX = x, viewportBeforePan, normalizedBefore, normalizedAfter = documentScroll.horizontalNormalizedPosition, direction, after = Meta(), beforeEvents = eventsBeforePan, afterEvents = ImageActions().Length });
                                for (int n = 0; n < 10; n++) { reader.Command("reference_media.pan_right", "SimulatedController"); LateScroll(); }
                                int edgeEvents = ImageActions().Length;
                                C(!reader.Command("reference_media.pan_right", "SimulatedController") && ImageActions().Length == edgeEvents, dimensions + "/edge no-op remains noncredit");
                            }
                            finally { images[reader.PageIndex] = originalTexture; graphic.texture = originalTexture; UnityEngine.Object.DestroyImmediate(texture); }
                        }
                        C(!ResearchSessionFlowController.TutorialMediaCommandMatches("image_pan", "illustrated", "reference_media.zoom_in")
                            && ResearchSessionFlowController.TutorialMediaCommandMatches("image_pan", "illustrated", "reference_media.pan_left"), "preparatory zoom never matches pan fact credit");
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
            File.WriteAllText(Path.Combine(evidenceDirectory, "infographic-pan.json"), JsonConvert.SerializeObject(new {
                status, evidence = "STOPPED_EDITOR_ACTUAL_UGUI_ASPECT_FIXTURE_NOT_PLAY_OR_QUEST", checks = rows.Count, failures, rows
            }, Formatting.Indented));
            return "INFOGRAPHIC_PAN " + status + " checks=" + rows.Count + " failures=" + failures;
        }
    }
}
#endif
