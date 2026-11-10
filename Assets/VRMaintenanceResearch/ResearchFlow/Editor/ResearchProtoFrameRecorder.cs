#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 3 Nov prototype media: records fixed close-up cameras while the Task B SDK runner drives the real mechanics,
    // so Video and Illustrated show tested motion, not authored animation. Editor-only; writes outside session data.
    public static class ResearchProtoFrameRecorder
    {
        static Camera[] cameras;
        static string[] cameraNames;
        static RenderTexture target;
        static Texture2D pixels;
        static StreamWriter index;
        static string directory;
        static Transform[] anchors;
        static Vector3[] anchorOffsets;
        static string[] anchorNames;
        static double started, lastFrameAt;
        static int frame;
        static float interval;
        static string[] filters = new string[0];
        static readonly FieldInfo RunnerStep = typeof(ResearchS1TaskBSdkRunner).GetField("step", BindingFlags.NonPublic | BindingFlags.Static);

        public static bool Recording => cameras != null;

        // cameraList: "name:eyeX,eyeY,eyeZ,lookX,lookY,lookZ,fov;..." offsets from the front guard centre in world metres,
        // so the framing follows the lowered fan layout.
        // anchorList: comma-separated "Name" or "alias=Name@x|y|z" (a local-space point on that transform, columns named alias).
        // stepFilter: comma-separated substrings of the runner step; frames are saved only while one matches (empty = always).
        public static string Start(string outputDirectory, string cameraList, int width, int height, float framesPerSecond, string anchorList, string stepFilter)
        {
            filters = (stepFilter ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start the recorder in Play Mode with Task B active.");
            if (Recording) throw new InvalidOperationException("Recorder already running.");
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>();
            if (!task || !task.frontGuard) throw new InvalidOperationException("Task B mechanics not found.");
            if (Directory.Exists(outputDirectory)) throw new InvalidOperationException("Use a fresh output directory.");
            directory = outputDirectory;
            Directory.CreateDirectory(directory);
            var centre = GuardCentre(task);
            var specs = Parse(cameraList);
            cameras = new Camera[specs.Length]; cameraNames = new string[specs.Length];
            for (int c = 0; c < specs.Length; c++)
            {
                cameraNames[c] = specs[c].name;
                cameras[c] = MakeCamera(specs[c], centre);
                Directory.CreateDirectory(Path.Combine(directory, "frames_" + cameraNames[c]));
            }
            target = new RenderTexture(width, height, 24) { antiAliasing = 8 };
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var specsA = (anchorList ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            anchorNames = specsA.Select(a => a.Contains('=') ? a.Split('=')[0] : a).ToArray();
            string[] targets = specsA.Select(a => (a.Contains('=') ? a.Split('=')[1] : a).Split('@')[0]).ToArray();
            anchorOffsets = specsA.Select(a => a.Contains('@')
                ? (Vector3)ParseVector(a.Split('@')[1].Split('|')) : Vector3.zero).ToArray();
            anchors = targets.Select(n => task.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n)).ToArray();
            index = new StreamWriter(Path.Combine(directory, "frames.csv"));
            index.WriteLine("frame,seconds,step," + string.Join(",", cameraNames.SelectMany(cn => anchorNames.SelectMany(n => new[] { cn + "." + n + "_x", cn + "." + n + "_y", cn + "." + n + "_visible" }))));
            for (int c = 0; c < cameras.Length; c++)
                File.WriteAllText(Path.Combine(directory, "camera_" + cameraNames[c] + ".json"), JsonUtility.ToJson(new CameraRecord
                {
                    position = cameras[c].transform.position, rotation = cameras[c].transform.rotation.eulerAngles, fieldOfView = cameras[c].fieldOfView,
                    width = width, height = height, guardCentre = centre, missingAnchors = string.Join(",", anchorNames.Where((n, i) => !anchors[i]))
                }, true));
            interval = 1f / Mathf.Max(1, framesPerSecond);
            frame = 0; started = lastFrameAt = EditorApplication.timeSinceStartup - interval;
            EditorApplication.update += Tick;
            return "recording " + directory + " cameras=" + string.Join(",", cameraNames) + " missing_anchors=" + string.Join(",", anchorNames.Where((n, i) => !anchors[i]));
        }

        // Stills for framing review, Edit or Play Mode; same camera spec format as Start.
        public static string Still(string outputDirectory, string cameraList, int width, int height)
        {
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            var centre = GuardCentre(task);
            Directory.CreateDirectory(outputDirectory);
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 8 };
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            foreach (var spec in Parse(cameraList))
            {
                var cam = MakeCamera(spec, centre);
                Grab(cam, rt, tex);
                File.WriteAllBytes(Path.Combine(outputDirectory, spec.name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
            return "stills " + outputDirectory + " centre=" + centre.ToString("F3");
        }

        // Read-only: lists visible participant-view text in the wrong script so a Japanese take cannot ship Thai panels.
        public static string UiLanguageAudit()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            string language = controller ? controller.Language : "none";
            bool Thai(char c) => c >= '฀' && c <= '๿';
            bool Kana(char c) => (c >= '぀' && c <= 'ヿ') || (c >= '一' && c <= '鿿');
            var texts = UnityEngine.Object.FindObjectsByType<ResearchFlowView>(FindObjectsSortMode.None)
                .SelectMany(v => v.GetComponentsInChildren<TMPro.TMP_Text>(false))
                .Where(t => t.enabled && t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text)).ToArray();
            var wrong = texts.Where(t => language == "Japanese" ? t.text.Any(Thai) : language == "Thai" && t.text.Any(Kana))
                .Select(t => HierarchyPath(t.transform) + ": " + t.text.Replace("\n", " ").Substring(0, Math.Min(60, t.text.Length))).ToArray();
            string HierarchyPath(Transform x) => x.parent && !x.parent.GetComponent<ResearchFlowView>() ? HierarchyPath(x.parent) + "/" + x.name : x.name;
            // Absence of the wrong script proves nothing on an empty view: the Task B title must be visible in the session language.
            string key = controller ? ResearchStrings.Get("task.B.title", language) : null;
            bool keyText = !string.IsNullOrEmpty(key) && texts.Any(t => t.text.Contains(key));
            return "language=" + language + " visible_texts=" + texts.Length + " key_text=" + (keyText ? 1 : 0) + " wrong_script=" + wrong.Length
                + (wrong.Length > 0 ? " :: " + string.Join(" | ", wrong.Take(8)) : "");
        }

        public static string Stop()
        {
            if (!Recording) return "not recording";
            EditorApplication.update -= Tick;
            index.Dispose(); index = null;
            foreach (var cam in cameras) if (cam) UnityEngine.Object.DestroyImmediate(cam.gameObject);
            cameras = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            return "stopped frames=" + frame;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Stop(); return; }
            double now = EditorApplication.timeSinceStartup;
            if (now - lastFrameAt < interval) return;
            lastFrameAt = now;
            string step = RunnerStep?.GetValue(null) as string ?? "";
            if (filters.Length > 0 && !filters.Any(f => step.Contains(f))) return;
            var row = new System.Text.StringBuilder();
            row.Append(frame).Append(',').Append((now - started).ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(step);
            for (int c = 0; c < cameras.Length; c++)
            {
                Grab(cameras[c], target, pixels);
                File.WriteAllBytes(Path.Combine(directory, "frames_" + cameraNames[c], frame.ToString("D5") + ".jpg"), pixels.EncodeToJPG(95));
                for (int a = 0; a < anchors.Length; a++)
                {
                    var anchor = anchors[a];
                    if (!anchor) { row.Append(",,,0"); continue; }
                    // Viewport, not screen: a camera without a target texture projects with the Game View's size and aspect.
                    cameras[c].targetTexture = target;
                    var p = cameras[c].WorldToViewportPoint(anchor.TransformPoint(anchorOffsets[a]));
                    cameras[c].targetTexture = null;
                    row.Append(',').Append((p.x * target.width).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                        .Append(((1 - p.y) * target.height).ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(p.z > 0 ? 1 : 0);
                }
            }
            index.WriteLine(row.ToString());
            frame++;
        }

        static Vector3 ParseVector(string[] v) =>
            new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));

        static Vector3 GuardCentre(ResearchS1TaskBMechanics task) =>
            task.frontGuard.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;

        static (string name, float[] v)[] Parse(string cameraList) => cameraList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(spec => spec.Split(':')).Select(p => (p[0].Trim(), p[1].Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray())).ToArray();

        static Camera MakeCamera((string name, float[] v) spec, Vector3 centre)
        {
            var cam = new GameObject("ResearchProtoCamera_" + spec.name).AddComponent<Camera>();
            cam.fieldOfView = spec.v[6]; cam.nearClipPlane = .01f; cam.enabled = false;
            cam.transform.position = centre + new Vector3(spec.v[0], spec.v[1], spec.v[2]);
            cam.transform.LookAt(centre + new Vector3(spec.v[3], spec.v[4], spec.v[5]));
            return cam;
        }

        static void Grab(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = previous;
            cam.targetTexture = null;
        }

        [Serializable]
        class CameraRecord
        {
            public Vector3 position, rotation, guardCentre;
            public float fieldOfView;
            public int width, height;
            public string missingAnchors;
        }
    }
}
#endif
