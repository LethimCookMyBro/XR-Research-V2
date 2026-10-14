#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchSpatialChecks
    {
        // DEV geometry envelope derived from the authored Training station, not participant anthropometry.
        public static string RunReach()
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>();
            var colliders = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
            string[] benches = { "Computer_Main", "Fan_Main" };
            string[] targets = { "Computer_TaskCase", "Fan_TaskFan" };
            for (int i = 0; i < benches.Length; i++)
            {
                var station = controller.stations[i + 1];
                if (Vector3.Dot(station.forward, Vector3.back) < .999f)
                    throw new InvalidOperationException(benches[i] + " reach measurement requires authored -Z facing");
                var table = colliders.Single(c => c.name == benches[i] && c.enabled);
                var target = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Single(t => t.name == targets[i]);
                float gap = station.position.z - table.bounds.max.z;
                float reach = Vector2.Distance(new Vector2(station.position.x, station.position.z), new Vector2(target.position.x, target.position.z));
                if (gap < .299f || gap > .501f || reach > .901f)
                    throw new InvalidOperationException(benches[i] + " DEV reach failed: edge gap=" + gap + ", target planar distance=" + reach);
                if (Mathf.Abs(table.bounds.max.y - .92f) > .001f || Mathf.Abs(station.position.y) > .001f)
                    throw new InvalidOperationException(benches[i] + " floor/table height changed");
            }
            return "DEV_REACH_PASS stations=2; geometry only, physical reach HARDWARE PENDING";
        }

        public static string RunPlacement()
        {
            var source = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            var head = new GameObject("SyntheticPlacementHead");
            GameObject blocker = null;
            int checks = 0;
            try
            {
                var view = clone.GetComponent<ResearchFlowView>();
                view.controller = null;
                view.Head = head.transform;
                view.mainPanel.view = view; view.referencePanel.view = view;
                view.mainPanel.gameObject.SetActive(true);
                ((RectTransform)view.mainPanel.transform).sizeDelta = new Vector2(700, 410); // current compact task slate, not stale serialized legacy size
                view.referencePanel.gameObject.SetActive(false);
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "Environment_OcclusionCheckOnly"; blocker.SetActive(false);
            foreach (var station in source.controller.stations.Select(t => t.position))
                    foreach (float height in new[] {1.2f,1.65f,1.9f})
                    {
                        head.transform.SetPositionAndRotation(station + Vector3.up * height, Quaternion.Euler(0,180,0));
                        view.referencePanel.gameObject.SetActive(false);
                        view.mainPanel.Recenter(false, false);
                if (view.mainPanel.PlacementBlocked || !view.mainPanel.PlacementClear(view.mainPanel.transform.position,view.mainPanel.transform.rotation)) throw new InvalidOperationException("Main panel blocked: " + station + "/" + height);
                float mainYaw = Mathf.DeltaAngle(180, view.mainPanel.transform.eulerAngles.y);
                if (mainYaw < 27.5f || mainYaw > 35.5f) throw new InvalidOperationException("Main panel must retain a clear nearby right-side placement, got " + mainYaw + " at " + station + "/" + height);
                        view.referencePanel.gameObject.SetActive(true);
                        view.referencePanel.Recenter(false, false);
                if (view.referencePanel.PlacementBlocked || !view.referencePanel.PlacementClear(view.referencePanel.transform.position,view.referencePanel.transform.rotation)) throw new InvalidOperationException("Reference panel blocked: " + station + "/" + height);
                if (Vector3.Distance(view.mainPanel.transform.position,view.referencePanel.transform.position) < .9f) throw new InvalidOperationException("Panels overlap at " + station + "/" + height + "; main=" + view.mainPanel.transform.position + "; reference=" + view.referencePanel.transform.position);
                        checks += 4;
                    }
                view.referencePanel.gameObject.SetActive(false);
                // Native callbacks may run Reference before Main; preserve first-open placement order.
                var motionType = typeof(ResearchPanelMotion);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var pending = motionType.GetProperty("RecenterPending");
                var applyPending = motionType.GetMethod("TryApplyPendingRecenter", flags);
                foreach (var panel in new[] { view.mainPanel, view.referencePanel })
                {
                    pending.SetValue(panel, true);
                    motionType.GetField("requestedFrame", flags).SetValue(panel, -1);
                }
                var ready = new object[] { true, true, true, Time.frameCount + 1 };
                applyPending.Invoke(view.referencePanel, ready);
                if (!view.referencePanel.RecenterPending) throw new InvalidOperationException("Reference must wait for pending Main placement");
                checks++;
                applyPending.Invoke(view.mainPanel, ready);
                applyPending.Invoke(view.referencePanel, new object[] { false, true, true, Time.frameCount + 1 });
                if (!view.referencePanel.RecenterPending) throw new InvalidOperationException("Reference still requires a valid native pose");
                checks++;
                applyPending.Invoke(view.referencePanel, ready);
                if (view.mainPanel.RecenterPending || view.referencePanel.RecenterPending) throw new InvalidOperationException("Ordered native panel placement did not finish");
                checks++;
                blocker.transform.position = head.transform.position + head.transform.forward * .6f;
                blocker.transform.localScale = Vector3.one * .3f; blocker.SetActive(true);
                Physics.SyncTransforms();
                if (view.mainPanel.PlacementClear(head.transform.position + head.transform.forward * 1.2f, Quaternion.LookRotation(head.transform.forward)))
                    throw new InvalidOperationException("Clear final panel volume must still reject foreground occlusion");
                checks++;
                blocker.transform.position = head.transform.position;
                blocker.transform.localScale = Vector3.one;
                blocker.GetComponent<BoxCollider>().size = Vector3.one * 6;
                Physics.SyncTransforms();
                view.mainPanel.Recenter(false, false);
                if (!view.mainPanel.PlacementBlocked || view.mainPanel.GetComponent<Canvas>().enabled ||
                    view.mainPanel.GetComponent<UnityEngine.EventSystems.BaseRaycaster>().enabled)
                    throw new InvalidOperationException("Rejected placement must hide canvas and disable raycasting");
                checks++;
                blocker.SetActive(false);
                Physics.SyncTransforms();
                motionType.GetField("nextBlockedRetry", flags).SetValue(view.mainPanel, 0f);
                motionType.GetMethod("LateUpdate", flags).Invoke(view.mainPanel, null);
                if (view.mainPanel.PlacementBlocked || !view.mainPanel.GetComponent<Canvas>().enabled ||
                    !view.mainPanel.GetComponent<UnityEngine.EventSystems.BaseRaycaster>().enabled)
                    throw new InvalidOperationException("Deferred valid retry must restore canvas and raycasting without moving the head");
                checks++;
                return "SPATIAL_PLACEMENT_PASS checks=" + checks + "; synthetic head poses only";
            }
            finally { if (blocker) UnityEngine.Object.DestroyImmediate(blocker); UnityEngine.Object.DestroyImmediate(clone); UnityEngine.Object.DestroyImmediate(head); }
        }

        // Inactive DEV controller; detached UI is activated only to initialize native TMP font/layout metrics.
        public static string RunModalPlacement()
        {
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            var donorCamera = donor && donor.Head ? donor.Head.GetComponent<Camera>() : null;
            if (!donor || !donor.controller || !donorCamera || !donor.thaiFont || !donor.japaneseFont || !donor.englishFont
                || donor.controller.stations.Length < 3 || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Load the ResearchFlow scene and use the offline Editor modal fixture with no running XR subsystem.");
            var root = new GameObject("ResearchFlow_ModalPlacementFixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            GameObject uiRoot = null;
            int checks = 0, cases = 0;
            float minTitle = float.PositiveInfinity, minBody = float.PositiveInfinity;
            string completionMetrics = "";
            void Set(object target, string property, object value) => target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
            void Check(bool value, string context, string gate, ResearchPanelMotion panel)
            {
                checks++;
                if (!value) throw new InvalidOperationException(context + ": " + gate + "; rejection=" + panel.LastRejection);
            }
            try
            {
                SceneManager.MoveGameObjectToScene(root, donor.gameObject.scene);
                var fixture = root.AddComponent<ResearchSessionFlowController>();
                var view = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                fixture.view = view; view.controller = fixture;
                var head = new GameObject("SyntheticModalHead"); head.transform.SetParent(root.transform, false);
                var camera = head.AddComponent<Camera>(); camera.CopyFrom(donorCamera); camera.enabled = false;
                view.Head = head.transform;
                view.enabled = false; view.mainPanel.enabled = view.referencePanel.enabled = false;
                uiRoot = view.gameObject; uiRoot.SetActive(false); uiRoot.hideFlags = HideFlags.HideAndDontSave;
                view.transform.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(uiRoot, donor.gameObject.scene);
                if (!uiRoot.scene.IsValid()) throw new InvalidOperationException("Modal UI fixture must retain its donor scene");
                uiRoot.SetActive(true); // TMP Awake/OnEnable, without Controller Awake or an Update frame.
                Canvas.ForceUpdateCanvases();
                if (root.activeInHierarchy || fixture.Registry != null || fixture.Logger != null)
                    throw new InvalidOperationException("Modal fixture must never activate its controller or open a session.");
                var title = view.mainPanel.transform.Find("Expanded/Title").GetComponent<TMPro.TMP_Text>();
                var body = view.mainPanel.transform.Find("Expanded/Body").GetComponent<TMPro.TMP_Text>();
                var fitsView = typeof(ResearchPanelMotion).GetMethod("FitsView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var foreground = donor.gameObject.scene.GetRootGameObjects().Where(g => g.name.StartsWith("Environment_") || g.name == "ResearchFlow_EnvironmentSecondPass")
                    .SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
                var actionCorners = new Vector3[4];
                var samples = new (ResearchFlowState state, int completed, bool paused, bool? inspect)[]
                {
                    (ResearchFlowState.TutorialWelcome, 0, false, null),
                    (ResearchFlowState.TutorialInteractions, 0, false, null),
                    (ResearchFlowState.TutorialInteractions, 1, false, null),
                    (ResearchFlowState.TutorialInteractions, 2, false, null),
                    (ResearchFlowState.TutorialReferenceInspect, 3, false, null),
                    (ResearchFlowState.TutorialReferenceInspect, 4, false, null),
                    (ResearchFlowState.TutorialComplete, 5, false, null),
                    (ResearchFlowState.Task1Ready, 5, false, null),
                    (ResearchFlowState.Task1Active, 5, false, null),
                    (ResearchFlowState.PostTask1Checkpoint, 5, false, null),
                    (ResearchFlowState.Task2Ready, 5, false, null),
                    (ResearchFlowState.Task2Active, 5, false, null),
                    (ResearchFlowState.PostTask2Checkpoint, 5, false, null),
                    (ResearchFlowState.ParticipantComplete, 5, false, null),
                    (ResearchFlowState.Task1Active, 5, false, true),
                    (ResearchFlowState.Task1Active, 5, false, false),
                    (ResearchFlowState.Task1Active, 5, true, null),
                    (ResearchFlowState.Task2Active, 5, false, true),
                    (ResearchFlowState.Task2Active, 5, false, false),
                    (ResearchFlowState.Task2Active, 5, true, null)
                };
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                    foreach (var sample in samples)
                        // Tutorial transitions belong at Training; task/result transitions belong at either task station.
                        foreach (int stationIndex in sample.state <= ResearchFlowState.TutorialComplete ? new[] { 0 } : new[] { 1, 2 })
                            foreach (float height in new[] { 1.2f, 1.65f, 1.9f })
                            {
                                var station = donor.controller.stations[stationIndex];
                                bool firstTask = sample.state <= ResearchFlowState.PostTask1Checkpoint;
                                var order = stationIndex == 2 ? (firstTask ? TaskOrder.BA : TaskOrder.AB) : (firstTask ? TaskOrder.AB : TaskOrder.BA);
                                Set(fixture, "Record", new ParticipantRecord { participant_id = "DEV_MODAL_PLACEMENT", group = ParticipantGroup.Dev,
                                    language = language, session_state = RegistryState.InProgress, task_order = order,
                                    input_mode = ExperimentInputMode.Controller, test_data = true });
                                Set(fixture, "Flow", new ResearchSessionFlow(order, sample.state, sample.completed, sample.paused, sample.inspect));
                                head.transform.SetPositionAndRotation(station.position + Vector3.up * height, station.rotation);
                                view.Refresh(); // Measure the real localized card, never a fixed compact-task rectangle.
                                Canvas.ForceUpdateCanvases(); title.ForceMeshUpdate(true); body.ForceMeshUpdate(true);
                                view.Refresh(); // Newly activated tutorial/checkpoint children now have native TMP metrics too.
                                Canvas.ForceUpdateCanvases(); title.ForceMeshUpdate(true); body.ForceMeshUpdate(true);
                                view.mainPanel.Recenter(false, false);
                                var panel = view.mainPanel;
                                var size = ((RectTransform)panel.transform).rect.size;
                                string context = language + "/" + order + "/" + sample.state + "/step=" + sample.completed + (sample.paused ? "/pause" : sample.inspect.HasValue ? (sample.inspect.Value ? "/pass" : "/fail") : "")
                                    + "/" + station.name + "/eye=" + height + "/rect=" + size + "/title=" + title.preferredHeight + "/body=" + body.preferredHeight;
                                var expectedFont = language == ParticipantLanguage.Thai ? donor.thaiFont : donor.japaneseFont;
                                Check(title.font == expectedFont && body.font == expectedFont, context, "title/body must use the current localized font", panel);
                                Check(title.preferredHeight > 0 && body.preferredHeight > 0, context, "native TMP title/body metrics must be initialized", panel);
                                minTitle = Mathf.Min(minTitle, title.preferredHeight); minBody = Mathf.Min(minBody, body.preferredHeight);
                                bool workingTutorial = sample.state == ResearchFlowState.TutorialInteractions || sample.state == ResearchFlowState.TutorialReferenceInspect;
                                bool workingTask = (sample.state == ResearchFlowState.Task1Active || sample.state == ResearchFlowState.Task2Active) && !sample.paused && !sample.inspect.HasValue;
                                if (!workingTutorial && !workingTask) Check(panel.centered, context, "modal must use centered placement", panel);
                                Check(!panel.PlacementBlocked, context, "modal must have a valid placement", panel);
                                Check(panel.GetComponent<Canvas>().enabled, context, "modal canvas must remain enabled", panel);
                                Check((bool)fitsView.Invoke(panel, new object[] { panel.transform.position, panel.transform.rotation }), context, "actual card must fit the copied camera viewport", panel);
                                Check(panel.PlacementClear(panel.transform.position, panel.transform.rotation), context, "actual card must clear the authored scene and sightlines", panel);
                                panel.transform.localScale = Vector3.one * .001f; // Check the resting native control rect, independent of enter animation.
                                var controls = panel.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(b => b.isActiveAndEnabled && b.IsInteractable()).Select(b => (RectTransform)b.transform)
                                    .Concat(panel.GetComponentsInChildren<ResearchDragHandle>(false).Where(h => panel.movable && h.isActiveAndEnabled).Select(h => (RectTransform)h.transform));
                                foreach (var control in controls)
                                {
                                    control.GetWorldCorners(actionCorners);
                                    bool clear = true;
                                    foreach (var corner in actionCorners.Concat(new[] { (actionCorners[0] + actionCorners[2]) * .5f }))
                                        foreach (var renderer in foreground)
                                        {
                                            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.bounds.size.x >= 4 || renderer.bounds.size.z >= 4) continue;
                                            var sight = corner - head.transform.position;
                                            if (renderer.bounds.IntersectRay(new Ray(head.transform.position, sight.normalized), out float hit) && hit < sight.magnitude - .08f) clear = false;
                                        }
                                    Check(clear, context, "visible action corners and center must not be occluded: " + control.name, panel);
                                }
                                if (sample.state == ResearchFlowState.ParticipantComplete && stationIndex == 2 && height == 1.2f)
                                    completionMetrics += "; " + language + " Fan completion rect=" + size + " title=" + title.preferredHeight + " body=" + body.preferredHeight;
                                if (workingTask && Vector3.Distance(panel.transform.position, head.transform.position) > 1.8f)
                                {
                                    var handle = panel.GetComponentsInChildren<ResearchDragHandle>(false).First(h => h.isActiveAndEnabled);
                                    var beforeDrag = panel.transform.position;
                                    var pointer = new UnityEngine.EventSystems.PointerEventData(null) { pointerId = 999 };
                                    const System.Reflection.BindingFlags dragFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                                    typeof(ResearchDragHandle).GetMethod("Begin", dragFlags).Invoke(handle, new object[] { pointer, beforeDrag });
                                    typeof(ResearchDragHandle).GetMethod("MoveAt", dragFlags).Invoke(handle, new object[] { beforeDrag, 1f / 72 });
                                    Check(handle.MoveSamples == 1 && Vector3.Distance(beforeDrag, panel.transform.position) < .001f, context,
                                        "stationary first drag sample must preserve the native recenter pose (direct core, not input callbacks)", panel);
                                    handle.OnEndDrag(pointer);
                                }
                                cases++;
                            }
                return "MODAL_PLACEMENT_PASS cases=" + cases + "; checks=" + checks + "; minTitle=" + minTitle + "; minBody=" + minBody + completionMetrics
                    + "; controller inactive, native TMP initialized; synthetic eye poses only, hardware pending";
            }
            finally { if (uiRoot) UnityEngine.Object.DestroyImmediate(uiRoot); UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string Run()
        {
            var scene = SceneManager.GetActiveScene();
            var meshes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            foreach (var mesh in meshes.Where(m => m.name.Contains("Stool") || new[] { "Training_Practice_A", "Computer_Main", "Computer_Support", "Fan_Main", "Fan_Support" }.Contains(m.name)))
                Check(Mathf.Abs(mesh.sharedMesh.vertices.Min(v => mesh.transform.TransformPoint(v).y)) < .0001f, mesh.name + " actual feet must contact floor");
            foreach (var mesh in meshes.Where(m => m.name.EndsWith("_Floor")))
                Check(mesh.sharedMesh.uv.Length == mesh.sharedMesh.vertexCount && mesh.sharedMesh.uv.Distinct().Count() > 3, mesh.name + " needs nondegenerate UV0");
            Check(!meshes.Single(m => m.name == "Training_NeutralCube").gameObject.activeSelf, "Obsolete training block must be hidden");
            return "SPATIAL_SCENE_PASS checks=" + checks + "; actual mesh vertices, not physical Quest evidence";
        }
    }
}
#endif
