#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchContentClarityChecks
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Instance).GetValue(value);
        static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Instance).Invoke(value, args);
        static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        {
            var scene = SceneManager.GetSceneAt(i);
            return scene.handle + ":" + scene.path + ":" + scene.isDirty;
        }).ToArray();

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor without XR required.");
            string directory = Path.GetFullPath(evidenceDirectory);
            if (!Path.IsPathRooted(evidenceDirectory) || Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("A fresh absolute TEST evidence directory is required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Authored TH/JA/EN font donor required.");

            var ownedType = typeof(ResearchParticipantSafetyChecks).GetNestedType("Owned", BindingFlags.NonPublic);
            var beforeScenes = Scenes();
            int previews = EditorSceneManager.previewSceneCount;
            var priorEvents = EventSystem.current;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var checks = new List<object>();
            var captures = new List<object>();
            var layout = new List<object>();
            var packages = new List<object>();
            var failures = new List<string>();
            var errors = new List<string>();
            void Check(bool pass, string name, object observed)
            {
                checks.Add(new { name, pass, observed });
                if (!pass) failures.Add(name);
            }
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception) errors.Add(message + "\n" + stack);
            };
            Directory.CreateDirectory(directory);
            Application.logMessageReceived += onLog;
            try
            {
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                foreach (var task in new[] { "A", "B" })
                {
                    object owned = null;
                    ResearchFlowView view = null;
                    Action<bool, string> visibility = null;
                    string prefix = language + "/" + task;
                    try
                    {
                        owned = Activator.CreateInstance(ownedType, Instance, null,
                            new object[] { Path.Combine(directory, language + "_" + task), donor, language }, null);
                        var controller = Field<ResearchSessionFlowController>(owned, "Controller");
                        view = Field<ResearchFlowView>(owned, "View");
                        visibility = (Action<bool, string>)Delegate.CreateDelegate(typeof(Action<bool, string>), controller,
                            controller.GetType().GetMethod("ReferenceVisibilityChanged", Instance));
                        view.ReferenceVisibilityChanged += visibility;
                        if (task == "B") Call(owned, "Seed", ResearchFlowState.Task2Ready, 5, (bool?)null);
                        void Click(string command) => Call(owned, "Click", (ResearchUIAction)Call(owned, "Action", command, null), 1);
                        Click("task_start");
                        Click("task_start_confirm");
                        Check(controller.ReaderAuthorized && controller.Record.test_data && controller.Record.group == ParticipantGroup.Test
                            && controller.Flow.IsTaskActive && controller.Flow.CurrentTask == task,
                            prefix + "/normal TEST session authorizes active task", new { controller.ReaderAuthorized, controller.Record.test_data, controller.Flow.CurrentTask });

                        Call(owned, "Capture", "task_card", view.mainPanel, captures, layout);
                        foreach (var label in view.mainPanel.GetComponentsInChildren<TMP_Text>(true).Where(t => t.gameObject.activeInHierarchy))
                        {
                            label.ForceMeshUpdate(true, true);
                            Check(!label.isTextOverflowing && !label.isTextTruncated, prefix + "/task text fits/" + label.name,
                                new { label.text, label.isTextOverflowing, label.isTextTruncated, label.fontSize, label.preferredHeight, height = label.rectTransform.rect.height });
                        }
                        Click("reference");
                        var viewer = view.ReferenceViewer;
                        var scroll = Field<ScrollRect>(view, "scroll");
                        void Flush()
                        {
                            Call(owned, "Refresh");
                            Canvas.ForceUpdateCanvases();
                            bool settled = false;
                            foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                                if (panel && panel.gameObject.activeInHierarchy && panel.RecenterPending)
                                {
                                    Call(panel, "TryApplyPendingRecenter", true, true, true, Time.frameCount + 1);
                                    settled |= !panel.RecenterPending;
                                }
                            if (settled) { view.RefreshReference(); Canvas.ForceUpdateCanvases(); }
                            Call(scroll, "LateUpdate");
                            Call(viewer, "Update");
                            Canvas.ForceUpdateCanvases();
                        }
                        JObject Meta() => JObject.FromObject(viewer.Metadata);
                        Flush();
                        string locale = language == ParticipantLanguage.Thai ? "th" : language == ParticipantLanguage.Japanese ? "ja" : "en";
                        string resource = controller.ReaderResource;
                        Check(resource == "ResearchReaderS1/" + task + "5_" + locale + "/package",
                            prefix + "/new session selects versioned V5 content", resource);
                        var asset = Resources.Load<TextAsset>(resource);
                        if (!asset) throw new FileNotFoundException(resource);
                        var package = JObject.Parse(asset.text);
                        string hash = Hash(asset.bytes);
                        packages.Add(new { task, locale, resource, hash, version = (string)package["version"] });
                        Check(new[] { "user_manual", "step_by_step", "illustrated", "video" }.All(controller.ReferenceAvailable),
                            prefix + "/four formats offered", resource);
                        Check(viewer.Status == "AwaitingSelection" && !viewer.IsReady,
                            prefix + "/participant chooses format without forced exposure", Meta());

                        foreach (var format in new[] { "user_manual", "step_by_step", "illustrated", "video" })
                        {
                            Click("reference_tab." + format);
                            Flush();
                            var metadata = Meta();
                            Check(viewer.BoundSection == format && viewer.BoundRegistryResource == resource
                                && (string)metadata["registry_sha256"] == hash && (string)metadata["version"] == (string)package["version"],
                                prefix + "/" + format + "/selected current package", metadata);
                            Call(owned, "Capture", format + "_first", view.referencePanel, captures, layout);
                            if (format == "video")
                            {
                                string video = Path.Combine(Application.streamingAssetsPath, (string)package["video"]["streaming_path"]);
                                Check(File.Exists(video) && Hash(File.ReadAllBytes(video)) == (string)package["video"]["sha256"],
                                    prefix + "/video bytes match manifest", video);
                                Check(viewer.Status == "Loading" && viewer.DecodedFrameCount == 0 && !viewer.IsReady,
                                    prefix + "/stopped Editor makes no playback claim", Meta());
                                continue;
                            }
                            string collection = format == "user_manual" ? "pages" : format == "step_by_step" ? "steps" : "images";
                            int count = ((JArray)package[collection]).Count;
                            var visited = new HashSet<string>();
                            int transitions = 0;
                            while (true)
                            {
                                metadata = Meta();
                                int page = viewer.PageIndex;
                                int part = format == "illustrated" ? (int?)metadata["caption_part"] ?? 1 : 1;
                                string key = page + ":" + part;
                                Check(visited.Add(key), prefix + "/" + format + "/navigation advances/" + key, metadata);
                                bool correct;
                                object textLayout = null;
                                if (format == "user_manual")
                                {
                                    var image = scroll.content.GetComponentsInChildren<RawImage>(true).SingleOrDefault(item => item.name == "PDFPage_" + page);
                                    correct = image && image.texture && image.texture.width > 0 && image.texture.height > 0;
                                }
                                else
                                {
                                    var label = Field<TMP_Text>(viewer, "contentText");
                                    label.ForceMeshUpdate(true, true);
                                    string expected;
                                    if (format == "step_by_step") expected = (string)package["steps"][page - 1]["text_" + locale];
                                    else
                                    {
                                        var (heading, parts) = ResearchCaptionParts.Split((string)package["images"][page - 1]["caption_" + locale]);
                                        expected = heading == null ? parts[part - 1] : (heading.IndexOf('<') < 0 ? "<b>" + heading + "</b>" : heading) + "\n" + parts[part - 1];
                                    }
                                    correct = !string.IsNullOrWhiteSpace(expected) && label.text == expected
                                        && label.textInfo.characterCount > 0 && !label.isTextOverflowing && !label.isTextTruncated;
                                    textLayout = new { label.text, label.isTextOverflowing, label.isTextTruncated, label.fontSize,
                                        label.preferredHeight, height = label.rectTransform.rect.height, width = label.rectTransform.rect.width };
                                    if (format == "illustrated")
                                    {
                                        var image = scroll.content.GetComponentsInChildren<RawImage>(true).SingleOrDefault(item => item.name == "SourceImage_" + page);
                                        correct &= image && image.texture && image.texture.width > 0 && image.texture.height > 0;
                                    }
                                }
                                Check(viewer.IsReady && viewer.PageCount == count && correct,
                                    prefix + "/" + format + "/visible authored content/" + key, new { metadata, textLayout });
                                if (!viewer.CanNext || ++transitions >= 128) break;
                                Click("reference_next");
                                Flush();
                            }
                            var expectedCards = format == "illustrated"
                                ? Enumerable.Range(1, count).SelectMany(page => Enumerable.Range(1, ResearchCaptionParts.Count((string)package["images"][page - 1]["caption_" + locale])).Select(part => page + ":" + part))
                                : Enumerable.Range(1, count).Select(page => page + ":1");
                            Check(transitions < 128 && !viewer.CanNext && visited.SetEquals(expectedCards),
                                prefix + "/" + format + "/all pages reachable through pointer callbacks", visited.ToArray());
                        }
                        var overflows = Field<List<string>>(owned, "TextOverflows");
                        Check(overflows.Count == 0, prefix + "/all captured text fits", overflows.ToArray());
                        Check(!Field<ResearchTaskTiming>(controller, "taskTiming").Ended,
                            prefix + "/reading never completes task", controller.Flow.CurrentTask);
                    }
                    catch (Exception error)
                    {
                        Check(false, prefix + "/execution", (error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error).ToString());
                    }
                    finally
                    {
                        if (view && visibility != null) view.ReferenceVisibilityChanged -= visibility;
                        (owned as IDisposable)?.Dispose();
                    }
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value;
            }
            Check(errors.Count == 0, "Unity callbacks reported no errors", errors);
            Check(beforeScenes.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount && priorEvents == EventSystem.current,
                "caller scenes and EventSystem preserved", new { beforeScenes, afterScenes = Scenes(), previews, afterPreviews = EditorSceneManager.previewSceneCount });
            string report = Path.Combine(directory, "content-clarity-ui.json");
            File.WriteAllText(report, JsonConvert.SerializeObject(new
            {
                status = failures.Count == 0 ? "PASS" : "FAIL", failures,
                scope = "Fresh TEST records; stopped-Editor authored UI and synthetic pointer callbacks; A/B x TH/JA/EN x four formats",
                hardware_verified = false, native_decoder = "NOT_TESTED", wearer_readability = "NOT_TESTED", timed_completion = "NOT_TESTED",
                packages, checks, layout, captures
            }, Formatting.Indented));
            return JsonConvert.SerializeObject(new { status = failures.Count == 0 ? "PASS" : "FAIL", failures = failures.Count, checks = checks.Count, report });
        }
    }
}
#endif
