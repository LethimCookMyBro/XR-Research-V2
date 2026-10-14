#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Renders S1 Reader Images and video caption cards from the real Task A/B models in the authored canonical scene.
    // Text comes only from the authored steps.json (same text as PDF/Steps), so every format carries the same facts.
    // Writes PNGs and a card manifest; ffmpeg (outside Unity) joins the cards into H.264. Never saves the scene.
    public static class ResearchS1ReaderMediaBuilder
    {
        const string Scene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const int W = 1280, H = 720;
        static Light fill;

        // Camera framing per section: look direction (from target toward camera) and which scene objects to frame.
        internal static readonly Dictionary<string, (Vector3 from, string[] frame)> Shots = new Dictionary<string, (Vector3, string[])>
        {
            ["A.overview"] = (new Vector3(0, .55f, 1), new[] { "ResearchS1TaskA" }),
            ["A.requirements"] = (new Vector3(.45f, .7f, 1), new[] { "ResearchS1TaskA" }),
            ["A.parts"] = (new Vector3(0, .8f, 1), new[] { "M01", "M02", "M03", "M04", "M05", "S01", "S02", "S03", "G01", "G02" }),
            ["A.slots"] = (new Vector3(0, .1f, 1), new[] { "MB_B760_DDR4" }),
            ["A.ram"] = (new Vector3(-.7f, .35f, 1), new[] { "DIMM_A1", "DIMM_B2" }),
            ["A.ssd"] = (new Vector3(.3f, .3f, -1), new[] { "SEAT_CASE_ATX_MID_Sled25_1", "SEAT_CASE_ATX_MID_Sled25_2" }),
            ["A.gpu"] = (new Vector3(-.35f, .3f, 1), new[] { "PCIEX16_1", "PCIEX16_2" }),
            ["A.inspect"] = (new Vector3(-.35f, .6f, 1), new[] { "ResearchS1TaskA" }),
            ["B.overview"] = (new Vector3(.25f, .1f, 1), new[] { "FAN_PED_GUARD_FRONT", "FAN_PED_GUARD_REAR" }),
            ["B.symptom"] = (new Vector3(0, 0, 1), new[] { "FAN_PED_GUARD_FRONT" }),
            ["B.causes"] = (new Vector3(.3f, .1f, 1), new[] { "FAN_PED_BLADE", "FAN_PED_SPINNER" }),
            ["B.observations"] = (new Vector3(.35f, -.2f, -1), new[] { "FAN_PED_GUARD_REAR", "MOV_FAN_PED_GUARD_REAR_GuardClip" }),
            ["B.access"] = (new Vector3(.8f, .1f, .6f), new[] { "FAN_PED_GUARD_FRONT", "FAN_PED_GUARD_REAR" }),
            ["B.correction"] = (new Vector3(-.2f, .2f, -1), new[] { "FAN_PED_GUARD_REAR", "FAN_PED_GUARD_LOCK" }),
            ["B.reassembly"] = (new Vector3(.15f, .15f, 1), new[] { "FAN_PED_SPINNER", "FAN_PED_BLADE" }),
            ["B.inspect"] = (new Vector3(-.3f, .15f, 1), new[] { "FAN_PED_GUARD_FRONT", "FAN_PED_GUARD_REAR" }),
        };

        public static string Build(string documentsRoot, string outputRoot)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Directory.CreateDirectory(outputRoot + "/images");
            var cameraGo = new GameObject("S1MediaCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>(); camera.fieldOfView = 45; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI")); camera.clearFlags = CameraClearFlags.Skybox;
            // Fill light from the viewpoint so part interiors (DIMM slots, trays) are legible; not saved in the scene.
            fill = cameraGo.AddComponent<Light>(); fill.type = LightType.Spot; fill.spotAngle = 70; fill.range = 4; fill.intensity = 1.6f; fill.shadows = LightShadows.None;
            var result = new JObject();
            try
            {
                var targets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var shot in Shots)
                {
                    var bounds = Frame(targets, shot.Value.frame);
                    var direction = shot.Value.from.normalized;
                    float radius = Mathf.Max(bounds.extents.magnitude, .06f);
                    // Close-ups keep a working distance; wide shots stay below the room ceiling.
                    float distance = Mathf.Clamp(radius / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f, .35f, 2.4f);
                    camera.transform.position = bounds.center + direction * distance;
                    camera.transform.LookAt(bounds.center);
                    fill.intensity = Mathf.Clamp(1.6f * distance * distance / 1.4f, .25f, 1.6f);
                    File.WriteAllBytes(outputRoot + "/images/" + shot.Key + ".png", Render(camera, null));
                }
                var fonts = new Dictionary<string, TMP_FontAsset>
                {
                    ["th"] = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset"),
                    ["ja"] = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include).japaneseFont,
                };
                fonts["th"] = fonts["th"] ?? UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include).thaiFont;
                foreach (var task in new[] { "A", "B" })
                    foreach (var lang in new[] { "th", "ja" })
                        result[task + "_" + lang] = Cards(documentsRoot, outputRoot, task, lang, fonts[lang], camera);
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraGo); }
            File.WriteAllText(outputRoot + "/cards.json", result.ToString(Formatting.Indented));
            return "rendered " + Shots.Count + " images and cards for " + result.Count + " packages; scene dirty=" + scene.isDirty;
        }

        // Neutral Training package: Trainer_FX1 sections built from the in-app Training strings, never Task A/B content.
        static readonly (string section, string[] keys, Vector3 from, string[] frame)[] TrainingSections =
        {
            ("T.module", new[] { "examine.info.training.module", "tutorial.try.insert_wrong", "tutorial.try.insert" }, new Vector3(0, .8f, 1), new[] { "training.trainer.module" }),
            ("T.latch", new[] { "tutorial.try.lock", "tutorial.try.unlock" }, new Vector3(-.4f, .7f, 1), new[] { "MOV_TRAINER_FX1_BASE_ModuleLatch", "training.trainer.module" }),
            ("T.screw", new[] { "tutorial.try.rotate_access", "tutorial.try.fasten", "tutorial.try.loosen" }, new Vector3(.4f, .8f, -1), new[] { "SEAT_TRAINER_FX1_BASE_Insert1", "SEAT_TRAINER_FX1_BASE_Insert2" }),
            ("T.lead", new[] { "tutorial.try.cable_connect", "tutorial.try.cable_disconnect" }, new Vector3(.5f, .7f, 1), new[] { "TrainingLeadHead", "SEAT_TRAINER_FX1_BASE_E1" }),
            ("T.inspect", new[] { "tutorial.try.examine", "tutorial.try.inspect_fail", "tutorial.try.inspect.trainer" }, new Vector3(0, .9f, 1), new[] { "ResearchFlow_TrainingStation" }),
        };

        public static string BuildTraining(string outputRoot)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Directory.CreateDirectory(outputRoot + "/images");
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include);
            var cameraGo = new GameObject("S1MediaCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>(); camera.fieldOfView = 45; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            fill = cameraGo.AddComponent<Light>(); fill.type = LightType.Spot; fill.spotAngle = 70; fill.range = 4; fill.shadows = LightShadows.None;
            var result = new JObject();
            try
            {
                var targets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var t in TrainingSections)
                {
                    var bounds = Frame(targets, t.frame);
                    float distance = Mathf.Clamp(Mathf.Max(bounds.extents.magnitude, .06f) / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f, .35f, 2.4f);
                    camera.transform.position = bounds.center + t.from.normalized * distance; camera.transform.LookAt(bounds.center);
                    fill.intensity = Mathf.Clamp(1.6f * distance * distance / 1.4f, .25f, 1.6f);
                    File.WriteAllBytes(outputRoot + "/images/" + t.section + ".png", Render(camera, null));
                }
                var languages = new Dictionary<string, (string name, TMP_FontAsset font)> { ["th"] = ("Thai", view.thaiFont), ["ja"] = ("Japanese", view.japaneseFont), ["en"] = ("English", view.englishFont) };
                foreach (var lang in languages)
                {
                    string dir = outputRoot + "/docs/T/" + lang.Key; Directory.CreateDirectory(dir);
                    var steps = new JArray(TrainingSections.Select(t => new JObject { ["section_id"] = t.section,
                        ["text_" + lang.Key] = string.Join("\n\n", t.keys.Select(k => ResearchStrings.Get(k, lang.Value.name))) }));
                    File.WriteAllText(dir + "/steps.json", steps.ToString(Formatting.Indented));
                    result["T_" + lang.Key] = Cards(outputRoot + "/docs", outputRoot, "T", lang.Key, lang.Value.font, camera);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraGo); }
            File.WriteAllText(outputRoot + "/cards_T.json", result.ToString(Formatting.Indented));
            return "rendered training images and cards; scene dirty=" + scene.isDirty;
        }

        // DEMO Reader set leaves the shipped Resources/StreamingAssets but stays loadable by Editor checks (GUIDs kept by MoveAsset).
        public static string MoveDemoToEditorOnly()
        {
            const string editor = "Assets/VRMaintenanceResearch/ResearchFlow/Editor";
            var log = new List<string>();
            if (!AssetDatabase.IsValidFolder(editor + "/Resources")) AssetDatabase.CreateFolder(editor, "Resources");
            if (!AssetDatabase.IsValidFolder(editor + "/ReaderDemoFixtures")) AssetDatabase.CreateFolder(editor, "ReaderDemoFixtures");
            void Move(string from, string to) => log.Add(from + " [" + AssetDatabase.AssetPathToGUID(from) + "] -> " + to + " : " + (AssetDatabase.MoveAsset(from, to) is var e && string.IsNullOrEmpty(e) ? "ok" : e));
            Move("Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchReaderDemo", editor + "/Resources/ResearchReaderDemo");
            foreach (var file in new[] { "source.pdf", "test.mp4", "test-long.mp4" })
                Move("Assets/StreamingAssets/ResearchReaderDemo/" + file, editor + "/ReaderDemoFixtures/" + file);
            if (AssetDatabase.FindAssets("", new[] { "Assets/StreamingAssets/ResearchReaderDemo" }).Length == 0)
                log.Add("removed empty Assets/StreamingAssets/ResearchReaderDemo: " + AssetDatabase.DeleteAsset("Assets/StreamingAssets/ResearchReaderDemo"));
            return string.Join("\n", log);
        }

        internal static Bounds Frame(List<Transform> all, string[] names)
        {
            bool found = false; var bounds = new Bounds();
            foreach (var name in names)
            {
                var t = all.FirstOrDefault(x => x.name == name || x.GetComponent<AssetLibraryQA.QAPlug>()?.stableId == name || x.GetComponents<AssetLibraryQA.QASocket>().Any(s => s.seatName == name))
                    ?? throw new InvalidOperationException("Frame target missing: " + name);
                var socket = t.GetComponents<AssetLibraryQA.QASocket>().FirstOrDefault(s => s.seatName == name);
                var renderers = socket ? Array.Empty<Renderer>() : t.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                var b = socket ? new Bounds(socket.seat.position, Vector3.one * .03f) : renderers.Length > 0 ? renderers.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }) : new Bounds(t.position, Vector3.one * .05f);
                if (found) bounds.Encapsulate(b); else { bounds = b; found = true; }
            }
            return bounds;
        }

        internal static byte[] Render(Camera camera, Canvas overlay)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = rt; camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(W, H, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, W, H), 0, 0); texture.Apply();
            RenderTexture.active = previous; camera.targetTexture = null;
            var png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            return png;
        }

        // One chapter per section; the section text is paginated (TMP Page overflow) onto cards under the section image.
        static JArray Cards(string documentsRoot, string outputRoot, string task, string lang, TMP_FontAsset font, Camera camera)
        {
            var steps = JArray.Parse(File.ReadAllText(Path.Combine(documentsRoot, task, lang, "steps.json")));
            string dir = outputRoot + "/cards/" + task + "_" + lang; Directory.CreateDirectory(dir);
            var canvasGo = new GameObject("S1CardCanvas", typeof(Canvas), typeof(CanvasScaler)) { hideFlags = HideFlags.HideAndDontSave, layer = LayerMask.NameToLayer("UI") };
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; // a near plane (0.05) shrinks the canvas until TMP distance-field text drops out
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(W, H);
            var background = Child<Image>(canvasGo.transform, "Background", Vector2.zero, new Vector2(W, H)); background.color = Color.white;
            var picture = Child<RawImage>(canvasGo.transform, "Picture", new Vector2(0, 130), new Vector2(W, 460));
            var band = Child<Image>(canvasGo.transform, "Band", new Vector2(0, -235), new Vector2(W, 250)); band.color = ResearchUITheme.Glass;
            // Text lives on its own canvas in front of the image layer (same-canvas sorting hid it behind the band).
            var textCanvasGo = new GameObject("S1CardText", typeof(Canvas), typeof(CanvasScaler)) { hideFlags = HideFlags.HideAndDontSave, layer = LayerMask.NameToLayer("UI") };
            textCanvasGo.transform.SetParent(canvasGo.transform, false);
            var textCanvas = textCanvasGo.GetComponent<Canvas>(); textCanvas.overrideSorting = true; textCanvas.sortingOrder = 10;
            ((RectTransform)textCanvasGo.transform).sizeDelta = new Vector2(W, H);
            var heading = Child<TextMeshProUGUI>(textCanvasGo.transform, "Chapter", new Vector2(0, -130), new Vector2(W - 80, 40));
            var text = Child<TextMeshProUGUI>(textCanvasGo.transform, "Caption", new Vector2(0, -258), new Vector2(W - 80, 190));
            foreach (var t in new[] { heading, text }) { t.font = font; t.color = ResearchUITheme.Ink; t.alignment = TextAlignmentOptions.TopLeft; t.richText = false; }
            heading.fontSize = 26; heading.color = ResearchUITheme.Muted; text.fontSize = 32; text.lineSpacing = 6; text.overflowMode = TextOverflowModes.Page;
            var cards = new JArray();
            try
            {
                int n = 0, chapter = 0;
                foreach (var step in steps)
                {
                    chapter++;
                    string section = (string)step["section_id"];
                    var bytes = File.ReadAllBytes(outputRoot + "/images/" + section + ".png");
                    var image = new Texture2D(2, 2); image.LoadImage(bytes); picture.texture = image;
                    var size = new Vector2(W, 460); float aspect = (float)image.width / image.height;
                    picture.rectTransform.sizeDelta = aspect > size.x / size.y ? new Vector2(size.x, size.x / aspect) : new Vector2(size.y * aspect, size.y);
                    string body = ((string)step["text_" + lang]).Trim();
                    text.overflowMode = TextOverflowModes.Overflow;
                    var pages = Pack(text, body, lang);
                    for (int page = 1; page <= pages.Count; page++)
                    {
                        text.text = pages[page - 1]; heading.text = chapter + " / " + steps.Count + (pages.Count > 1 ? "   (" + page + "/" + pages.Count + ")" : "");
                        text.ForceMeshUpdate(); Canvas.ForceUpdateCanvases();
                        int chars = Math.Max(1, pages[page - 1].Length);
                        string file = (++n).ToString("000") + ".png";
                        int mask = camera.cullingMask; camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
                        var png = Render(camera, canvas);
                        camera.cullingMask = mask;
                        File.WriteAllBytes(Path.Combine(dir, file), png);
                        var check = new Texture2D(2, 2); check.LoadImage(png);
                        int inked = check.GetPixels32(0).Take(W * 230).Count(p => p.r < 110 && p.g < 110);
                        UnityEngine.Object.DestroyImmediate(check);
                        // Reading time: Thai/Japanese characters per second plus a fixed settle time.
                        double seconds = Math.Round(2.5 + chars / (lang == "ja" ? 8.0 : 14.0), 1);
                        cards.Add(new JObject { ["file"] = file, ["chapter"] = chapter, ["section"] = section, ["page"] = page, ["pages"] = pages.Count, ["chars"] = chars,
                            ["seconds"] = seconds, ["inked_band_pixels"] = inked, ["overflow"] = text.isTextOverflowing, ["text"] = pages[page - 1] });
                    }
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(canvasGo); }
            return cards;
        }

        // Whole sentences (Japanese, after 。) or phrases (Thai, at spaces) per card; paragraphs never merge across a blank line.
        static List<string> Pack(TMP_Text box, string body, string lang)
        {
            var cards = new List<string>();
            float limit = box.rectTransform.rect.height, width = box.rectTransform.rect.width;
            foreach (var paragraph in body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var units = lang == "ja" ? System.Text.RegularExpressions.Regex.Split(paragraph.Trim(), "(?<=。)") : paragraph.Trim().Split(' ');
                string current = "";
                foreach (var raw in units.Where(u => u.Length > 0))
                {
                    string unit = raw.Trim('\n');
                    string joined = current.Length == 0 ? unit : current + (lang == "ja" ? "" : " ") + unit;
                    if (current.Length > 0 && box.GetPreferredValues(joined, width, 0).y > limit) { cards.Add(current); current = unit; }
                    else current = joined;
                }
                if (current.Length > 0) cards.Add(current);
            }
            return cards;
        }

        static T Child<T>(Transform parent, string name, Vector2 position, Vector2 size) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave, layer = LayerMask.NameToLayer("UI") };
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            return go.AddComponent<T>();
        }
    }
}
#endif
