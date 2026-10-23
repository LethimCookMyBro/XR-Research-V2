#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Locomotion;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using VRMaintenanceResearch.HandsLabQuest3;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Explicit, one-time integration builder. Donor scenes and shared materials are never edited.</summary>
    public static class ResearchFlowSceneBuilder
    {
        public const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const string Root = "Assets/VRMaintenanceResearch/ResearchFlow";
        const string HandsScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_HandsLab_Quest3.unity";
        const string EnvironmentScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_Environment_V2.unity";
        const string Evidence = "Logs/ResearchFlowEvidence/2026-09-21";

        [MenuItem("VR Research/Research Flow/Apply Context Examine Second Pass")]
        public static void ApplyContextExamineSecondPass()
        {
            Guard();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the exact ResearchFlow integration scene.");
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            ApplyContextExamineSecondPass(new Dictionary<string, Transform> {
                ["A.module"] = transforms.Single(t => t.GetComponent<ResearchS1TaskAMechanics>()).GetComponent<ResearchS1TaskAMechanics>().servicePivot,
                ["B.module"] = transforms.Single(t => t.GetComponent<ResearchS1TaskBMechanics>())
            });
        }

        /// <summary>Only the two real assemblies are available. Connection/mount identities remain reserved.</summary>
        public static void ApplyContextExamineSecondPass(IDictionary<string, Transform> physicalTargets)
        {
            Guard();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the exact ResearchFlow integration scene.");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            var allTargets = controller.GetComponentsInChildren<ResearchExamineTarget>(true);
            string[] reservedIds = { "A.connection", "A.mount", "B.connection", "B.mount" };
            string[] ids = { "A.module", "B.module" };
            if (allTargets.Length != 6 || !allTargets.Select(t => t.stableTarget).OrderBy(s => s).SequenceEqual(ids.Concat(reservedIds).OrderBy(s => s)))
                throw new InvalidOperationException("Expected the six existing stable Examine targets; no target identities will be inferred.");
            var targets = allTargets.Where(t => ids.Contains(t.stableTarget)).ToArray();
            var unavailable = allTargets.Where(t => reservedIds.Contains(t.stableTarget)).ToArray();
            if (physicalTargets == null || physicalTargets.Count != ids.Length || ids.Any(id => !physicalTargets.TryGetValue(id, out var anchor) || !anchor || anchor.gameObject.scene != scene))
                throw new InvalidOperationException("Only A.module and B.module may map to the existing physical assemblies. Connection/mount targets are unavailable.");
            if (!physicalTargets["A.module"].GetComponentInParent<ResearchS1TaskAMechanics>()
                || physicalTargets["A.module"] != physicalTargets["A.module"].GetComponentInParent<ResearchS1TaskAMechanics>().servicePivot || !physicalTargets["B.module"].GetComponent<ResearchS1TaskBMechanics>())
                throw new InvalidOperationException("Examine must bind to the S1 task assemblies.");
            if (physicalTargets.Values.Distinct().Count() != ids.Length)
                throw new InvalidOperationException("Different research targets must not silently share the same physical anchor.");
            foreach (var anchor in physicalTargets.Values)
                if (anchor.GetComponentsInChildren<Collider>(true).Length == 0
                    && (!anchor.GetComponent<MeshFilter>() || !anchor.GetComponent<MeshFilter>().sharedMesh || !anchor.GetComponent<MeshRenderer>()))
                    throw new InvalidOperationException("Physical target needs an existing collider or rendered mesh: " + anchor.name);
            var oldCanvases = targets.Select(t => t.stationCanvas).Distinct().ToArray();
            if (oldCanvases.Any(c => !c)) throw new InvalidOperationException("Missing existing Examine canvas.");
            if (targets.Any(t => !(t.transform is RectTransform) || !t.GetComponent<Button>() || !t.GetComponentInChildren<TMP_Text>(true)))
                throw new InvalidOperationException("Existing Examine chip is missing its UI primitives.");
            if (oldCanvases.Length == ids.Length && oldCanvases.All(c => c.name.StartsWith("ResearchFlow_ContextExamine_", StringComparison.Ordinal)))
            {
                if (unavailable.Any(t => t.enabled || t.gameObject.activeSelf || t.stationCanvas || t.interactionRoots.Length > 0))
                    throw new InvalidOperationException("Reserved connection/mount targets must remain disabled and detached from interaction canvases.");
                foreach (var target in targets)
                {
                    Undo.RecordObject(target, "Update physical Examine target");
                    var anchor = physicalTargets[target.stableTarget];
                    target.ConfigureFocus(PhysicalFocusColliders(anchor), anchor);
                    EditorUtility.SetDirty(target);
                }
                DisableLegacyTaskProps(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save contextual Examine mapping.");
                return;
            }
            if (oldCanvases.Length != 2 || oldCanvases.Any(c => !c.name.StartsWith("ResearchFlow_Examine_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected two audited legacy Examine canvases; refusing broader UI removal.");
            var oldInteractions = targets.SelectMany(t => t.interactionRoots).Where(g => g).Distinct().ToArray();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Contextual Examine second pass");
            var reservedRoot = new GameObject("ResearchFlow_UnavailableExamineTargets");
            Undo.RegisterCreatedObjectUndo(reservedRoot, "Preserve unavailable Examine identities");
            reservedRoot.transform.SetParent(controller.transform, false);
            reservedRoot.SetActive(false);
            foreach (var target in unavailable)
            {
                Undo.RecordObject(target, "Disable unavailable Examine affordance");
                Undo.RecordObject(target.gameObject, "Disable unavailable Examine object");
                target.enabled = false; target.gameObject.SetActive(false);
                Undo.SetTransformParent(target.transform, reservedRoot.transform, "Preserve reserved target identity");
                target.stationCanvas = null; target.stationGroup = null; target.interactionRoots = Array.Empty<GameObject>();
                target.ConfigureFocus(Array.Empty<Collider>(), null);
                EditorUtility.SetDirty(target);
            }
            foreach (var target in targets)
            {
                var anchor = physicalTargets[target.stableTarget];
                var chip = new GameObject("ResearchFlow_ContextExamine_" + target.stableTarget, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
                Undo.RegisterCreatedObjectUndo(chip, "Create compact Examine canvas");
                chip.transform.SetParent(controller.transform, false);
                chip.transform.SetPositionAndRotation(anchor.position + Vector3.up * .1f, Quaternion.Euler(0, 180, 0));
                chip.transform.localScale = Vector3.one * .001f;
                chip.GetComponent<RectTransform>().sizeDelta = new Vector2(190, 72);
                var canvas = chip.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = controller.origin.Camera;
                var group = chip.GetComponent<CanvasGroup>();
                Undo.RecordObject(target, "Rewire existing Examine target");
                Undo.SetTransformParent(target.transform, chip.transform, "Move existing Examine chip");
                var rect = (RectTransform)target.transform;
                Undo.RecordObject(rect, "Size Examine chip");
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity; rect.anchoredPosition3D = Vector3.zero; rect.sizeDelta = new Vector2(190, 72);
                var label = target.GetComponentInChildren<TMP_Text>(true);
                Undo.RecordObject(label, "Use neutral Examine action");
                Undo.RecordObject(label.rectTransform, "Size Examine label");
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                label.rectTransform.anchoredPosition3D = Vector3.zero; label.rectTransform.sizeDelta = new Vector2(174, 64);
                label.fontSize = 32; label.text = ResearchStrings.Get("action.examine", "Thai"); label.raycastTarget = false; label.enabled = false;
                target.stationCanvas = canvas; target.stationGroup = group;
                target.ConfigureFocus(PhysicalFocusColliders(anchor), anchor);
                target.interactionRoots = QuickActionsAPI.AddRayCanvasInteraction(chip, false).Concat(QuickActionsAPI.AddPokeCanvasInteraction(chip, false)).ToArray();
                foreach (var interaction in target.interactionRoots) interaction.SetActive(false);
                var button = target.GetComponent<Button>(); Undo.RecordObject(button, "Hide idle Examine action");
                button.interactable = false;
                if (button.targetGraphic) { Undo.RecordObject(button.targetGraphic, "Hide idle Examine graphic"); button.targetGraphic.enabled = false; button.targetGraphic.raycastTarget = false; }
                canvas.enabled = false; group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
                EditorUtility.SetDirty(target);
            }
            foreach (var interaction in oldInteractions)
                if (interaction && !oldCanvases.Any(c => interaction.transform.IsChildOf(c.transform))) Undo.DestroyObjectImmediate(interaction);
            foreach (var canvas in oldCanvases) if (canvas) Undo.DestroyObjectImmediate(canvas.gameObject);
            DisableLegacyTaskProps(scene);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save contextual Examine migration.");
            string evidencePath = "Logs/ResearchFlowEvidence/2026-09-22-UXSecondPass/examine-target-map.json";
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));
            File.WriteAllText(evidencePath, JsonConvert.SerializeObject(new {
                available = physicalTargets.Select(pair => new { stable_target = pair.Key, physical_object = pair.Value.name, scope = "existing whole assembly" }),
                unavailable = reservedIds,
                reason = "Imported PC/fan models are merged meshes without separate connection/mount objects. User selected assembly-only contextual examination.",
                added_geometry = false, physical_input_verified = false
            }, Formatting.Indented));
            Debug.Log("CONTEXT_EXAMINE_MIGRATION=2 assembly chips; connection/mount reserved and unavailable; no backend/session changes");
        }

        static void DisableLegacyTaskProps(Scene scene)
        {
            foreach (var node in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "Computer_TaskCase" || t.name == "Fan_TaskFan"))
            {
                Undo.RecordObject(node.gameObject, "Disable replaced S1 prop");
                node.gameObject.SetActive(false);
                EditorUtility.SetDirty(node.gameObject);
            }
        }

        static Collider[] PhysicalFocusColliders(Transform anchor)
        {
            var colliders = anchor.GetComponents<Collider>();
            if (colliders.Length > 0) return colliders;
            if (!anchor.GetComponent<MeshFilter>()) return anchor.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
            // Match the rendered static mesh, never a larger invisible UI volume.
            var collider = Undo.AddComponent<MeshCollider>(anchor.gameObject);
            collider.sharedMesh = anchor.GetComponent<MeshFilter>().sharedMesh;
            return new Collider[] { collider };
        }

        [MenuItem("VR Research/Research Flow/Add Deliberate Examine Targets")]
        public static void AddExamineTargets()
        {
            Guard();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the research integration scene before adding targets.");
            var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResearchSessionFlowController>(true)).Single();
            BuildExamineTargets(controller, controller.taskPorts.Single(p => p.taskId == "A"), new Vector3(0, 1.3f, 5.15f), controller.view.thaiFont);
            BuildExamineTargets(controller, controller.taskPorts.Single(p => p.taskId == "B"), new Vector3(-9.7f, 1.3f, 4.65f), controller.view.thaiFont);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save examination target integration.");
        }

        [MenuItem("VR Research/Research Flow/Build New Integration Scene")]
        public static void Build()
        {
            Guard();
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Integration scene already exists; preserve it and use targeted edits.");
            if (!File.Exists(HandsScene) || !File.Exists(EnvironmentScene)) throw new FileNotFoundException("Required donor scene is unavailable.");
            Directory.CreateDirectory(Root + "/Prefabs"); Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Evidence);
            var fonts = BuildFonts();
            if (!AssetDatabase.CopyAsset(HandsScene, ScenePath)) throw new IOException("Could not copy HandsLab donor scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lab = scene.GetRootGameObjects().Single(g => g.name == "HandsLab_Quest3");
            if (PrefabUtility.IsPartOfPrefabInstance(lab)) PrefabUtility.UnpackPrefabInstance(lab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var session in lab.GetComponentsInChildren<HandsLabSession>(true)) session.enabled = false;
            foreach (var panel in lab.GetComponentsInChildren<HandsLabPanel>(true)) panel.gameObject.SetActive(false);
            SetChildActive(lab.transform, "Floor", false); SetChildActive(lab.transform, "HandsLab_KeyLight", false);

            var source = EditorSceneManager.OpenScene(EnvironmentScene, OpenSceneMode.Additive);
            GameObject environment;
            try
            {
                var donor = source.GetRootGameObjects().Single(g => g.name == "Environment_V2");
                environment = UnityEngine.Object.Instantiate(donor); environment.name = "Environment_V2";
                SceneManager.MoveGameObjectToScene(environment, scene);
                var donorLight = source.GetRootGameObjects().FirstOrDefault(g => g.name == "Baseline_Directional_Light");
                if (donorLight != null)
                {
                    var lightCopy = UnityEngine.Object.Instantiate(donorLight); lightCopy.name = "ResearchFlow_KeyLight";
                    SceneManager.MoveGameObjectToScene(lightCopy, scene);
                }
            }
            finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(scene); }
            if (environment.GetComponentsInChildren<XROrigin>(true).Length != 0 || environment.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Environment decor unexpectedly contains a camera/rig; inspect donor before proceeding.");

            var origin = lab.GetComponentsInChildren<XROrigin>(true).Single();
            // Floor-space head poses already include eye height, even before XROrigin initialization completes.
            origin.CameraYOffset = 1.65f; var offset = origin.CameraFloorOffsetObject.transform.localPosition; offset.y = 0f; origin.CameraFloorOffsetObject.transform.localPosition = offset;
            lab.transform.SetPositionAndRotation(new Vector3(9.72f, 0, 4.95f), Quaternion.Euler(0, 180, 0));
            var trainingRoot = new GameObject("ResearchFlow_TrainingPractice"); trainingRoot.transform.SetParent(lab.transform, false);
            foreach (string name in new[] { "PinchStone", "GripHandle", "Table", "Placement" })
            {
                var child = lab.transform.Find(name);
                if (child == null) throw new InvalidOperationException("Missing audited training object " + name);
                child.SetParent(trainingRoot.transform, true);
            }
            trainingRoot.transform.Find("Table").gameObject.SetActive(false);
            trainingRoot.transform.localPosition = new Vector3(0, .17f, .16f);
            ConfigureLocomotion(origin);
            var locomotor = origin.GetComponentsInChildren<FirstPersonLocomotor>(true).Single();
            var sessionRoot = new GameObject("ResearchFlow_Session");
            var stations = new[] {
                Station(sessionRoot.transform, "Training", new Vector3(9.72f, 0, 4.95f)),
                Station(sessionRoot.transform, "Computer", new Vector3(0, 0, 5.60f)),
                Station(sessionRoot.transform, "Fan", new Vector3(-9.7f, 0, 5.15f))
            };
            var view = ResearchFlowView.Build(sessionRoot.transform, fonts[0], fonts[1], fonts[2]);
            view.Head = origin.Camera.transform;
            foreach (var canvas in view.ParticipantCanvases)
            {
                canvas.worldCamera = origin.Camera;
                QuickActionsAPI.AddRayCanvasInteraction(canvas.gameObject);
                QuickActionsAPI.AddPokeCanvasInteraction(canvas.gameObject);
            }
            var ports = new[] { TaskPort(sessionRoot.transform, "A"), TaskPort(sessionRoot.transform, "B") };
            var controller = sessionRoot.AddComponent<ResearchSessionFlowController>();
            controller.origin = origin; controller.view = view;
            controller.guards = lab.GetComponentsInChildren<HandsLabInputGuard>(true);
            foreach (var guard in controller.guards)
            {
                guard.uiClearanceColliders = view.ParticipantCanvases.SelectMany(c => c.GetComponentsInChildren<Collider>(true)).ToArray();
                guard.uiClearanceSurfaces = view.ParticipantCanvases.SelectMany(c => c.GetComponentsInChildren<RayInteractable>(true)).ToArray();
            }
            controller.locomotionInputRoots = origin.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "LocomotionControllerInteractorGroup" || t.name == "LocomotionHandInteractorGroup").Select(t => t.gameObject).ToArray();
            controller.trainingObjects = trainingRoot.GetComponentsInChildren<HandsLabObject>(true);
            controller.taskPorts = ports; controller.stations = stations; controller.locomotor = locomotor; controller.trainingRoot = trainingRoot;
            view.controller = controller; sessionRoot.AddComponent<ResearchLocomotionEvents>().controller = controller;
            ResearcherSetupView.Build(sessionRoot.transform, fonts[2]).controller = controller;
            BuildExamineTargets(controller, ports[0], new Vector3(0, 1.3f, 5.15f), fonts[0]);
            BuildExamineTargets(controller, ports[1], new Vector3(-9.7f, 1.3f, 4.65f), fonts[0]);
            PolishEnvironment(environment, scene);
            ValidateRig(scene);
            // Save the UI independently; scene-only camera references are assigned again by the runtime.
            PrefabUtility.SaveAsPrefabAsset(view.gameObject, Root + "/Prefabs/ResearchFlowUI.prefab");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save integration scene.");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Evidence + "/scene-build.json", JsonConvert.SerializeObject(new {
                scene = ScenePath, donor = HandsScene, decor = EnvironmentScene,
                xr_origins = 1, main_cameras = 1, event_systems = 1,
                input_guard_count = controller.guards.Length, training_object_count = controller.trainingObjects.Length,
                stations = stations.Select(t => new { t.name, position = new[] { t.position.x, t.position.y, t.position.z }, yaw = t.eulerAngles.y }),
                original_hand_visuals_preserved = true, demo_only_task_ports = true, hardware_verified = false
            }, Formatting.Indented));
            Debug.Log("RESEARCH_FLOW_SCENE_BUILT " + ScenePath);
        }

        static void Guard()
        {
            if (!string.Equals(Application.dataPath.Replace('\\', '/'), "D:/VR_TMU/XR-Research-V2/Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Builder restricted to the existing research project.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for stable Edit Mode before integration.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Loaded scene is dirty: " + SceneManager.GetSceneAt(i).path);
        }

        static void SetChildActive(Transform root, string name, bool active)
        { var child = root.Find(name); if (child == null) throw new InvalidOperationException("Missing audited donor child " + name); child.gameObject.SetActive(active); }

        static void ConfigureLocomotion(XROrigin origin)
        {
            // Exact native dependencies disabled by the HandsLab builder. Distance/ray grabs stay unchanged.
            var enabled = new HashSet<string> { "Locomotor", "LocomotionSettings", "LocomotionOutput", "LocomotionControllerInteractorGroup",
                "TeleportControllerInteractor", "TeleportActiveState", "ControllerTurnerInteractor", "LocomotionHandInteractorGroup",
                "LocomotionGate", "TeleportHandInteractor", "ScrollLocomotionGate", "LocomotionTeleportAudio", "EnterLocomotionSound", "TeleportSound" };
            foreach (var transform in origin.GetComponentsInChildren<Transform>(true))
            {
                if (enabled.Contains(transform.name)) transform.gameObject.SetActive(true);
                if (transform.name == "ControllerLocomotionSlideActions" || transform.name == "SmoothMovementTunneling") transform.gameObject.SetActive(false);
            }
            foreach (var mover in origin.GetComponentsInChildren<MovingSetting>(true))
            {
                // ReactiveValue setter alone does not persist a nested prefab override.
                var serialized = new SerializedObject(mover);
                serialized.FindProperty("_controllerMovement").FindPropertyRelative("_value").intValue = (int)MovingSetting.MovementStyle.Teleport;
                // Meta sample residue: Teleport mode also enables a 0.8 m thumbstick-back step (Relative translation),
                // which ResearchLocomotionEvents does not log. Locomotion is teleport + snap only.
                var teleportObjects = serialized.FindProperty("_teleportGameObjects");
                for (int i = teleportObjects.arraySize - 1; i >= 0; i--)
                    if (teleportObjects.GetArrayElementAtIndex(i).objectReferenceValue is GameObject item && item.name == "ControllerStepInteractor")
                    { item.SetActive(false); teleportObjects.DeleteArrayElementAtIndex(i); }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(mover);
            }
            foreach (var setting in origin.GetComponentsInChildren<TurningSetting>(true))
            { setting.ControllerTurn.Value = TurningSetting.RotationStyle.Snap; setting.RotationSnapAngle.Value = 45f; }
            foreach (var turner in origin.GetComponentsInChildren<TurnerEventBroadcaster>(true))
            { turner.TurnMethod = TurnerEventBroadcaster.TurnMode.Snap; turner.SnapTurnDegrees = 45f; }
        }

        static Transform Station(Transform parent, string name, Vector3 position)
        {
            var station = new GameObject("ResearchFlow_Station_" + name); station.transform.SetParent(parent, false);
            station.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 180, 0));
            QuickActionsAPI.AddTeleportInteraction(station, TeleportSurfaceType.Hotspot, TeleportHotspotSnap.SnapPosition);
            return station.transform;
        }

        static ResearchTaskPort TaskPort(Transform parent, string id)
        {
            var target = new GameObject("ResearchFlow_Task" + id + "_Port"); target.transform.SetParent(parent, false);
            var port = target.AddComponent<ResearchTaskPort>(); port.taskId = id; port.demoMode = true; port.contentApproved = false;
            return port;
        }

        static void BuildExamineTargets(ResearchSessionFlowController controller, ResearchTaskPort task, Vector3 position, TMP_FontAsset font)
        {
            var existing = controller.GetComponentsInChildren<ResearchExamineTarget>(true).Where(t => t.task == task).ToArray();
            if (existing.Length > 0)
            {
                if (existing.Length != 3 || existing.Select(t => t.stableTarget).Distinct().Count() != 3)
                    throw new InvalidOperationException("Unexpected duplicate/incomplete examination targets for task " + task.taskId);
                return;
            }
            var root = new GameObject("ResearchFlow_Examine_" + task.taskId, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            root.transform.SetParent(controller.transform, false); root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 180, 0));
            root.transform.localScale = Vector3.one * .001f;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1100, 230);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = controller.origin.Camera;
            var group = root.GetComponent<CanvasGroup>();
            var targets = new List<ResearchExamineTarget>();
            string[] suffixes = { "module", "connection", "mount" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                var rect = ResearchFlowView.Rect(root.transform, task.taskId + "." + suffixes[i], new Vector2((i - 1) * 370, i == 1 ? 32 : -22), new Vector2(290, 116));
                var graphic = rect.gameObject.AddComponent<VRMaintenanceResearch.TrainingV2.TrainingRoundedGraphic>(); graphic.color = Color.white; graphic.raycastTarget = true;
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
                var colors = button.colors; colors.normalColor = new Color(.055f, .085f, .12f); colors.highlightedColor = new Color(.09f, .23f, .36f);
                colors.pressedColor = new Color(.07f, .17f, .27f); colors.disabledColor = new Color(.12f, .14f, .17f, .35f); colors.fadeDuration = .1f; button.colors = colors;
                var textRect = ResearchFlowView.Rect(rect, "Label", Vector2.zero, new Vector2(270, 108));
                var label = textRect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font; label.fontSize = 32;
                label.alignment = TextAlignmentOptions.Center; label.color = new Color(.93f, .96f, 1); label.raycastTarget = false;
                label.text = ResearchStrings.Get("action.examine", "Thai");
                var target = rect.gameObject.AddComponent<ResearchExamineTarget>(); target.controller = controller; target.task = task;
                target.stableTarget = task.taskId + "." + suffixes[i]; target.targetKind = i; target.stationCanvas = canvas; target.stationGroup = group;
                targets.Add(target);
            }
            var interactions = QuickActionsAPI.AddRayCanvasInteraction(root).Concat(QuickActionsAPI.AddPokeCanvasInteraction(root)).ToArray();
            foreach (var target in targets) target.interactionRoots = interactions;
            canvas.enabled = false; group.alpha = 0; group.blocksRaycasts = false;
            foreach (var interaction in interactions) interaction.SetActive(false);
        }

        static TMP_FontAsset[] BuildFonts()
        {
            var thai = NewFont("Thai", "Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai.ttf", 2048);
            var japanese = NewFont("Japanese", Root + "/UI/Fonts/NotoSansJP-Regular.otf", 4096);
            var english = NewFont("English", "Assets/TextMesh Pro/Fonts/LiberationSans.ttf", 2048);
            thai.fallbackFontAssetTable = new List<TMP_FontAsset> { english, japanese };
            // NotoSansJP includes Latin and the checkmark absent from LiberationSans. Keep fallback graph acyclic.
            english.fallbackFontAssetTable = new List<TMP_FontAsset> { japanese };
            japanese.fallbackFontAssetTable = new List<TMP_FontAsset>();
            var fonts = new[] { thai, japanese, english }; var languages = new[] { "Thai", "Japanese", "English" };
            var result = new List<object>();
            for (int i = 0; i < fonts.Length; i++)
            {
                string chars = string.Concat(ResearchStrings.All(languages[i]).Values) + string.Concat(ResearchDemoManual.All(languages[i]).Values)
                    + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz:.-+/()[]<>% " + "✓○●×";
                chars = new string(chars.Where(c => !char.IsControl(c)).Distinct().ToArray());
                fonts[i].TryAddCharacters(chars, out string missingNative);
                fonts[i].HasCharacters(chars, out uint[] missing, true, true);
                result.Add(new { language = languages[i], characters = chars.Length, missing_codepoints = missing, native_fallback_codepoints = (missingNative ?? "").Select(c => (int)c).ToArray() });
                foreach (var font in fonts) PersistFontTextures(font);
                if (missing != null && missing.Length > 0) throw new InvalidOperationException("Missing " + languages[i] + " glyphs: " + string.Join(",", missing));
            }
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "/font-glyphs.json", JsonConvert.SerializeObject(result, Formatting.Indented));
            AssetDatabase.SaveAssets(); return fonts;
        }

        static TMP_FontAsset NewFont(string name, string sourcePath, int atlas)
        {
            string path = Root + "/UI/Fonts/ResearchFlow_" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path); if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null) throw new InvalidOperationException("Font source is not imported: " + sourcePath);
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);
            font.name = "ResearchFlow_" + name; font.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(font, path); PersistFontTextures(font); return font;
        }
        static void PersistFontTextures(TMP_FontAsset font)
        {
            if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) if (texture != null && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(font);
        }

        static void PolishEnvironment(GameObject environment, Scene scene)
        {
            var owned = new Dictionary<Material, Material>();
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials; bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i]; if (source == null || !source.HasProperty("_Smoothness")) continue;
                    string label = (source.name + " " + renderer.name).ToLowerInvariant();
                    if (!label.Contains("floor") && !label.Contains("wall") && !label.Contains("workbench")) continue;
                    if (!owned.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = "ResearchFlow_" + source.name };
                        copy.SetFloat("_Smoothness", Mathf.Clamp(source.GetFloat("_Smoothness"), .12f, .35f));
                        string safeName = string.Concat(copy.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(Root + "/Materials/" + safeName + ".mat"));
                        owned.Add(source, copy);
                    }
                    materials[i] = copy; changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            var light = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(false)).FirstOrDefault(l => l.type == LightType.Directional);
            if (light == null) { light = new GameObject("ResearchFlow_KeyLight").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -35, 0); }
            light.intensity = 1.05f; light.color = new Color(1f, .97f, .92f); light.shadows = LightShadows.Soft; light.shadowStrength = .7f;
            RenderSettings.sun = light; RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.48f, .54f, .61f); RenderSettings.ambientEquatorColor = new Color(.3f, .34f, .39f);
            RenderSettings.ambientGroundColor = new Color(.14f, .16f, .19f); RenderSettings.reflectionIntensity = .65f;
        }

        static void ValidateRig(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(g => g.GetComponentsInChildren<XROrigin>(true)).Count() != 1) throw new InvalidOperationException("Expected exactly one XR Origin.");
            if (roots.SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Count(c => c.CompareTag("MainCamera")) != 1) throw new InvalidOperationException("Expected exactly one main camera.");
            if (roots.SelectMany(g => g.GetComponentsInChildren<EventSystem>(true)).Count() != 1
                || roots.SelectMany(g => g.GetComponentsInChildren<PointableCanvasModule>(true)).Count() != 1) throw new InvalidOperationException("Expected one native Meta UI EventSystem/module.");
            if (roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Any(t => t.name == "Training Left Controller Hand" || t.name == "Training Right Controller Hand"))
                throw new InvalidOperationException("Legacy presentation hand visual unexpectedly copied.");
        }
    }
}
#endif
