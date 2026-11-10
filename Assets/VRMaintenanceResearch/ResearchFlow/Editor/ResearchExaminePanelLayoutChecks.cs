#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchExaminePanelLayoutChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Run(string output)
        {
            if (EditorApplication.isPlaying || !Path.IsPathRooted(output) || File.Exists(output))
                throw new InvalidOperationException("Stopped Editor and a fresh absolute output file required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
            var scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty, oldDev = ResearchBuildFlavor.EditorDevOverride;
            var root = new GameObject("ExaminePanelLayoutFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var rows = new List<object>(); var failures = new List<string>(); int checks = 0;
            void Check(bool pass, string label) { checks++; if (!pass) failures.Add(label); }
            void Set(object o, string n, object value) => o.GetType().GetProperty(n).GetSetMethod(true).Invoke(o, new[] { value });
            void Field(object o, string n, object value) => o.GetType().GetField(n, Hidden).SetValue(o, value);
            T Get<T>(object o, string n) => (T)o.GetType().GetField(n, Hidden).GetValue(o);
            try
            {
                ResearchBuildFlavor.EditorDevOverride = true;
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                controller.view = view; view.controller = controller;
                Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.BA, ResearchFlowState.Task1Active, 5));
                var record = new ParticipantRecord { group = ParticipantGroup.Dev, test_data = true, training_version = ResearchUIPolicy.TrainingTrainerV6 };
                Set(controller, "Record", record);
                Field(view, "examineOpen", true); Field(view, "examineAreaList", true);
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
                {
                    record.language = language; Field(view, "language", language.ToString());
                    var samples = new Dictionary<string, (string area, string text)>();
                    samples["choose"] = (null, ResearchStrings.Get("examine.choose", language.ToString()));
                    foreach (S1Placement front in Enum.GetValues(typeof(S1Placement)))
                    foreach (S1Placement blade in Enum.GetValues(typeof(S1Placement)))
                    foreach (S1Placement rear in Enum.GetValues(typeof(S1Placement)))
                    foreach (S1ThreadState thread in Enum.GetValues(typeof(S1ThreadState)))
                    foreach (bool aligned in new[] { false, true })
                    foreach (bool clipped in new[] { false, true })
                    foreach (bool isolated in new[] { false, true })
                    foreach (bool running in new[] { false, true })
                    foreach (bool available in new[] { false, true })
                    {
                        var state = new S1TaskBState { systemIntegrityValid = available, powerIsolated = isolated, motorRunning = running,
                            frontGuardPlacement = front, frontGuardAligned = aligned, frontGuardClipped = clipped,
                            bladePlacement = blade, bladeKeyAligned = aligned, spinner = thread,
                            rearGuardPlacement = rear, rearLock = thread };
                        foreach (string area in ResearchS1FanObservations.Areas)
                        {
                            string text = ResearchS1FanObservations.Get(state, area, language.ToString());
                            samples[area + "|" + text] = (area, text);
                        }
                    }
                    foreach (var sample in samples.Values)
                    {
                        Field(view, "examinedTarget", sample.area); Field(view, "examinedInformation", sample.text);
                        foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.font = view.Font;
                        typeof(ResearchFlowView).GetMethod("LayoutExamine", Hidden).Invoke(view, null);
                        var content = Get<GameObject>(view, "mainContent").GetComponent<RectTransform>();
                        var body = Get<TMP_Text>(view, "body"); var title = Get<TMP_Text>(view, "title");
                        var buttons = Get<Button[]>(view, "actions").Take(4).ToArray();
                        string key = language + "/" + sample.area + "/" + sample.text;
                        Vector2 preferred = body.GetPreferredValues(body.text, body.rectTransform.rect.width, 10000);
                        Check(content.rect.height <= 560, key + " compact shell");
                        Check(body.fontSize >= 40 && !body.enableAutoSizing, key + " readable fixed body size");
                        Check(body.color == ResearchUITheme.Ink, key + " opaque Ink body");
                        Check(body.text == sample.text, key + " exact observation");
                        Check(preferred.y <= body.rectTransform.rect.height + .5f, key + " full body fits");
                        Check(title.GetPreferredValues(title.text, title.rectTransform.rect.width, 10000).y <= title.rectTransform.rect.height + .5f, key + " heading fits");
                        Rect Bounds(RectTransform rt)
                        {
                            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                            var a = content.InverseTransformPoint(corners[0]); var b = content.InverseTransformPoint(corners[2]);
                            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                        }
                        Rect bodyRect = Bounds(body.rectTransform);
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            var rt = (RectTransform)buttons[i].transform; var label = buttons[i].GetComponentInChildren<TMP_Text>(true);
                            Rect bounds = Bounds(rt);
                            Check(!bounds.Overlaps(bodyRect), key + " body does not overlap button " + i);
                            Check(content.rect.Contains(bounds.min) && content.rect.Contains(bounds.max), key + " button stays inside " + i);
                            Check(label.fontSize >= 32 && label.GetPreferredValues(label.text, label.rectTransform.rect.width, 10000).y <= label.rectTransform.rect.height + .5f, key + " button label fits " + i);
                            for (int j = 0; j < i; j++) Check(!bounds.Overlaps(Bounds((RectTransform)buttons[j].transform)), key + " buttons do not overlap");
                        }
                        Check(buttons.Take(3).Select(b => ((RectTransform)b.transform).rect.size).Distinct().Count() == 1, key + " equal region targets");
                        if (sample.area == null) Check(buttons.Take(3).Select(b => b.targetGraphic.color).Distinct().Count() == 1, key + " no initial preference");
                        rows.Add(new { language = language.ToString(), sample.area, sample.text, font = body.font.name, body_font_size = body.fontSize,
                            preferred_height = preferred.y, available_height = body.rectTransform.rect.height, shell = new[] { content.rect.width, content.rect.height } });
                    }
                }
            }
            finally
            {
                ResearchBuildFlavor.EditorDevOverride = oldDev;
                UnityEngine.Object.DestroyImmediate(root);
            }
            Check(scene.isDirty == wasDirty, "scene dirty status preserved");
            string verdict = failures.Count == 0 ? "PASS" : "FAIL";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonConvert.SerializeObject(new { verdict, checks, failures, rows, scope = "Unity EditMode isolated actual layout and fonts; not physical input or Quest" }, Formatting.Indented));
            return "EXAMINE_LAYOUT " + verdict + " checks=" + checks + " failures=" + failures.Count;
        }
    }
}
#endif
