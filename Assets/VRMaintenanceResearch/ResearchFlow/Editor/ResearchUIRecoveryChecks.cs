#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchUIRecoveryChecks
    {
        public static string RunExamineTargetStabilityRegression(string evidenceDirectory)
        {
            if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated target stability outside Play Mode.");
            var sceneBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirtyBefore = sceneBefore.isDirty;
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("ExamineTargetStabilityFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            var observations = new List<object>();
            string failure = null;
            GameObject recoveryObject = null;
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var head = new GameObject("SyntheticHead", typeof(Camera)); head.transform.SetParent(root.transform, false);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1;
                head.transform.position = new Vector3(50, 1.65f, 50);
                var chip = new GameObject("ExamineChip", typeof(RectTransform), typeof(Canvas)); chip.transform.SetParent(root.transform, false);
                var rect = (RectTransform)chip.transform; rect.sizeDelta = new Vector2(240, 96); rect.localScale = Vector3.one * .001f;
                var target = chip.AddComponent<ResearchExamineTarget>(); target.controller = controller; target.stationCanvas = chip.GetComponent<Canvas>();
                target.stationCanvas.renderMode = RenderMode.WorldSpace; target.stationCanvas.enabled = true;
                var place = typeof(ResearchExamineTarget).GetMethod("PlaceBesidePanels", BindingFlags.Instance | BindingFlags.NonPublic);
                var hover = typeof(ResearchExamineTarget).GetField("hoveringPointer", BindingFlags.Instance | BindingFlags.NonPublic);
                var presses = (Dictionary<int, (string context, string source)>)typeof(ResearchExamineTarget)
                    .GetField("presses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
                var bounds = new Bounds(head.transform.position + Vector3.forward * 1.2f, new Vector3(.3f, .3f, .1f));
                bool Place() => (bool)place.Invoke(target, new object[] { camera, bounds });
                void Check(bool value, string label)
                {
                    observations.Add(new { label, passed = value });
                    if (!value) throw new InvalidOperationException(label);
                }
                Check(Place(), "Initial contextual target has a readable beside-object placement");
                var position = rect.position; var rotation = rect.rotation;
                hover.SetValue(target, 17); head.transform.rotation = Quaternion.Euler(0, 12, 0);
                Check(Place() && Vector3.Distance(position, rect.position) < .0001f && Quaternion.Angle(rotation, rect.rotation) < .001f,
                    "Hovered target stays in world space when the head turns within readable bounds");
                hover.SetValue(target, int.MinValue); presses.Add(17, ("staged-context", "SimulatedController"));
                head.transform.rotation = Quaternion.Euler(0, -12, 0);
                Check(Place() && Vector3.Distance(position, rect.position) < .0001f && Quaternion.Angle(rotation, rect.rotation) < .001f,
                    "Acquired press retains the same target pose until release");
                head.transform.rotation = Quaternion.Euler(0, 150, 0);
                Check(!Place() && Vector3.Distance(position, rect.position) < .0001f,
                    "Unreadable acquired target is rejected without moving underneath the pointer");
                var originalHeadPosition = head.transform.position;
                head.transform.position = position + rect.right * 1.2f;
                head.transform.rotation = Quaternion.LookRotation(position - head.transform.position, Vector3.up);
                Check(!Place() && Vector3.Distance(position, rect.position) < .0001f,
                    "An edge-on acquired target is rejected even when its corners remain inside the viewport");
                head.transform.position = position + rect.forward * .8f;
                head.transform.rotation = Quaternion.LookRotation(position - head.transform.position, Vector3.up);
                Check(!Place() && Vector3.Distance(position, rect.position) < .0001f,
                    "An acquired target viewed from behind is rejected instead of showing reversed text");
                head.transform.position = originalHeadPosition;
                presses.Clear(); head.transform.rotation = Quaternion.Euler(0, 12, 0);
                Check(Place() && Vector3.Distance(position, rect.position) > .001f,
                    "After acquisition ends the existing contextual placement can recover normally");
                var view = root.AddComponent<ResearchFlowView>(); controller.view = view;
                recoveryObject = new GameObject("ActiveRecovery", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(ResearchPanelMotion)) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(recoveryObject, preview);
                view.RecoveryCanvas = recoveryObject.GetComponent<Canvas>(); view.RecoveryCanvas.renderMode = RenderMode.WorldSpace;
                var recoveryRect = (RectTransform)recoveryObject.transform; recoveryRect.sizeDelta = new Vector2(472, 96);
                recoveryRect.localScale = Vector3.one * .001f; recoveryRect.SetPositionAndRotation(rect.position, rect.rotation);
                var readable = typeof(ResearchExamineTarget).GetMethod("ReadablePlacement", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(!(bool)readable.Invoke(target, new object[] { camera, rect }),
                    "Contextual target rejects projected overlap with visible recovery controls");
                view.RecoveryCanvas.enabled = false;
                Check((bool)readable.Invoke(target, new object[] { camera, rect }),
                    "Disabled recovery canvas does not block contextual target placement");
            }
            catch (Exception ex) { failure = ex.ToString(); }
            finally { if (recoveryObject) UnityEngine.Object.DestroyImmediate(recoveryObject); UnityEngine.Object.DestroyImmediate(root); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != sceneBefore || sceneBefore.isDirty != dirtyBefore)
                failure = (failure ?? "") + "\nTarget fixture changed the active scene or dirty state.";
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(Path.Combine(evidenceDirectory, "examine-target-stability.json"), JsonConvert.SerializeObject(new {
                provenance = "isolated monoscopic geometry; staged hover/press, not tracked input", hardware_verified = false,
                passed = failure == null, observations, failure
            }, Formatting.Indented));
            if (failure != null) throw new InvalidOperationException(failure);
            return "EXAMINE_TARGET_STABILITY_PASS " + observations.Count + " assertions; synthetic geometry only";
        }

        public static string RunRecoveryProjectionRegression(string evidenceDirectory)
        {
            if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Run isolated recovery geometry outside Play Mode.");
            var sceneBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirtyBefore = sceneBefore.isDirty;
            int previewsBefore = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = new GameObject("RecoveryProjectionFixture") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            var observations = new List<object>();
            string failure = null;
            try
            {
                GameObject Child(string name, params Type[] components)
                {
                    var child = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(child, preview);
                    child.transform.SetParent(root.transform, false);
                    return child;
                }
                // Keep the partial view inactive: its Awake applies theme to live XR cursors.
                var viewRoot = Child("InactiveView"); viewRoot.SetActive(false);
                var view = viewRoot.AddComponent<ResearchFlowView>();
                var camera = Child("SyntheticHead", typeof(Camera)).GetComponent<Camera>();
                camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1;
                camera.transform.position = new Vector3(100, 1.65f, 100); view.Head = camera.transform;
                ResearchPanelMotion Panel(string id, Vector2 size, float depth)
                {
                    var canvas = Child(id, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)).GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    var rect = (RectTransform)canvas.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * .001f;
                    rect.position = camera.transform.position + Vector3.forward * depth;
                    var panel = canvas.gameObject.AddComponent<ResearchPanelMotion>();
                    panel.enabled = false; panel.view = view; panel.panelId = id; panel.movable = false;
                    return panel;
                }
                view.mainPanel = Panel("task_information", new Vector2(700, 500), 1.2f);
                view.referencePanel = Panel("reference", new Vector2(700, 500), 1.2f);
                view.referencePanel.gameObject.SetActive(false);
                var recovery = Panel("recovery", new Vector2(200, 96), .6f);
                var overMain = camera.transform.position + Vector3.forward * .6f;
                var besideMain = overMain + Vector3.right * .4f;
                void Check(string label, bool expected, Vector3 position)
                {
                    bool actual = recovery.PlacementClear(position, Quaternion.identity);
                    observations.Add(new { label, expected, actual, reason = recovery.LastRejection });
                    if (actual != expected) throw new InvalidOperationException(label + ": expected clear=" + expected + ", actual=" + actual);
                }
                var mainWorld = new Bounds(view.mainPanel.transform.position, new Vector3(.7f, .5f, .08f));
                var recoveryWorld = new Bounds(overMain, new Vector3(.25f, .146f, .08f));
                if (mainWorld.Intersects(recoveryWorld)) throw new InvalidOperationException("Fixture must separate world planes.");
                // Exercise both captioned footprints; these cases test projected separation, not viewport fit.
                var recoveryRect = (RectTransform)recovery.transform;
                foreach (var size in new[] { new Vector2(376, 96), new Vector2(472, 96) })
                {
                    recoveryRect.sizeDelta = size;
                    Check("captioned_" + size.x + "_main_overlap", false, overMain);
                    Check("captioned_" + size.x + "_main_separated", true, overMain + Vector3.right * .65f);
                    view.mainPanel.gameObject.SetActive(false); view.referencePanel.gameObject.SetActive(true);
                    Check("captioned_" + size.x + "_reference_overlap", false, overMain);
                    Check("captioned_" + size.x + "_reference_separated", true, overMain + Vector3.right * .65f);
                    view.mainPanel.gameObject.SetActive(true); view.referencePanel.gameObject.SetActive(false);
                }
                recoveryRect.sizeDelta = new Vector2(200, 96); // Preserve the existing exaggerated-eye parallax fixture below.
                Check("main_projected_overlap_without_world_overlap", false, overMain);
                Check("beside_visible_main", true, besideMain);
                view.mainPanel.gameObject.SetActive(false); Check("inactive_main", true, overMain);
                view.mainPanel.gameObject.SetActive(true);
                var mainCanvas = view.mainPanel.GetComponent<Canvas>();
                mainCanvas.enabled = false; Check("disabled_main_canvas", true, overMain); mainCanvas.enabled = true;
                var mainGroup = view.mainPanel.GetComponent<CanvasGroup>();
                mainGroup.alpha = 0; Check("transparent_main", true, overMain); mainGroup.alpha = 1;
                var block = typeof(ResearchPanelMotion).GetMethod("SetPlacementBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
                block.Invoke(view.mainPanel, new object[] { true }); Check("blocked_main", true, overMain);
                block.Invoke(view.mainPanel, new object[] { false });
                view.mainPanel.gameObject.SetActive(false); view.referencePanel.gameObject.SetActive(true);
                Check("reference_projected_overlap_without_world_overlap", false, overMain);
                Check("beside_visible_reference", true, besideMain);
                view.referencePanel.gameObject.SetActive(false); view.mainPanel.gameObject.SetActive(true);
                if (!view.mainPanel.PlacementClear(overMain, Quaternion.identity)) throw new InvalidOperationException("Main placement policy changed.");

                // Exaggerated synthetic eye translation proves parallax policy; this is not a native IPD/comfort test.
                var eyeCheck = typeof(ResearchPanelMotion).GetMethod("RecoveryClearEye", BindingFlags.Instance | BindingFlags.NonPublic);
                if (eyeCheck == null) throw new InvalidOperationException("Recovery per-eye clearance missing.");
                var nativeView = camera.worldToCameraMatrix;
                var projection = camera.projectionMatrix;
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Translate(Vector3.left * .4f) * nativeView);
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, nativeView);
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, projection);
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, projection);
                void EyeCheck(string label, Camera.MonoOrStereoscopicEye eye, bool expected)
                {
                    bool actual = (bool)eyeCheck.Invoke(recovery, new object[] { besideMain, Quaternion.identity, camera, eye });
                    observations.Add(new { label, expected, actual, reason = recovery.LastRejection });
                    if (actual != expected) throw new InvalidOperationException(label + ": per-eye projected clearance mismatch.");
                }
                EyeCheck("mono_beside_peer", Camera.MonoOrStereoscopicEye.Mono, true);
                EyeCheck("left_eye_parallax_overlap", Camera.MonoOrStereoscopicEye.Left, false);
                EyeCheck("right_eye_clear", Camera.MonoOrStereoscopicEye.Right, true);
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, nativeView);
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, Matrix4x4.Translate(Vector3.left * .4f) * nativeView);
                EyeCheck("left_eye_clear_after_swap", Camera.MonoOrStereoscopicEye.Left, true);
                EyeCheck("right_eye_parallax_overlap_after_swap", Camera.MonoOrStereoscopicEye.Right, false);
            }
            catch (Exception exception) { failure = exception.ToString(); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
            bool preserved = sceneBefore.IsValid() && UnityEngine.SceneManagement.SceneManager.GetActiveScene() == sceneBefore
                && sceneBefore.isDirty == dirtyBefore && UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount == previewsBefore;
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(Path.Combine(evidenceDirectory, "recovery-projection-regression.json"), JsonConvert.SerializeObject(new
            {
                passed = failure == null && preserved, observations, failure, scene_preserved = preserved,
                dirty_before = dirtyBefore, dirty_after = sceneBefore.isDirty,
                previews_before = previewsBefore, previews_after = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount,
                synthetic_eye_matrices = true, hardware_verified = false
            }, Formatting.Indented));
            if (failure != null || !preserved) throw new InvalidOperationException(failure ?? "Recovery fixture changed live scene state.");
            return "UI_RECOVERY_PROJECTION_PASS checks=" + observations.Count + " scene_preserved=True hardware_verified=False";
        }

        public static string InstallNativeRecovery()
        {
            if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Install scene recovery adapters outside Play Mode.");
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!view || !view.controller || !view.Head) throw new InvalidOperationException("Canonical view/controller/head required.");
            view.ApplyVisualTheme();
            var canvas = view.RecoveryCanvas; canvas.worldCamera = view.Head.GetComponent<Camera>();
            if (!canvas.GetComponentInChildren<Oculus.Interaction.PointableCanvas>(true))
            {
                Oculus.Interaction.Editor.QuickActions.QuickActionsAPI.AddRayCanvasInteraction(canvas.gameObject, false);
                Oculus.Interaction.Editor.QuickActions.QuickActionsAPI.AddPokeCanvasInteraction(canvas.gameObject, false);
            }
            foreach (var guard in view.controller.guards)
                if (guard)
                {
                    guard.uiClearanceColliders = view.ParticipantCanvases.SelectMany(c => c.GetComponentsInChildren<Collider>(true)).ToArray();
                    guard.uiClearanceSurfaces = view.ParticipantCanvases.SelectMany(c => c.GetComponentsInChildren<Oculus.Interaction.RayInteractable>(true)).ToArray();
                    UnityEditor.EditorUtility.SetDirty(guard);
                    if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(guard)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(guard);
                }
            UnityEditor.EditorUtility.SetDirty(view);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            return "UI_NATIVE_RECOVERY_ADAPTERS_INSTALLED; root must preserve scene dirty state and save approved batch";
        }
        public static string RunLightBaseline()
        {
            if (ResearchUITheme.Glass.r < .85f || ResearchUITheme.Glass.g < .85f || ResearchUITheme.Glass.a < .99f
                || ResearchUITheme.Ink.r > .25f || ResearchUITheme.Ink.g > .3f)
                throw new InvalidOperationException("Reading surface remains navy/translucent with light text.");
            return "UI_LIGHT_PASS opaque pale surface and dark text tokens";
        }

        public static string RunReaderPolicyBaseline()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Load canonical ResearchFlow scene first.");
            var root = new GameObject("UIRecoveryPolicyFixture"); root.SetActive(false);
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.controller = fixture; fixture.view = view;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_UI_POLICY", test_data = true, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                Set("ReferenceOpen", true); Set("ReferenceSection", "user_manual");
                view.Refresh();
                var content = typeof(ResearchFlowView).GetField("mainContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view) as GameObject;
                if (!content.activeSelf) throw new InvalidOperationException("Opening Reference auto-minimizes the task surface.");
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                view.Refresh();
                if (content.activeSelf) throw new InvalidOperationException("Tutorial Reference still has a detached visible coach.");
                return "UI_READER_POLICY_PASS independent task surface and integrated Tutorial reader";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunRecoveryBaseline()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Load canonical ResearchFlow scene first.");
            var root = new GameObject("UIRecoveryCanvasFixture"); root.SetActive(false);
            var clone = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont).gameObject;
            try
            {
                var view = clone.GetComponent<ResearchFlowView>(); view.ApplyVisualTheme();
                var safety = view.transform.Find("PanelRecoveryUtility");
                if (!safety || !safety.GetComponent<Canvas>()) throw new InvalidOperationException("Content concealment has no independent recovery/Pause Canvas.");
                foreach (var name in new[] { "RecoverPanels", "Pause" })
                    if (!safety.Find(name)?.GetComponent<UnityEngine.UI.Button>()) throw new InvalidOperationException("Safety utility missing " + name);
                return "UI_RECOVERY_SURFACE_PASS independent recovery and Pause Canvas";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunOrientationRegression()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Load canonical ResearchFlow scene first.");
            var root = new GameObject("UIRecoveryOrientationFixture"); root.SetActive(false);
            var clone = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont).gameObject;
            var head = new GameObject("UIRecoverySyntheticHead", typeof(Camera));
            try
            {
                var view = clone.GetComponent<ResearchFlowView>(); view.controller = null; view.Head = head.transform;
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1;
                head.transform.position = new Vector3(100, 1.65f, 100);
                var panel = view.mainPanel; panel.view = view; panel.centered = false;
                ((RectTransform)panel.transform).sizeDelta = new Vector2(260, 76);
                var fits = typeof(ResearchPanelMotion).GetMethod("FitsView", BindingFlags.Instance | BindingFlags.NonPublic);
                var position = head.transform.position + Vector3.forward;
                bool Fits(float yaw) => (bool)fits.Invoke(panel, new object[] { position, Quaternion.Euler(0, yaw, 0) });
                if (!Fits(0)) throw new InvalidOperationException("Readable front collapsed control rejected.");
                if (Fits(180)) throw new InvalidOperationException("Rear-face collapsed control accepted (mirrored text path).");
                if (Fits(90)) throw new InvalidOperationException("Edge-on collapsed control accepted (unreadable ray path).");
                return "UI_ORIENTATION_PASS front=true rear=false edge=false; synthetic geometry only";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(head); }
        }

        public static string RunNativeLowEyeRegression()
        {
            if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Offline geometry fixture only.");
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            var root = new GameObject("NativeLowEyeGeometryFixture"); root.SetActive(false);
            var head = new GameObject("RecordedNativePoseReplay", typeof(Camera));
            GameObject ui = null;
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                ui = view.gameObject; view.controller = fixture; fixture.view = view; view.Head = head.transform;
                typeof(ResearchSessionFlowController).GetProperty("Record").GetSetMethod(true).Invoke(fixture, new object[] {
                    new ParticipantRecord { participant_id = "DEV_NATIVE_POSE_REPLAY", test_data = true,
                        language = ParticipantLanguage.Thai, input_mode = ExperimentInputMode.Controller } });
                typeof(ResearchSessionFlowController).GetProperty("Flow").GetSetMethod(true).Invoke(fixture,
                    new object[] { new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 0) });
                head.transform.SetPositionAndRotation(new Vector3(9.719731f, .9919633f, 4.94887352f),
                    new Quaternion(-.0533420555f, .9984726f, .0143769318f, .000757233356f));
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.aspect = 1848f / 1936;
                // Keep the controller inactive; this replays recorded geometry and never creates native input or a session.
                view.transform.SetParent(null); view.gameObject.SetActive(true);
                foreach (float fov in new[] { 60f, 90f, 100f, 110f })
                {
                    camera.fieldOfView = fov; view.Refresh(); Canvas.ForceUpdateCanvases();
                    view.mainPanel.Recenter(false, false);
                    if (view.mainPanel.PlacementBlocked || !view.mainPanel.IsReadableFromHead ||
                        !view.mainPanel.PlacementClear(view.mainPanel.transform.position, view.mainPanel.transform.rotation))
                        throw new InvalidOperationException("Recorded low-eye Tutorial cannot recover at FOV " + fov + ": " + view.mainPanel.LastRejection);
                }
                return "NATIVE_LOW_EYE_GEOMETRY_PASS FOV=60/90/100/110; recorded pose replay only; hardware_verified=false";
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(head); }
        }

        public static string RunTutorialGrowthRegression(string path)
        {
            if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Offline fixture only.");
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            var root = new GameObject("TutorialGrowthFixture"); root.SetActive(false);
            var head = new GameObject("TutorialGrowthSyntheticHead", typeof(Camera));
            GameObject ui = null;
            var observations = new List<object>();
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                ui = view.gameObject; view.controller = fixture; fixture.view = view; view.Head = head.transform;
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 60; camera.aspect = 16f / 9;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                view.transform.SetParent(null); ui.SetActive(true); view.enabled = false; view.mainPanel.enabled = false;
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                    foreach (bool held in new[] { false, true })
                    {
                        head.transform.SetPositionAndRotation(new Vector3(100, 1.65f, 100), Quaternion.identity);
                        Set("Record", new ParticipantRecord { participant_id = "DEV_GROWTH", test_data = true, language = language, input_mode = ExperimentInputMode.Controller });
                        Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3)); view.Refresh();
                        view.mainPanel.transform.SetPositionAndRotation(head.transform.position + new Vector3(.2f, -.2f, 1), Quaternion.identity);
                        float beforeHeight = ((RectTransform)view.mainPanel.transform).rect.height;
                        if (!view.mainPanel.IsReadableFromHead) throw new InvalidOperationException("Smaller authored cue fixture must fit.");
                        var original = view.mainPanel.transform.position;
                        var handle = view.mainPanel.GetComponentInChildren<ResearchDragHandle>(true);
                        var dragging = typeof(ResearchDragHandle).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic);
                        dragging.SetValue(handle, held);
                        Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 4)); view.Refresh();
                        float afterHeight = ((RectTransform)view.mainPanel.transform).rect.height;
                        bool deferred = !held || view.mainPanel.transform.position == original;
                        dragging.SetValue(handle, false);
                        var release = typeof(ResearchFlowView).GetMethod("RevalidateMainLayoutIfReady", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (release != null) release.Invoke(view, null);
                        bool readable = view.mainPanel.IsReadableFromHead && !view.mainPanel.PlacementBlocked;
                        var settled = view.mainPanel.transform.position; head.transform.position += Vector3.right * .02f; view.Refresh();
                        bool stable = view.mainPanel.transform.position == settled;
                        observations.Add(new { language = language.ToString(), held, beforeHeight, afterHeight, deferred, readable, stable, hardware_verified = false });
                        File.WriteAllText(path, JsonConvert.SerializeObject(new { observations }, Formatting.Indented));
                        if (afterHeight <= beforeHeight || !deferred || !readable || !stable) throw new InvalidOperationException("Same-state authored Tutorial growth is unrecoverable: " + language + "/held=" + held);
                    }
                return "TUTORIAL_GROWTH_PASS authored TH/JP same-state bounds, held defer/release, world-stability; " + path;
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(head); }
        }

        public static string RunStereoBoundsRegression(string path)
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            var root = new GameObject("StereoBoundsFixture"); root.SetActive(false);
            var head = new GameObject("SyntheticStereoMatrixCamera", typeof(Camera));
            try
            {
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.Head = head.transform; var panel = view.mainPanel;
                ((RectTransform)panel.transform).sizeDelta = new Vector2(260, 76);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1;
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, camera.worldToCameraMatrix);
                camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, camera.worldToCameraMatrix);
                var narrow = Matrix4x4.Frustum(-.03f, .03f, -.1f, .1f, .1f, 10);
                var wide = Matrix4x4.Frustum(-.1f, .1f, -.1f, .1f, .1f, 10);
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, narrow);
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, wide);
                var position = new Vector3(.45f, 0, 1);
                bool mono = panel.IsReadableFromHead; // Use the intended candidate, not the construction pose.
                panel.transform.SetPositionAndRotation(position, Quaternion.identity); mono = panel.IsReadableFromHead;
                var method = typeof(ResearchPanelMotion).GetMethod("FitsViewEye", BindingFlags.Instance | BindingFlags.NonPublic);
                bool? left = method == null ? (bool?)null : (bool)method.Invoke(panel, new object[] { position, Quaternion.identity, camera, Camera.MonoOrStereoscopicEye.Left });
                bool? right = method == null ? (bool?)null : (bool)method.Invoke(panel, new object[] { position, Quaternion.identity, camera, Camera.MonoOrStereoscopicEye.Right });
                File.WriteAllText(path, JsonConvert.SerializeObject(new { mono, left, right, synthetic_eye_matrices = true, hardware_verified = false }, Formatting.Indented));
                if (!mono || left != false || right != true) throw new InvalidOperationException("Mono fit does not establish both-eye fit.");
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, wide);
                camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, narrow);
                if (!(bool)method.Invoke(panel, new object[] { position, Quaternion.identity, camera, Camera.MonoOrStereoscopicEye.Left }) ||
                    (bool)method.Invoke(panel, new object[] { position, Quaternion.identity, camera, Camera.MonoOrStereoscopicEye.Right }))
                    throw new InvalidOperationException("Right-only clipping was accepted.");
                return "STEREO_BOUNDS_PASS real Camera API with synthetic asymmetric matrices; native branch requires next authorized APK";
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(head); }
        }

        public static string RunPendingClickBaseline()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Load canonical ResearchFlow scene first.");
            var root = new GameObject("UIRecoveryClickFixture"); root.SetActive(false);
            GameObject ui = null;
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                ui = view.gameObject; view.controller = fixture; fixture.view = view;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_UI_CLICK", test_data = true, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                view.transform.SetParent(null); view.gameObject.SetActive(true); view.Refresh();
                var action = view.mainPanel.transform.Find("Expanded/Actions/Action_0").GetComponent<ResearchUIAction>();
                action.gameObject.SetActive(true);
                typeof(ResearchUIAction).GetField("button", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(action, action.GetComponent<UnityEngine.UI.Button>());
                var pointer = new UnityEngine.EventSystems.PointerEventData(null) { pointerId = -97531 };
                action.command = "reference"; action.OnPointerDown(pointer);
                action.command = "main_minimize"; action.OnPointerUp(pointer);
                typeof(ResearchUIAction).GetMethod("Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(action, null);
                var content = typeof(ResearchFlowView).GetField("mainContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view) as GameObject;
                if (!content.activeSelf) throw new InvalidOperationException("Pending press activated a different command after state rebinding.");
                return "UI_PENDING_CLICK_PASS stale rebinding rejected; synthetic callback only";
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunExamineReleaseBaseline()
        {
            var root = new GameObject("ExamineReleaseControllerFixture"); root.SetActive(false);
            var chip = new GameObject("ExamineReleaseChip", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            chip.SetActive(false);
            try
            {
                var controller = root.AddComponent<ResearchSessionFlowController>();
                var port = root.AddComponent<ResearchTaskPort>(); port.demoMode = true; port.taskId = "A"; port.SetInteractionAllowed(true);
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(controller, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_EXAMINE_RELEASE", test_data = true, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5));
                var target = chip.AddComponent<ResearchExamineTarget>(); target.controller = controller; target.task = port; target.stableTarget = "A.module";
                target.stationCanvas = chip.GetComponent<Canvas>(); target.stationGroup = chip.GetComponent<CanvasGroup>();
                chip.SetActive(true);
                typeof(ResearchExamineTarget).GetMethod("SetFocus", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { target });
                int events = 0; port.Behavior += (kind, id, before, after) => { if (kind == "EXAMINE") events++; };
                target.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(null) { pointerId = -97532 });
                typeof(ResearchExamineTarget).GetMethod("Examine", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
                if (events != 0) throw new InvalidOperationException("Examine release without acquisition logged deliberate behavior.");
                return "EXAMINE_RELEASE_PASS unacquired release rejected; synthetic callbacks only";
            }
            finally { UnityEngine.Object.DestroyImmediate(chip); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunVisibilityFaceBaseline()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Canonical view required.");
            var root = new GameObject("VisibilityFaceController"); root.SetActive(false); GameObject ui = null;
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "A"; port.demoMode = true; fixture.taskPorts = new[] { port };
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont); ui = view.gameObject;
                view.controller = fixture; fixture.view = view;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_VISIBILITY_FACE", test_data = true, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5)); Set("ReferenceOpen", true); Set("ReferenceSection", "user_manual");
                view.transform.SetParent(null, false); ui.SetActive(true); view.enabled = false;
                var head = new GameObject("SyntheticVisibilityHead", typeof(Camera)); head.transform.SetParent(view.transform);
                head.transform.position = new Vector3(100, 1.65f, 100); var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                view.Head = head.transform; view.Refresh(); view.mainPanel.Recenter(false, false); view.referencePanel.Recenter(false, false);
                view.referencePanel.GetComponent<CanvasGroup>().alpha = 1;
                if (!view.IsReferenceActuallyVisible) throw new InvalidOperationException("Visibility fixture has no front content exposure; incidental setup failure.");
                view.referencePanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                if (view.IsReferenceActuallyVisible) throw new InvalidOperationException("Rear-face content counted as actual exposure before concealment.");
                return "REFERENCE_FACE_VISIBILITY_PASS rear exposure rejected before LateUpdate; synthetic only";
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunTutorialVisibilityBaseline()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Canonical view required.");
            var root = new GameObject("TutorialVisibilityController"); root.SetActive(false); GameObject ui = null;
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont); ui = view.gameObject;
                fixture.view = view; view.controller = fixture;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_TUTORIAL_VISIBILITY", test_data = true, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 0));
                ui.transform.SetParent(null, false); ui.SetActive(true); view.enabled = false;
                var head = new GameObject("SyntheticTutorialHead", typeof(Camera)); head.transform.SetParent(view.transform);
                head.transform.position = new Vector3(100, 1.65f, 100); var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                view.Head = head.transform; view.Refresh(); view.mainPanel.Recenter(false, false);
                var group = view.mainPanel.GetComponent<CanvasGroup>(); group.alpha = 1;
                if (!view.TutorialActionReady(fixture.TutorialFact)) throw new InvalidOperationException("Tutorial visibility fixture lacks its front visible prompt; incidental failure.");
                group.alpha = 0;
                if (view.TutorialActionReady(fixture.TutorialFact)) throw new InvalidOperationException("Alpha-hidden Tutorial prompt remained eligible for action credit.");
                group.alpha = 1; view.mainPanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                if (view.TutorialActionReady(fixture.TutorialFact)) throw new InvalidOperationException("Rear-face Tutorial prompt remained eligible before concealment.");
                return "TUTORIAL_VISIBILITY_PASS alpha-hidden/rear prompt ineligible; synthetic query only";
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunTutorialLayoutChecks(string evidenceFolder)
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Canonical donor fonts required.");
            var root = new GameObject("LocalizedTutorialLayoutController"); root.SetActive(false);
            GameObject ui = null;
            var observations = new List<object>(); var failures = new List<string>();
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var item = root.AddComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject>();
                var held = (HashSet<int>)typeof(VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject).GetField("pointers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
                fixture.trainingObjects = new[] { item };
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.controller = fixture; fixture.view = view; ui = view.gameObject;
                var head = new GameObject("LocalizedTutorialLayoutHead", typeof(Camera)); head.transform.SetParent(view.transform);
                head.transform.position = new Vector3(100, 1.65f, 100); head.GetComponent<Camera>().enabled = false;
                head.GetComponent<Camera>().fieldOfView = 90; head.GetComponent<Camera>().aspect = 16f / 9; view.Head = head.transform;
                ui.transform.SetParent(null, false); ui.SetActive(true); view.enabled = false;
                foreach (var motion in ui.GetComponentsInChildren<ResearchPanelMotion>(true)) motion.enabled = false;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                var facts = new[] { new[] { "teleport", "snap", "roomscale" }, new[] { "select" }, new[] { "grab", "move", "rotate", "release" }, new[] { "reference", "switch", "reposition", "close" }, new[] { "inspect" } };
                var credited = (HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture);
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                    foreach (var mode in new[] { ExperimentInputMode.Controller, ExperimentInputMode.HandTracking })
                        for (int step = 0; step < facts.Length; step++)
                            for (int index = 0; index < facts[step].Length; index++)
                            {
                                Set("Record", new ParticipantRecord { participant_id = "DEV_TUTORIAL_LAYOUT", test_data = true, language = language, input_mode = mode });
                                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, step >= 3 ? ResearchFlowState.TutorialReferenceInspect : ResearchFlowState.TutorialInteractions, step));
                                credited.Clear(); foreach (string prior in facts[step].Take(index)) credited.Add(prior);
                                held.Clear(); if (step == 2 && index > 0) held.Add(int.MinValue);
                                Set("ReferenceOpen", step == 3 && index > 0); Set("ReferenceMinimized", false); Set("ReferenceSection", "user_manual");
                                view.Refresh(); Canvas.ForceUpdateCanvases();
                                var text = fixture.ReferenceOpen ? view.referencePanel.transform.Find("Expanded/TutorialCue").GetComponent<TMPro.TMP_Text>()
                                    : view.mainPanel.transform.Find("Expanded/Body").GetComponent<TMPro.TMP_Text>();
                                text.ForceMeshUpdate(true);
                                bool fit = !text.isTextOverflowing && text.preferredHeight <= text.rectTransform.rect.height + 1;
                                bool authoredLines = fixture.ReferenceOpen || text.textInfo.lineCount == System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "").Split('\n').Length;
                                if (step == 4)
                                {
                                    string action = ResearchStrings.Get(fixture.TutorialInstructionKey, language.ToString());
                                    int actionLines = text.textInfo.characterInfo.Take(Mathf.Min(action.Length, text.textInfo.characterCount)).Where(ch => ch.isVisible).Select(ch => ch.lineNumber).Distinct().Count();
                                    authoredLines = actionLines == action.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
                                    if (language == ParticipantLanguage.Japanese)
                                        fit &= text.textInfo.lineInfo.Take(text.textInfo.lineCount).Where(line => line.visibleCharacterCount > 0).All(line => line.visibleCharacterCount >= 2);
                                }
                                bool separated = true;
                                if (fixture.ReferenceOpen)
                                {
                                    var reader = view.referencePanel.transform;
                                    var cue = RectTransformUtility.CalculateRelativeRectTransformBounds(reader, text.transform);
                                    foreach (var tab in reader.GetComponentsInChildren<ResearchUIAction>(false).Where(action => action.command.StartsWith("reference_tab.")))
                                        separated &= cue.min.y - RectTransformUtility.CalculateRelativeRectTransformBounds(reader, tab.transform).max.y >= ResearchUITheme.SpaceL - 1;
                                    var demo = reader.Find("Expanded/TutorialDemo");
                                    separated &= cue.min.x - RectTransformUtility.CalculateRelativeRectTransformBounds(reader, demo).max.x >= ResearchUITheme.SpaceL - 1;
                                }
                                string name = language + "/" + mode + "/" + facts[step][index];
                                observations.Add(new { name, fact = fixture.TutorialFact, instruction = fixture.TutorialInstructionKey, text = text.text,
                                    font_size = text.fontSize, preferred_height = text.preferredHeight, rect_height = text.rectTransform.rect.height,
                                    rendered_lines = text.textInfo.lineCount, fit, authored_lines = authoredLines, separated,
                                    synthetic_held_state = step == 2 && index > 0, hardware_verified = false });
                                if (!fit || !authoredLines || !separated) failures.Add(name + ": preferred=" + text.preferredHeight + ", rect=" + text.rectTransform.rect.height + ", lines=" + text.textInfo.lineCount + ", authored=" + authoredLines + ", separated=" + separated);
                            }
                string folder = Path.GetFullPath(evidenceFolder); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "tutorial-localized-layout-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(new { passed = failures.Count == 0, observations, failures, hardware_verified = false, scope = "Actual TMP layout in isolated Editor fixture; explicit synthetic facts/held state, no physical input or language approval" }, Formatting.Indented));
                if (failures.Count != 0) throw new InvalidOperationException("Localized Tutorial layout failures: " + string.Join("; ", failures) + "; " + path);
                return "TUTORIAL_LOCALIZED_LAYOUT_PASS cases=" + observations.Count + "; " + path;
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunReferenceResizeChecks(string evidenceFolder)
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor) throw new InvalidOperationException("Canonical donor fonts required.");
            var root = new GameObject("ReferenceResizeController"); root.SetActive(false);
            GameObject ui = null;
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.controller = fixture; fixture.view = view; ui = view.gameObject;
                var head = new GameObject("ReferenceResizeHead", typeof(Camera)); head.transform.SetParent(view.transform);
                head.transform.position = new Vector3(100, 1.65f, 100);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 60; camera.aspect = 16f / 9; view.Head = head.transform;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                Set("Record", new ParticipantRecord { participant_id = "DEV_REFERENCE_RESIZE", test_data = true, language = ParticipantLanguage.Japanese, input_mode = ExperimentInputMode.Controller });
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                // Exercise the paged text reader; the Tutorial manual is a compact controller legend.
                Set("ReferenceOpen", true); Set("ReferenceSection", "step_by_step");
                var credited = (HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture);
                credited.Add("reference"); credited.Add("switch");
                ui.transform.SetParent(null, false); ui.SetActive(true); view.enabled = false;
                foreach (var motion in ui.GetComponentsInChildren<ResearchPanelMotion>(true)) motion.enabled = false;
                view.Refresh();
                var text = (TMPro.TMP_Text)typeof(ResearchFlowView).GetField("referenceBody", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                var refresh = (Action)typeof(ResearchFlowView).GetMethod("RefreshPageControls", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action), view);
                text.text = "資料"; text.pageToDisplay = 1; Canvas.ForceUpdateCanvases(); refresh();
                var panel = view.referencePanel; var rect = (RectTransform)panel.transform;
                // At this 60-degree camera pose the short reader fits the bottom margin; the taller paged reader must not.
                var originalPosition = head.transform.position + new Vector3(.2f, -.22f, 1.1f);
                panel.transform.SetPositionAndRotation(originalPosition, Quaternion.identity);
                if (!panel.IsReadableFromHead) throw new InvalidOperationException("Short reader fixture must start fully readable; incidental fixture failure.");
                var shortSize = rect.sizeDelta;
                text.text = string.Join("\n", Enumerable.Repeat("資料の現在のページを表示します。", 50));
                Canvas.ForceUpdateCanvases(); refresh();
                var longSize = rect.sizeDelta;
                bool oldPoseFits = (bool)typeof(ResearchPanelMotion).GetMethod("FitsView", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, new object[] { originalPosition, Quaternion.identity });
                bool revalidated = panel.IsReadableFromHead && !panel.PlacementBlocked;
                var result = new { short_size = shortSize.ToString(), long_size = longSize.ToString(), original_position = originalPosition.ToString(),
                    corrected_position = panel.transform.position.ToString(), old_pose_fits_new_bounds = oldPoseFits, revalidated,
                    fact = fixture.TutorialFact, ready = view.TutorialActionReady("reposition"), hardware_verified = false,
                    scope = "Actual TMP page size growth with synthetic 60-degree camera, no physical wearer or input evidence" };
                string folder = Path.GetFullPath(evidenceFolder); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "reference-resize-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(result, Formatting.Indented));
                if (longSize.y <= shortSize.y || oldPoseFits) throw new InvalidOperationException("Resize fixture did not grow beyond the prior readable viewport; incidental failure; " + path);
                if (!revalidated) throw new InvalidOperationException("Growing Reference page left the root/toolbar outside the readable viewport; " + path);
                var resting = panel.transform.position; var rotation = panel.transform.rotation;
                head.transform.position += Vector3.right * .02f; refresh();
                if (Vector3.Distance(resting, panel.transform.position) > .00001f || Quaternion.Angle(rotation, panel.transform.rotation) > .0001f)
                    throw new InvalidOperationException("Unchanged Reference layout followed ordinary head motion.");
                head.transform.position -= Vector3.right * .02f;
                text.text = "資料"; refresh(); panel.transform.SetPositionAndRotation(originalPosition, Quaternion.identity);
                var handle = panel.GetComponentInChildren<ResearchDragHandle>(true);
                var dragging = typeof(ResearchDragHandle).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic);
                dragging.SetValue(handle, true);
                try
                {
                    text.text = string.Join("\n", Enumerable.Repeat("資料の現在のページを表示します。", 50)); refresh();
                    if (Vector3.Distance(originalPosition, panel.transform.position) > .00001f)
                        throw new InvalidOperationException("Growing Reference reoriented or moved the explicitly injected held panel.");
                }
                finally { dragging.SetValue(handle, false); }
                typeof(ResearchFlowView).GetMethod("RevalidateReferenceLayoutIfReady", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                if (!panel.IsReadableFromHead || panel.PlacementBlocked)
                    throw new InvalidOperationException("Deferred Reference bounds growth was not revalidated after held-state release.");
                var completed = Newtonsoft.Json.Linq.JObject.FromObject(result);
                completed["passed"] = true; completed["unchanged_layout_world_stable"] = true;
                completed["injected_held_pose_unchanged"] = true; completed["deferred_release_revalidated"] = true;
                File.WriteAllText(path, completed.ToString(Formatting.Indented));
                return "REFERENCE_RESIZE_PASS actual page bounds revalidated, injected hold deferred, unchanged layout world-stable; " + path;
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string RunTemporalSequence(string evidenceFolder)
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!donor || !donor.Head) throw new InvalidOperationException("Canonical donor fonts and camera required.");
            var root = new GameObject("UIRecoveryTemporalController"); root.SetActive(false);
            GameObject ui = null;
            var observations = new List<object>();
            string folder = Path.Combine(Path.GetFullPath(evidenceFolder), "ui-temporal-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(folder);
            try
            {
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var taskA = root.AddComponent<ResearchTaskPort>(); taskA.taskId = "A"; taskA.demoMode = true;
                var taskB = root.AddComponent<ResearchTaskPort>(); taskB.taskId = "B"; taskB.demoMode = true;
                fixture.taskPorts = new[] { taskA, taskB };
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                view.controller = fixture; fixture.view = view; ui = view.gameObject;
                var head = new GameObject("SyntheticTemporalHead", typeof(Camera)); head.transform.SetParent(root.transform, false);
                var camera = head.GetComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.19f, .23f, .27f); camera.cullingMask = 1 << 31;
                camera.stereoTargetEye = StereoTargetEyeMask.None; view.Head = head.transform;
                void Set(string property, object value) => typeof(ResearchSessionFlowController).GetProperty(property).GetSetMethod(true).Invoke(fixture, new[] { value });
                // Geometry-only preview has no registered session/writer. Do not seed an invalid record for Pause eligibility.
                view.transform.SetParent(null, false); ui.SetActive(true); view.enabled = false;
                head.transform.SetParent(view.transform, true);
                foreach (var motion in ui.GetComponentsInChildren<ResearchPanelMotion>(true)) motion.enabled = false;
                void Capture(string name)
                {
                    foreach (var t in ui.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    foreach (var group in ui.GetComponentsInChildren<CanvasGroup>(true))
                        if (group.name != "TransitionFade") group.alpha = 1;
                    view.RefreshRecoveryUtility(); // Settle ownership after the fixture's authored group visibility.
                    Canvas.ForceUpdateCanvases(); foreach (var text in ui.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.ForceMeshUpdate(true);
                    var texture = RenderTexture.GetTemporary(1920, 1080, 24); var previous = RenderTexture.active;
                    var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                    try { camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG()); }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.DestroyImmediate(pixels); }
                    var renderedIcons = ui.GetComponentsInChildren<ResearchUtilityIcon>(false).Where(icon => icon.GetComponentsInParent<CanvasGroup>().All(group => group.alpha > .01f)).ToArray();
                    foreach (var icon in renderedIcons)
                    {
                        var renderer = icon.GetComponent<CanvasRenderer>();
                        if (!renderer || !renderer.GetMesh() || renderer.GetMesh().vertexCount == 0) throw new InvalidOperationException("Visible utility icon has no CanvasRenderer mesh: " + icon.transform.parent.name);
                    }
                    observations.Add(new { name, head = head.transform.position.ToString("F4"), yaw = head.transform.eulerAngles.y,
                        main_blocked = view.mainPanel.PlacementBlocked, main_readable = view.mainPanel.IsReadableFromHead,
                        reference_visible = view.IsReferenceActuallyVisible, recovery_visible = view.RecoveryCanvas.gameObject.activeSelf,
                        rendered_icon_meshes = renderedIcons.Select(icon => new { kind = icon.kind, vertices = icon.canvasRenderer.GetMesh().vertexCount, color = icon.color.ToString() }).ToArray(),
                        synthetic_input = true, hardware_verified = false });
                }
                head.transform.position = new Vector3(100, 1.65f, 100);
                foreach (var state in new[] { ResearchFlowState.TutorialWelcome, ResearchFlowState.TutorialInteractions, ResearchFlowState.Task1Active })
                {
                    Set("Flow", new ResearchSessionFlow(TaskOrder.AB, state, state == ResearchFlowState.Task1Active ? 5 : 0)); Set("ReferenceOpen", false);
                    view.Refresh(); view.mainPanel.Recenter(false, false); Capture(state.ToString());
                }
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, 3));
                ((HashSet<string>)typeof(ResearchSessionFlowController).GetField("tutorialEvidence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture)).Add("reference");
                Set("ReferenceOpen", true); Set("ReferenceSection", "user_manual"); view.Refresh(); view.referencePanel.Recenter(false, false); Capture("TutorialIntegratedReference");
                Set("Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5)); Set("ReferenceSection", "");
                view.Refresh(); view.mainPanel.Recenter(false, false); view.referencePanel.Recenter(false, false); Capture("TaskNeutralFormatChoice");
                if (view.IsReferenceActuallyVisible) throw new InvalidOperationException("Choice page counted as content exposure.");
                Set("ReferenceSection", "user_manual"); view.Refresh(); view.referencePanel.Recenter(false, false); Capture("TaskReferencePeerSurfaces");
                Set("ReferenceOpen", false); view.Refresh();
                // Explicit internal held-state injection tests recenter policy, not SDK acquisition or physical dragging.
                var heldHandle = view.mainPanel.GetComponentInChildren<ResearchDragHandle>(true);
                if (!heldHandle) throw new InvalidOperationException("No task drag handle for interrupted-drag policy test.");
                var heldField = typeof(ResearchDragHandle).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic);
                var heldPosition = view.mainPanel.transform.position; var heldRotation = view.mainPanel.transform.rotation;
                heldField.SetValue(heldHandle, true); head.transform.position += new Vector3(1, 0, 1);
                view.OnSpatialDiscontinuity("fixture_teleport_during_injected_hold");
                view.RefreshRecoveryUtility(); // Explicit per-frame View lifecycle because this synthetic fixture disables Update.
                if (!view.RecoveryCanvas.gameObject.activeSelf || view.RecoveryCanvas.GetComponent<ResearchPanelMotion>().PlacementBlocked)
                    throw new InvalidOperationException("Interrupted held slate must preserve an independent readable recovery/Pause path.");
                if (view.mainPanel.transform.position != heldPosition || view.mainPanel.transform.rotation != heldRotation) throw new InvalidOperationException("Spatial recovery moved a held panel.");
                Capture("InterruptedInjectedHoldUnchanged");
                heldField.SetValue(heldHandle, false);
                typeof(ResearchPanelMotion).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view.mainPanel, null);
                if (view.mainPanel.PlacementBlocked || !view.mainPanel.IsReadableFromHead) throw new InvalidOperationException("Deferred recenter failed after injected hold release.");
                Capture("InjectedHoldReleasedDeferredRecovery");
                foreach (float height in new[] { 1.2f, 1.65f, 1.9f })
                {
                    head.transform.SetPositionAndRotation(new Vector3(100, height, 100), Quaternion.identity); view.OnSpatialDiscontinuity("fixture_height");
                    foreach (float turn in new[] { 45f, 90f, 180f })
                    {
                        head.transform.rotation = Quaternion.Euler(0, turn, 0); view.OnSpatialDiscontinuity("fixture_snap"); view.RefreshRecoveryUtility();
                        if (view.mainPanel.PlacementBlocked || !view.mainPanel.IsReadableFromHead) throw new InvalidOperationException("Turn recovery unreadable " + height + "/" + turn);
                        Capture("eye" + height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "_turn" + turn);
                    }
                    var stablePosition = view.mainPanel.transform.position; var stableRotation = view.mainPanel.transform.rotation;
                    head.transform.position += head.transform.right * .12f;
                    typeof(ResearchPanelMotion).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view.mainPanel, null);
                    if (view.mainPanel.transform.position != stablePosition || view.mainPanel.transform.rotation != stableRotation) throw new InvalidOperationException("Resting slate followed an ordinary head translation.");
                    head.transform.position += new Vector3(1.2f, 0, .8f); view.OnSpatialDiscontinuity("fixture_teleport");
                    if (view.mainPanel.PlacementBlocked || !view.mainPanel.IsReadableFromHead) throw new InvalidOperationException("Teleport recovery unreadable.");
                    Capture("eye" + height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "_teleport");
                }
                // The same physical pose is deliberately viewed from its rear, then recovered without task-state mutation.
                head.transform.SetPositionAndRotation(new Vector3(100, 1.65f, 100), Quaternion.identity); view.OnSpatialDiscontinuity("fixture_reset");
                var oldPose = view.mainPanel.transform.position; var oldRotation = view.mainPanel.transform.rotation;
                head.transform.position = oldPose + oldRotation * Vector3.forward * .8f; head.transform.rotation = oldRotation * Quaternion.Euler(0, 180, 0);
                typeof(ResearchPanelMotion).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view.mainPanel, null); view.RefreshRecoveryUtility();
                if (!view.mainPanel.PlacementBlocked || !view.RecoveryCanvas.gameObject.activeSelf || view.RecoveryCanvas.GetComponent<ResearchPanelMotion>().PlacementBlocked) throw new InvalidOperationException("Rear concealment removed the recovery/Pause path.");
                Capture("RearFaceConcealedRecoveryAvailable");
                var stateBefore = fixture.Flow.State; view.LocalCommand("panel_recovery", "None"); view.RefreshRecoveryUtility();
                if (fixture.Flow.State != stateBefore || view.mainPanel.PlacementBlocked || !view.mainPanel.IsReadableFromHead) throw new InvalidOperationException("Recovery changed task state or remained unreadable.");
                Capture("RecoveredFrontFace");
                File.WriteAllText(Path.Combine(folder, "sequence.json"), JsonConvert.SerializeObject(new { provenance = "uGUI-only monoscopic synthetic sequence", hardware_verified = false, observations }, Formatting.Indented));
                return "UI_TEMPORAL_PASS output=" + folder + "; measured geometry/pixels only; stereo/device/wearer excluded";
            }
            finally { if (ui) UnityEngine.Object.DestroyImmediate(ui); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
