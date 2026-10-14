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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFocusedReaderInputChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Owner = -73401, Other = -73402;
        static T PublicField<T>(object fixture, string name) => (T)fixture.GetType().GetField(name).GetValue(fixture);
        static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Private).GetValue(source);
        static void Set(ResearchSessionFlowController controller, string name, object value)
            => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        static void Refresh(object fixture) => fixture.GetType().GetMethod("Refresh").Invoke(fixture, null);
        static void ScrollLateUpdate(ScrollRect scroll) => typeof(ScrollRect).GetMethod("LateUpdate", Private).Invoke(scroll, null);
        static object Capture(object fixture, string stage) => fixture.GetType().GetMethod("Capture").Invoke(fixture,
            new object[] { stage, PublicField<ResearchFlowView>(fixture, "View").referencePanel });
        static int Page(ResearchFlowView view) => (int)typeof(ResearchFlowView).GetProperty("ReferencePageIndex", Private).GetValue(view);
        static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };
        static object DisableState(ResearchDragHandle handle, ResearchSessionFlowController controller) => new
        {
            handle.enabled,
            handle.isActiveAndEnabled,
            active_in_hierarchy = handle.gameObject.activeInHierarchy,
            handle.IsDragging,
            controller_active_in_hierarchy = controller.gameObject.activeInHierarchy,
            editor_is_playing = EditorApplication.isPlaying,
            preview_scene = EditorSceneManager.IsPreviewScene(handle.gameObject.scene),
            scene_name = handle.gameObject.scene.name
        };
        static float[] Frame(ResearchFlowView view)
        {
            var panel = (RectTransform)view.referencePanel.transform; var position = panel.position; var rotation = panel.rotation;
            var title = Field<TMP_Text>(view, "referenceTitle").rectTransform;
            var previous = Field<Button>(view, "previousPage").GetComponent<RectTransform>();
            return new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w,
                panel.rect.width, panel.rect.height, title.anchoredPosition.x, title.anchoredPosition.y,
                previous.anchoredPosition.x, previous.anchoredPosition.y };
        }
        static bool Same(float[] before, float[] after) => before.Length == after.Length && before.Zip(after, (a, b) => Mathf.Abs(a - b)).All(d => d < .001f);

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Reader input fixture requires stopped Play Mode and no running XR subsystem.");
            string directory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "reader-input.json");
            if (File.Exists(path)) throw new IOException("Refusing to overwrite prior reader input evidence: " + path);
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the authored ResearchFlow scene with all three fonts.");
            var type = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
            var cursors = Resources.FindObjectsOfTypeAll<Oculus.Interaction.RayInteractorCursorVisual>().ToDictionary(c => c, c => c.HoverColor);
            var rows = new List<object>(); var captures = new List<object>(); var lifecycleDiagnostics = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed)
            { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
            {
                object fixture = null;
                try
                {
                    fixture = Activator.CreateInstance(type, new object[] { Path.Combine(directory, language.ToString()), donor, language });
                    var controller = PublicField<ResearchSessionFlowController>(fixture, "Controller");
                    var view = PublicField<ResearchFlowView>(fixture, "View"); var camera = PublicField<Camera>(fixture, "Camera");
                    Set(controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                    Set(controller, "ReferenceOpen", true); Set(controller, "ReferenceMinimized", false); Set(controller, "ReferenceSection", "user_manual");
                    Refresh(fixture);
                    var scroll = Field<ScrollRect>(view, "scroll"); var body = Field<TMP_Text>(view, "referenceBody");
                    var focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
                    view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
                    Check(scroll && focus && focus.scroll == scroll && focus.view == view, language + "/actual reader uses existing ScrollRect and ResearchSlateFocus",
                        new { focus_owner = focus ? focus.GetType().FullName : null, controller_axis_owner = "ResearchSessionFlowController.ReferenceScrollAxis / Right tracked IController.Primary2DAxis", synthetic_consumer_input = true });
                    body.ForceMeshUpdate(true, true);
                    bool longDocument = body.textInfo.pageCount >= 2 && scroll.content.rect.height > scroll.viewport.rect.height + 1;
                    Check(longDocument, language + "/existing fictitious-demo user manual has real TMP multipage content",
                        new { pages = body.textInfo.pageCount, content_height = scroll.content.rect.height, viewport_height = scroll.viewport.rect.height,
                            demo_content_version = ResearchDemoManual.ContentVersion, study_approved_content = false });
                    var pointer = new PointerEventData(EventSystem.current) { pointerId = Owner, button = PointerEventData.InputButton.Left };
                    var other = new PointerEventData(EventSystem.current) { pointerId = Other, button = PointerEventData.InputButton.Left };
                    if (longDocument)
                    {
                        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        var frame = Frame(view); captures.Add(Capture(fixture, "manual_before_scroll"));
                        float top = scroll.verticalNormalizedPosition;
                        focus.Scroll(-1, .6f);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, language + "/unfocused thumbstick consumer does not scroll", scroll.verticalNormalizedPosition);
                        focus.OnPointerEnter(pointer); focus.OnPointerExit(other);
                        Check(focus.Focused, language + "/unregistered pointer exit cannot clear current focus owner", focus.Focused);
                        foreach (var input in new[] { (.1f, .6f), (float.NaN, .6f), (float.PositiveInfinity, .6f), (-1f, 0f), (-1f, float.NaN) }) focus.Scroll(input.Item1, input.Item2);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - top) < .001f, language + "/deadzone and invalid timing/axis do not scroll", scroll.verticalNormalizedPosition);
                        focus.Scroll(-1, .6f); ScrollLateUpdate(scroll); body.ForceMeshUpdate(true, true);
                        Check(scroll.verticalNormalizedPosition < top - .001f && Page(view) > 1 && body.pageToDisplay == Page(view),
                            language + "/actual focused thumbstick consumer renders a distinct TMP page",
                            new { before = top, after = scroll.verticalNormalizedPosition, page = Page(view), rendered_page = body.pageToDisplay });
                        Check(Same(frame, Frame(view)), language + "/page change preserves world frame and header/pager aiming targets", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_thumbstick_scroll"));
                        controller.Flow.Pause(); float paused = scroll.verticalNormalizedPosition; focus.Scroll(-1, .6f);
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - paused) < .001f, language + "/paused flow rejects focused scrolling", scroll.verticalNormalizedPosition);
                        controller.Flow.ResearcherResume();
                        focus.OnPointerExit(pointer); focus.Scroll(-1, .6f);
                        Check(!focus.Focused && Mathf.Abs(scroll.verticalNormalizedPosition - paused) < .001f, language + "/matching exit releases focus and scroll ownership", focus.Focused);
                        scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        pointer.scrollDelta = new Vector2(0, -240);
                        scroll.OnScroll(pointer); ScrollLateUpdate(scroll); body.ForceMeshUpdate(true, true);
                        Check(scroll.verticalNormalizedPosition < .99f && Page(view) > 1 && body.pageToDisplay == Page(view),
                            language + "/real ScrollRect.OnScroll callback reaches the page listener",
                            new { scroll_delta = new[] { pointer.scrollDelta.x, pointer.scrollDelta.y }, normalized = scroll.verticalNormalizedPosition, page = Page(view), rendered_page = body.pageToDisplay });
                        Check(Same(frame, Frame(view)), language + "/OnScroll keeps reader frame stable", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_OnScroll"));
                        scroll.verticalNormalizedPosition = 1; ScrollLateUpdate(scroll);
                        pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = scroll.gameObject };
                        pointer.position = camera.WorldToScreenPoint(scroll.viewport.position);
                        scroll.OnInitializePotentialDrag(pointer); scroll.OnBeginDrag(pointer); pointer.position += Vector2.up * 120;
                        scroll.OnDrag(pointer); scroll.OnEndDrag(pointer); scroll.StopMovement(); ScrollLateUpdate(scroll);
                        Check(scroll.verticalNormalizedPosition < .99f, language + "/real content-surface drag scrolls without a narrow scrollbar", scroll.verticalNormalizedPosition);
                        Check(Same(frame, Frame(view)), language + "/content drag keeps the slate world frame stable", new { before = frame, after = Frame(view) });
                        captures.Add(Capture(fixture, "manual_content_drag"));
                        float saved = scroll.verticalNormalizedPosition;
                        foreach (string format in new[] { "step_by_step", "user_manual", "step_by_step", "user_manual" })
                        {
                            bool accepted = view.LocalCommand("reference_tab." + format, "None"); ScrollLateUpdate(scroll);
                            Check(accepted && controller.ReferenceSection == format && controller.ReferenceOpen && string.IsNullOrEmpty(controller.Error),
                                language + "/rapid real format command selects " + format, new { accepted, controller.ReferenceSection, controller.ReferenceOpen, controller.Error, frame = Frame(view) });
                        }
                        Check(Mathf.Abs(scroll.verticalNormalizedPosition - saved) < .001f, language + "/return to manual restores its actual scroll position",
                            new { before_switch = saved, after_return = scroll.verticalNormalizedPosition });
                        var beforeMinimize = Vector(view.referencePanel.transform.position);
                        bool minimized = view.LocalCommand("reference_minimize", "None");
                        Check(minimized && controller.ReferenceMinimized && controller.ReferenceOpen && Same(beforeMinimize, Vector(view.referencePanel.transform.position)),
                            language + "/real minimize keeps the reader world position", new { minimized, controller.ReferenceMinimized, position = Vector(view.referencePanel.transform.position) });
                        bool restored = view.LocalCommand("reference_restore", "None"); ScrollLateUpdate(scroll);
                        Check(restored && !controller.ReferenceMinimized && controller.ReferenceOpen && Same(beforeMinimize, Vector(view.referencePanel.transform.position))
                            && Mathf.Abs(scroll.verticalNormalizedPosition - saved) < .001f,
                            language + "/real restore preserves reader position and document progress", new { restored, controller.ReferenceMinimized, normalized = scroll.verticalNormalizedPosition, position = Vector(view.referencePanel.transform.position) });
                        captures.Add(Capture(fixture, "manual_restored"));
                    }

                    var handle = view.referencePanel.GetComponentsInChildren<ResearchDragHandle>(true).Single(h => h.panel == view.referencePanel);
                    pointer.pointerPressRaycast = new RaycastResult { module = view.referencePanel.GetComponent<GraphicRaycaster>(), gameObject = handle.gameObject };
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position);
                    handle.OnBeginDrag(pointer); var beforeDrag = view.referencePanel.transform.position;
                    Check(handle.IsDragging, language + "/existing owned handle accepts a valid programmatic press camera", handle.IsDragging);
                    other.pointerPressRaycast = pointer.pointerPressRaycast; other.position = pointer.position + Vector2.right * 100;
                    handle.OnDrag(other); handle.OnEndDrag(other);
                    Check(handle.IsDragging && Vector3.Distance(beforeDrag, view.referencePanel.transform.position) < .001f,
                        language + "/wrong pointer cannot move or finish the acquired panel", new { handle.IsDragging, position = Vector(view.referencePanel.transform.position) });
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position - camera.transform.right * .04f);
                    handle.OnDrag(pointer); handle.OnEndDrag(pointer);
                    Check(!handle.IsDragging && Vector3.Distance(beforeDrag, view.referencePanel.transform.position) > .001f,
                        language + "/matching pointer moves and releases the existing handle", new { handle.IsDragging, handle.MoveSamples, position = Vector(view.referencePanel.transform.position) });
                    captures.Add(Capture(fixture, "panel_owned_drag"));
                    pointer.position = camera.WorldToScreenPoint(handle.transform.position); handle.OnBeginDrag(pointer);
                    Check(handle.IsDragging, language + "/second drag owns a new acquisition", handle.IsDragging);
                    var beforeDisable = DisableState(handle, controller);
                    handle.enabled = false;
                    var afterPropertyDisable = DisableState(handle, controller);
                    bool explicitCallbackRequired = handle.IsDragging;
                    // This non-ExecuteAlways behaviour does not receive runtime lifecycle callbacks
                    // from an enabled-property change in the stopped Editor PreviewScene fixture.
                    // Exercise the existing cancellation callback without claiming runtime delivery.
                    if (explicitCallbackRequired)
                    {
                        var onDisable = typeof(ResearchDragHandle).GetMethod("OnDisable", Private);
                        if (onDisable == null) throw new MissingMethodException(typeof(ResearchDragHandle).FullName, "OnDisable");
                        onDisable.Invoke(handle, null);
                    }
                    var afterCallback = DisableState(handle, controller);
                    lifecycleDiagnostics.Add(new { language = language.ToString(), before_disable = beforeDisable,
                        after_enabled_property_disable = afterPropertyDisable, explicit_callback_required = explicitCallbackRequired,
                        callback = "existing ResearchDragHandle.OnDisable", after_callback = afterCallback,
                        scope = explicitCallbackRequired ? "Direct existing callback invocation in stopped Editor PreviewScene; automatic Unity runtime lifecycle delivery unverified."
                            : "Cancellation observed after enabled-property disable in stopped Editor PreviewScene; Unity runtime lifecycle delivery unverified." });
                    Check(!handle.IsDragging, language + "/existing disable callback cancels the acquired drag", new
                        { handle.IsDragging, explicit_callback_required = explicitCallbackRequired, release_cause = Field<string>(handle, "releaseCause") });
                    handle.enabled = true;
                    Check(controller.Flow.State == ResearchFlowState.Task1Active && controller.Record == null && string.IsNullOrEmpty(controller.Error),
                        language + "/fixture never evaluates or completes a study task", new { state = controller.Flow.State.ToString(), record_attached = controller.Record != null, controller.Error });
                }
                catch (Exception error) { Check(false, language + "/fixture completes", error.ToString()); }
                finally { if (fixture != null) ((IDisposable)fixture).Dispose(); foreach (var cursor in cursors) if (cursor.Key) cursor.Key.HoverColor = cursor.Value; }
            }
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews,
                "caller scene and previews preserved", new { caller.path, dirty, current_dirty = caller.isDirty, current_previews = EditorSceneManager.previewSceneCount });
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", failures, rows, captures, lifecycle_diagnostics = lifecycleDiagnostics,
                scope = "Actual ResearchSlateFocus.Scroll, ScrollRect callbacks/LateUpdate, existing LocalCommand format/minimize/restore and ResearchDragHandle ownership callbacks on isolated TEST fixture and authored fonts. Existing ResearchDragHandle.OnDisable explicitly invoked if stopped EditMode does not deliver it; this verifies callback cancellation, not Unity runtime disable lifecycle. Existing fictitious-demo manual, not approved study content. No native SDK pointer acquisition, physical thumbstick polling, Quest, consent, task outcome or wearer acceptance.", hardware_verified = false }, Formatting.Indented));
            if (failures != 0) throw new InvalidOperationException("FOCUSED_READER_INPUT_FAIL " + failures + "; " + path);
            return "FOCUSED_READER_INPUT_PASS " + rows.Count + "; " + path;
        }
    public static string BeginTemporal(string evidenceDirectory)
    {
        string directory = Path.GetFullPath(evidenceDirectory);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        if (Application.dataPath.Replace('\\', '/') != "D:/VR_TMU/XR-Research-V2/Assets" || EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
            throw new InvalidOperationException("Requires the bound project in stopped EditMode without XR.");
        Directory.CreateDirectory(directory);
        using (var marker = new FileStream(Path.Combine(directory, "started"), FileMode.CreateNew)) { }
        var caller = SceneManager.GetActiveScene(); bool dirty = caller.isDirty; int previews = EditorSceneManager.previewSceneCount;
        var currentEvents = EventSystem.current;
        Type LoadedType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        var cursorType = LoadedType("Oculus.Interaction.RayInteractorCursorVisual");
        var hoverColor = cursorType.GetProperty("HoverColor");
        var colors = Resources.FindObjectsOfTypeAll(cursorType).Select(x => new { cursor = (Component)x, color = (Color)hoverColor.GetValue(x) }).ToArray();
        var serializer = LoadedType("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) });
        var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().First(v => v.thaiFont && v.japaneseFont && v.englishFont);
        var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
        var capture = typeof(ResearchRecoveryTemporalChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        var moduleType = LoadedType("Oculus.Interaction.PointableCanvasModule");
        var processDrag = moduleType.GetMethod("ProcessDrag", hidden);
        var lateScroll = typeof(UnityEngine.UI.ScrollRect).GetMethod("LateUpdate", hidden);
        var rows = new List<object>(); var frames = new List<object>(); int failures = 0, updates = 0, languageIndex = 0, step = 0;
        var languages = new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese };
        object fixture = null; ResearchFlowView view = null; ResearchSessionFlowController controller = null;
        Camera camera = null; UnityEngine.UI.ScrollRect scroll = null; TMP_Text body = null; ResearchSlateFocus focus = null;
        Component module = null; PointerEventData pointer = null; EventSystem ownedEvents = null;
        ResearchUIAction pageAction = null; GameObject pressOwner = null; bool thumb = false, dragging = false;
        string stage = "initializing", error = null; int pressedPage = 0;
        float savedNormalized = 0; Vector3 headBase = default, panelBase = default; Quaternion headFacing = default, panelFacing = default;
        double start = EditorApplication.timeSinceStartup, lastTick = start, lastStep = start, lastCapture = start - 1, dragStart = 0;
        EditorApplication.CallbackFunction tick = null;
        void Check(bool pass, string name, object observed) { rows.Add(new { name, pass, observed, editor_time = EditorApplication.timeSinceStartup }); if (!pass) failures++; }
        int Page() => (int)typeof(ResearchFlowView).GetProperty("ReferencePageIndex", hidden).GetValue(view);
        void Set(string name, object value) => typeof(ResearchSessionFlowController).GetProperty(name).GetSetMethod(true).Invoke(controller, new[] { value });
        void Flush() { lateScroll.Invoke(scroll, null); Canvas.ForceUpdateCanvases(); body.ForceMeshUpdate(true, true); }
        void Click(string command)
        {
            var action = view.GetComponentsInChildren<ResearchUIAction>(true).First(x => x.command == command && x.gameObject.activeInHierarchy);
            if (typeof(ResearchUIAction).GetField("button", hidden).GetValue(action) == null) typeof(ResearchUIAction).GetMethod("OnEnable", hidden).Invoke(action, null);
            var data = new PointerEventData(ownedEvents) { pointerId = 953, button = PointerEventData.InputButton.Left };
            var owner = ExecuteEvents.ExecuteHierarchy(action.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(owner, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(owner, data, ExecuteEvents.pointerClickHandler);
            Flush();
        }
        void Setup()
        {
            var language = languages[languageIndex];
            fixture = Activator.CreateInstance(fixtureType, new object[] { Path.Combine(directory, language.ToString()), donor, language });
            controller = (ResearchSessionFlowController)fixtureType.GetField("Controller").GetValue(fixture);
            view = (ResearchFlowView)fixtureType.GetField("View").GetValue(fixture);
            camera = (Camera)fixtureType.GetField("Camera").GetValue(fixture);
            Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
            Set("ReferenceOpen", true); Set("ReferenceMinimized", false); Set("ReferenceSection", "user_manual");
            fixtureType.GetMethod("Refresh").Invoke(fixture, null);
            scroll = (UnityEngine.UI.ScrollRect)typeof(ResearchFlowView).GetField("scroll", hidden).GetValue(view);
            body = (TMP_Text)typeof(ResearchFlowView).GetField("referenceBody", hidden).GetValue(view);
            focus = view.referencePanel.GetComponent<ResearchSlateFocus>();
            camera.transform.SetParent(null, true); camera.scene = view.gameObject.scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(view.gameObject.scene);
            camera.aspect = 1; camera.fieldOfView = 90;
            view.referencePanel.GetComponent<Canvas>().worldCamera = camera;
            var go = new GameObject("Owned_LongReader_Module") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false); SceneManager.MoveGameObjectToScene(go, view.gameObject.scene);
            go.transform.SetParent(view.transform, true);
            ownedEvents = go.AddComponent<EventSystem>(); module = go.AddComponent(moduleType);
            typeof(BaseInputModule).GetField("m_EventSystem", hidden).SetValue(module, ownedEvents);
            pointer = new PointerEventData(ownedEvents) { pointerId = 952, button = PointerEventData.InputButton.Left, useDragThreshold = false, eligibleForClick = true };
            headBase = camera.transform.position; headFacing = camera.transform.rotation;
            panelBase = view.referencePanel.transform.position; panelFacing = view.referencePanel.transform.rotation;
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush();
            Check(body.textInfo.pageCount >= 2 && scroll.content.rect.height > scroll.viewport.rect.height + 1, language + "/real long document", new { pages = body.textInfo.pageCount, content = scroll.content.rect.height, viewport = scroll.viewport.rect.height, demo_content_version = ResearchDemoManual.ContentVersion, study_approved_content = false });
            if (body.textInfo.pageCount < 2 || scroll.content.rect.height <= scroll.viewport.rect.height + 1) throw new InvalidOperationException("No actual long-document range.");
            stage = language + "/first_page"; step = 0; lastStep = EditorApplication.timeSinceStartup;
        }
        void DisposeFixture() { if (fixture != null) ((IDisposable)fixture).Dispose(); fixture = null; }
        void Finish()
        {
            EditorApplication.update -= tick;
            try { DisposeFixture(); } catch (Exception ex) { error = (error ?? "") + ex; failures++; }
            foreach (var color in colors) if (color.cursor) hoverColor.SetValue(color.cursor, color.color);
            Check(SceneManager.GetActiveScene() == caller && caller.isDirty == dirty && EditorSceneManager.previewSceneCount == previews && EventSystem.current == currentEvents && !EditorApplication.isPlaying,
                "canonical scene, dirty flag, preview count, EventSystem and stopped mode preserved", new { caller.path, dirty, current_dirty = caller.isDirty, previews, current_previews = EditorSceneManager.previewSceneCount, editor_is_playing = EditorApplication.isPlaying });
            string summary = "LONG_READER_EDITOR_TEMPORAL_" + (failures == 0 ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(directory, "timeline.json"), (string)serializer.Invoke(null, new object[] { new { summary, failures, error, updates, rows, frames, hardware_verified = false,
                scope = "Continuous real EditorApplication.update and fixed-FOV Camera.Render in isolated preview scenes, stopped EditMode. TH/JP existing fictitious-demo manual and synthetic head/controller consumer/pointer inputs. ProcessDrag, ScrollRect.LateUpdate, ResearchUIAction acquisition/click and LocalCommand execute production code. Game clocks recorded but are not asserted to advance. No automatic Play lifecycle, native SDK ray acquisition, Quest/two-eye, approved content, study outcome or wearer claim." } }));
            Debug.Log(summary + "; " + Path.Combine(directory, "timeline.json"));
        }
        try { Setup(); }
        catch (Exception ex) { error = ex.ToString(); Check(false, "initialization", error); Finish(); throw; }
        tick = () =>
        {
            try
            {
                if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed || !donor) throw new InvalidOperationException("Bound stopped editor/fixture condition changed.");
                double now = EditorApplication.timeSinceStartup, delta = now - lastTick; lastTick = now; updates++;
                double elapsed = now - start; double interval = now - lastStep;
                camera.transform.SetPositionAndRotation(headBase + new Vector3(.025f * Mathf.Sin((float)elapsed * 3), .01f * Mathf.Sin((float)elapsed * 2), 0), headFacing * Quaternion.Euler(0, 2.5f * Mathf.Sin((float)elapsed * 2), 0));
                if (thumb) focus.Scroll(-1, (float)delta);
                if (dragging)
                {
                    var before = pointer.position; pointer.position = pointer.pressPosition + Vector2.up * (float)(now - dragStart) * 280;
                    pointer.delta = pointer.position - before; processDrag.Invoke(module, new object[] { pointer });
                }
                Flush();
                bool advanced = interval >= .55;
                if (advanced)
                {
                    lastStep = now; step++;
                    switch (step)
                    {
                        case 1:
                            stage = languages[languageIndex] + "/page_press_then_continuous_thumb_scroll";
                            pageAction = view.GetComponentsInChildren<ResearchUIAction>(true).First(x => x.command == "reference_next" && x.gameObject.activeInHierarchy);
                            if (typeof(ResearchUIAction).GetField("button", hidden).GetValue(pageAction) == null) typeof(ResearchUIAction).GetMethod("OnEnable", hidden).Invoke(pageAction, null);
                            pressedPage = Page(); pressOwner = ExecuteEvents.ExecuteHierarchy(pageAction.GetComponentInChildren<TMP_Text>().gameObject, pointer, ExecuteEvents.pointerDownHandler);
                            Check(pressOwner == pageAction.gameObject && pageAction.PointerHeld && pageAction.GetComponent<Button>().IsInteractable(), "page button acquisition is owned and enabled", new { page = pressedPage, acquired = pageAction.PointerHeld, owner = pressOwner ? pressOwner.name : null });
                            focus.OnPointerEnter(pointer); thumb = true; break;
                        case 2:
                            stage = languages[languageIndex] + "/stale_page_release"; thumb = false; int beforeRelease = Page();
                            Check(beforeRelease > pressedPage && beforeRelease < body.textInfo.pageCount && pageAction.PointerHeld && pageAction.GetComponent<Button>().IsInteractable(), "stale release is not masked by last-page disabling", new { pressedPage, beforeRelease, pages = body.textInfo.pageCount, acquired = pageAction.PointerHeld, interactable = pageAction.GetComponent<Button>().IsInteractable() });
                            ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pressOwner, pointer, ExecuteEvents.pointerClickHandler); Flush();
                            string loggedEvents;
                            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) loggedEvents = reader.ReadToEnd();
                            var rejection = loggedEvents.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).Select(Newtonsoft.Json.Linq.JObject.Parse).LastOrDefault(e => (string)e["event"] == "UI_CLICK_REJECTED" && (string)e["target"] == "reference_next");
                            Check(Page() == beforeRelease && !pageAction.PointerHeld && rejection != null && (string)rejection["result"] == "StaleAcquisition" && (string)rejection["reason"] == "context_changed", "stale page acquisition rejected with actual context-change event", new { pressedPage, beforeRelease, after = Page(), rejection });
                            Click("reference_next"); Check(Page() == beforeRelease + 1, "fresh next acquisition remains usable after rejection", new { beforeRelease, after = Page() });
                            Click("reference_previous"); Check(Page() == beforeRelease, "fresh previous acquisition returns to scrolled page", Page()); break;
                        case 3:
                            stage = languages[languageIndex] + "/owned_process_drag_with_head_motion";
                            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Flush();
                            pointer.position = camera.WorldToScreenPoint(scroll.viewport.position); pointer.pressPosition = pointer.position;
                            pointer.pointerPressRaycast = new RaycastResult { gameObject = body.gameObject, module = view.referencePanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() };
                            pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(body.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(body.gameObject);
                            ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
                            dragStart = now; dragging = true; break;
                        case 4:
                            stage = languages[languageIndex] + "/owned_drag_release"; dragging = false;
                            Check(pointer.dragging && pointer.pointerPress == pointer.pointerDrag && scroll.verticalNormalizedPosition < .99f && Page() == body.pageToDisplay,
                                "ProcessDrag moves real long content with stable input owner", new { pointer.dragging, press_owner = pointer.pointerPress ? pointer.pointerPress.name : null, drag_owner = pointer.pointerDrag ? pointer.pointerDrag.name : null, normalized = scroll.verticalNormalizedPosition, page = Page(), rendered_page = body.pageToDisplay });
                            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
                            pointer.dragging = false; pointer.pointerPress = null; pointer.pointerDrag = null; scroll.StopMovement(); focus.OnPointerExit(pointer); savedNormalized = scroll.verticalNormalizedPosition; break;
                        case 5: stage = languages[languageIndex] + "/user_format_steps"; Click("reference_tab.step_by_step"); Check(controller.ReferenceSection == "step_by_step", "explicit user format selection", controller.ReferenceSection); break;
                        case 6: stage = languages[languageIndex] + "/user_format_manual_restored"; Click("reference_tab.user_manual"); Check(Mathf.Abs(scroll.verticalNormalizedPosition - savedNormalized) < .001f, "manual position restored after explicit format return", new { savedNormalized, after = scroll.verticalNormalizedPosition }); break;
                        case 7: stage = languages[languageIndex] + "/user_minimize"; Click("reference_minimize"); Check(controller.ReferenceMinimized, "explicit minimize", controller.ReferenceMinimized); break;
                        case 8: stage = languages[languageIndex] + "/user_restore"; Click("reference_restore"); Check(!controller.ReferenceMinimized && Mathf.Abs(scroll.verticalNormalizedPosition - savedNormalized) < .001f, "restore retains manual progress", new { controller.ReferenceMinimized, normalized = scroll.verticalNormalizedPosition }); break;
                        case 9:
                            stage = languages[languageIndex] + "/user_last_page";
                            for (int n = 0; n < 32 && Page() < body.textInfo.pageCount; n++) Click("reference_next");
                            Check(Page() == body.textInfo.pageCount && body.pageToDisplay == Page(), "last real TMP page reached by explicit page control", new { page = Page(), rendered_page = body.pageToDisplay, pages = body.textInfo.pageCount }); break;
                        case 10:
                            Check(controller.Flow.State == ResearchFlowState.Task1Active && controller.Record == null && !controller.Flow.Paused && string.IsNullOrEmpty(controller.Error), "no real task/session evaluation", new { state = controller.Flow.State.ToString(), record = controller.Record, controller.Error });
                            DisposeFixture(); languageIndex++;
                            if (languageIndex == languages.Length) { Finish(); return; }
                            Setup(); break;
                    }
                }
                Check(Vector3.Distance(panelBase, view.referencePanel.transform.position) < .00001f && Quaternion.Angle(panelFacing, view.referencePanel.transform.rotation) < .001f,
                    "reference pose remains unchanged during head and reader input", view.referencePanel.Pose());
                if (advanced || now - lastCapture >= .10)
                {
                    lastCapture = now; string path = Path.Combine(directory, "frame-" + frames.Count.ToString("D4") + ".png");
                    capture.Invoke(null, new object[] { camera, path });
                    var surfaceFocus = pointer.pointerPress ? pointer.pointerPress.GetComponent<ResearchSlateFocus>() : null;
                    frames.Add(new { stage, path, editor_time = now, elapsed_editor_seconds = elapsed, frame = Time.frameCount, unscaled_time = Time.unscaledTime, language = languages[languageIndex].ToString(), page = Page(), rendered_page = body.pageToDisplay, pages = body.textInfo.pageCount, normalized = scroll.verticalNormalizedPosition, content_height = scroll.content.rect.height, viewport_height = scroll.viewport.rect.height, section = controller.ReferenceSection, controller.ReferenceMinimized, head_position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z }, head_rotation = new[] { camera.transform.rotation.x, camera.transform.rotation.y, camera.transform.rotation.z, camera.transform.rotation.w }, fov = camera.fieldOfView, aspect = camera.aspect, reference_pose = view.referencePanel.Pose(), reference_readable = view.referencePanel.IsReadableFromHead, recovery = view.RecoveryCanvas.gameObject.activeSelf, held = (surfaceFocus && surfaceFocus.PointerHeld) || (pageAction && pageAction.PointerHeld), reader_surface_held = surfaceFocus && surfaceFocus.PointerHeld, slate_root_held = focus.PointerHeld, pointer.dragging, press_owner = pointer.pointerPress ? pointer.pointerPress.name : null, drag_owner = pointer.pointerDrag ? pointer.pointerDrag.name : null, page_action_held = pageAction && pageAction.PointerHeld });
                }
                if (now - start > 90) throw new TimeoutException("Focused editor capture exceeded bounded duration.");
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception ex) { error = ex.ToString(); Check(false, "execution", error); Finish(); }
        };
        EditorApplication.update += tick; EditorApplication.QueuePlayerLoopUpdate();
        return "LONG_READER_EDITOR_TEMPORAL_STARTED; " + directory;
    }
    }
}
#endif
