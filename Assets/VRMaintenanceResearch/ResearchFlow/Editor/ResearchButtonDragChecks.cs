#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Oculus.Interaction;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public sealed class ResearchButtonEventSystemProbe : EventSystem
    {
        public void Bind() => base.OnEnable();
        public void Unbind() => base.OnDisable();
    }

    public sealed class ResearchButtonDragModuleProbe : PointableCanvasModule
    {
        protected override void Awake() { }
        protected override void Start() { }
        protected override void OnDestroy() { }
        public void Bind() => base.OnEnable();
        public void Step(PointerEventData data, bool pressed = false, bool released = false)
        {
            UpdatePointerEventData(data, pressed, released);
            if (!released) ProcessDrag(data);
        }
    }

    public sealed class ResearchButtonPanelDragProbe : MonoBehaviour, IDragHandler
    {
        public int Calls;
        public void OnDrag(PointerEventData data) { Calls++; transform.position += (Vector3)data.delta; }
    }

    public static class ResearchButtonDragChecks
    {
        public static string Run(string evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var failures = new List<string>();
            var cases = new List<object>();
            var originalEventSystem = EventSystem.current;
            var dirty = new Dictionary<Scene, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            { var existing = SceneManager.GetSceneAt(i); dirty.Add(existing, existing.isDirty); }
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            ResearchButtonEventSystemProbe system = null;
            try
            {
                root = new GameObject("ButtonDragFixture", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var events = new GameObject("FixtureEvents", typeof(ResearchButtonEventSystemProbe)) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(events, scene); events.transform.SetParent(root.transform, false);
                system = events.GetComponent<ResearchButtonEventSystemProbe>(); system.Bind(); system.pixelDragThreshold = 40;
                EventSystem.current = system;
                var module = events.AddComponent<ResearchButtonDragModuleProbe>(); module.Bind();
                foreach (bool toggle in new[] { false, true })
                {
                    Check(toggle, true, true, 0, false);
                    Check(toggle, true, true, 5, false);
                    Check(toggle, true, true, 80, false);
                    Check(toggle, true, true, 0, true);
                    Check(toggle, true, false, 80, false);
                }
                Check(false, false, true, 0, false);
                Check(false, false, true, 5, false);
                Check(false, false, true, 80, false);

                void Check(bool toggle, bool inScroll, bool scrollEnabled, float distance, bool offTarget)
                {
                    string name = $"{(toggle ? "toggle" : "button")}_{(inScroll ? "scroll" : "panel")}_{scrollEnabled}_{distance}_{offTarget}";
                    GameObject panel = null;
                    try
                    {
                        panel = Rect(name, root.transform, new Vector2(600, 400));
                        var panelDrag = panel.AddComponent<ResearchButtonPanelDragProbe>();
                        var initialPanelPosition = panel.transform.position;
                        UnityEngine.UI.ScrollRect scroll = null;
                        RectTransform content = null;
                        Transform parent = panel.transform;
                        if (inScroll)
                        {
                            var scrollObject = Rect("Scroll", parent, new Vector2(300, 200));
                            var viewport = Rect("Viewport", scrollObject.transform, new Vector2(300, 200));
                            viewport.AddComponent<UnityEngine.UI.Image>(); viewport.AddComponent<UnityEngine.UI.Mask>();
                            content = (RectTransform)Rect("Content", viewport.transform, new Vector2(300, 800)).transform;
                            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
                            content.anchoredPosition = Vector2.zero;
                            scroll = scrollObject.AddComponent<UnityEngine.UI.ScrollRect>();
                            scroll.viewport = (RectTransform)viewport.transform; scroll.content = content;
                            scroll.horizontal = false; scroll.inertia = false;
                            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
                            scroll.Rebuild(UnityEngine.UI.CanvasUpdate.PostLayout); scroll.enabled = scrollEnabled;
                            parent = content;
                        }
                        var target = Rect("Target", parent, new Vector2(200, 50));
                        var image = target.AddComponent<UnityEngine.UI.Image>();
                        int clicks = 0;
                        UnityEngine.UI.Toggle value = null;
                        if (toggle)
                        {
                            value = target.AddComponent<UnityEngine.UI.Toggle>(); value.targetGraphic = image;
                            value.onValueChanged.AddListener(_ => clicks++);
                        }
                        else
                        {
                            var button = target.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
                            button.onClick.AddListener(() => clicks++);
                        }
                        target.AddComponent<ResearchButtonDragAbsorber>();
                        Vector2 before = content ? content.anchoredPosition : Vector2.zero;
                        var pointer = new PointerEventData(system) { pointerId = -111, button = PointerEventData.InputButton.Left, position = new Vector2(200, 200), pointerCurrentRaycast = new RaycastResult { gameObject = target } };
                        module.Step(pointer, true);
                        bool forwarded = scroll && pointer.pointerDrag == scroll.gameObject;
                        if (distance > 0)
                        {
                            Move(new Vector2(200, 200 + distance));
                            if (distance > 40) Move(new Vector2(200, 200 + distance + 10));
                        }
                        bool dragging = pointer.dragging;
                        if (offTarget) pointer.pointerCurrentRaycast = new RaycastResult();
                        module.Step(pointer, false, true);
                        float pixels = content ? Vector2.Distance(before, content.anchoredPosition) : 0;
                        bool shouldScroll = inScroll && scrollEnabled && distance > 40;
                        int expectedClicks = shouldScroll || offTarget ? 0 : 1;
                        bool passed = clicks == expectedClicks && (!toggle || value.isOn == (expectedClicks == 1))
                            && (shouldScroll ? pixels > 1 : pixels < .001f) && panelDrag.Calls == 0
                            && panel.transform.position == initialPanelPosition && (!inScroll || scrollEnabled || !forwarded)
                            && (distance > 40 || !dragging) && !pointer.dragging && pointer.pointerPress == null;
                        cases.Add(new { name, passed, clicks, expectedClicks, toggleOn = value && value.isOn, pixels, panelDragCalls = panelDrag.Calls, forwarded, dragging });
                        if (!passed) failures.Add(name);
                        void Move(Vector2 position) { pointer.delta = position - pointer.position; pointer.position = position; module.Step(pointer); }
                    }
                    catch (Exception exception) { cases.Add(new { name, passed = false, exception = exception.ToString() }); failures.Add(name + " " + exception); }
                    finally { if (panel) UnityEngine.Object.DestroyImmediate(panel); }
                }
            }
            catch (Exception exception) { failures.Add("fixture " + exception); }
            finally
            {
                if (system) system.Unbind();
                if (root) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                if (originalEventSystem) EventSystem.current = originalEventSystem;
            }
            foreach (var pair in dirty)
            { var existing = pair.Key; if (!existing.IsValid() || existing.isDirty != pair.Value) failures.Add("canonical_scene_changed " + pair.Key); }
            if (EventSystem.current != originalEventSystem) failures.Add("event_system_not_restored");
            string summary = "BUTTON_DRAG_" + (failures.Count == 0 ? "PASS" : "FAIL") + " cases=" + cases.Count + " failures=" + failures.Count;
            File.WriteAllText(Path.Combine(evidenceDirectory, "result.json"), JsonConvert.SerializeObject(new { summary, passed = failures.Count == 0, scope = "Synthetic PointerEventData component test using installed Meta UpdatePointerEventData and ProcessDrag. Not native ray or human evidence.", hardware_verified = false, cases, failures }, Formatting.Indented));
            File.WriteAllText(Path.Combine(evidenceDirectory, "stdout.txt"), summary + Environment.NewLine);
            Console.WriteLine(summary);
            return summary;
        }
        static GameObject Rect(string name, Transform parent, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            node.transform.SetParent(parent, false); ((RectTransform)node.transform).sizeDelta = size; return node;
        }
    }
}
#endif
