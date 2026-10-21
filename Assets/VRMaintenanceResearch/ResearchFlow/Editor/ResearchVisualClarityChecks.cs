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

        // Run() and its stage checks asserted a retired tutorial control demo (AnimateDemo, Person/Controller art) and pre-S1
        // Task copy; deleted 21 Oct 2026. The Fixture below stays: ResearchCompactUIChecks and other suites build their views with it.

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
