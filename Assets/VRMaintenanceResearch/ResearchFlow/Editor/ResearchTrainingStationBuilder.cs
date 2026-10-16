#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Grab;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.AssetLibraryQA.Editor;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Adds the neutral Trainer_FX1 practice station (S1 §4) to the canonical scene. Rebuilds the station root if present.
    public static class ResearchTrainingStationBuilder
    {
        const string Scene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const string Fbx = "Assets/VRMaintenanceResearch/AssetLibraryQA_V2/FBX/";
        const string RootName = "ResearchFlow_TrainingStation";
        static readonly Vector3 BasePosition = new Vector3(9.72f, .921f, 4.20f), ModulePosition = new Vector3(9.72f, .921f, 4.40f);

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            // Reopen from disk: Play leaves runtime UI drift in memory that must not be saved into the scene.
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<ResearchSessionFlowController>(FindObjectsInactive.Include);
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (existing) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject(RootName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var pivot = new GameObject("Turntable").transform;
            pivot.SetParent(root.transform, false); pivot.position = BasePosition;

            var fixture = Instance("Trainer_FX1/TRAINER_FX1_BASE", pivot);
            fixture.transform.position = BasePosition;
            // Module side and latch toward the participant; screw and plate inserts at the back, reached by turning the station.
            fixture.transform.rotation = Quaternion.Euler(0, 180, 0) * fixture.transform.rotation;
            var interactables = new List<GameObject>();

            var moduleSocket = fixture.AddComponent<QASocket>();
            moduleSocket.fixtureId = "training.trainer"; moduleSocket.seatName = "Module";
            moduleSocket.seat = Node(fixture, "SEAT_TRAINER_FX1_BASE_Module");
            // The pocket is ~65 x 42 mm for a 60 x 40 mm module and the base collider is one box over it, so nothing guides the held
            // (kinematic) module in. A 6 mm window gave 97 s of no response on Quest; 15 mm captures once the module visibly covers the
            // pocket. Turn still decides keyed vs reversed (0/180 deg, +-15).
            moduleSocket.spec = new SeatSpec { approachMm = 25, posTolMm = 15, angTolDeg = 15, hysteresisMm = 2, hysteresisDeg = 4, seatedDepthTolMm = .5f };
            // Chamfered key: the right way seats, the reversed module stops at the entrance chamfer.
            moduleSocket.rules = new List<PairRule>
            {
                new PairRule { plug = "TRAINER_FX1_MODULE", mate = "Slot", testId = "training.module.keyed", category = "keyed", outcome = PairOutcome.Seat, turnDeg = 0 },
                new PairRule { plug = "TRAINER_FX1_MODULE", mate = "Slot", testId = "training.module.reversed", category = "keyed", outcome = PairOutcome.Block, turnDeg = 180, reachMm = 9 },
            };

            var leadSocket = fixture.AddComponent<QASocket>();
            leadSocket.fixtureId = "training.trainer"; leadSocket.seatName = "E2";
            leadSocket.seat = Node(fixture, "SEAT_TRAINER_FX1_BASE_E2");
            leadSocket.spec = new SeatSpec { approachMm = 25, posTolMm = 5, angTolDeg = 15, hysteresisMm = 2, hysteresisDeg = 4, seatedDepthTolMm = .5f };
            leadSocket.rules = new List<PairRule> { new PairRule { plug = "TRAINER_FX1_LEAD", mate = "EndB2", testId = "training.lead", category = "connector", outcome = PairOutcome.Seat, turnDeg = 0 } };

            var module = Instance("Trainer_FX1/TRAINER_FX1_MODULE", root.transform);
            module.transform.position = ModulePosition;
            var moduleBody = module.AddComponent<Rigidbody>(); moduleBody.mass = .2f; moduleBody.interpolation = RigidbodyInterpolation.Interpolate;
            moduleBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var modulePlug = Plug(module, "TRAINER_FX1_MODULE", "training.trainer.module", moduleBody, GrabTypeFlags.Palm, interactables);

            // Latch: selecting toggles it; a pointable-less interactable never moves the lever by grab.
            var latch = Node(fixture, "MOV_TRAINER_FX1_BASE_ModuleLatch");
            var latchBox = latch.gameObject.AddComponent<BoxCollider>(); var latchBounds = latch.GetComponent<MeshFilter>().sharedMesh.bounds;
            latchBox.center = latchBounds.center; latchBox.size = latchBounds.size + Vector3.one * .01f;
            var latchBody = latch.gameObject.AddComponent<Rigidbody>(); latchBody.isKinematic = true; latchBody.useGravity = false;
            AssetQABuilder.AddInteractable(latch, latchBody, null, GrabTypeFlags.All);
            var latchInteractable = latch.GetComponentsInChildren<MonoBehaviour>(true).First(m => m is IInteractableView && m.transform.parent == latch);
            interactables.Add(latchInteractable.gameObject);

            var screwGo = Instance("Tools_Fasteners/SCREW_632_CASE", fixture.transform);
            var screwBody = screwGo.AddComponent<Rigidbody>(); screwBody.isKinematic = true; screwBody.useGravity = false;
            var screwGrab = screwGo.AddComponent<Grabbable>();
            var screw = screwGo.AddComponent<ResearchTrainingScrew>();
            screw.seat = Node(fixture, "SEAT_TRAINER_FX1_BASE_Insert1"); screw.mate = Node(screwGo, "MATE_SCREW_632_CASE_Thread");
            screw.body = screwBody; screw.grabbable = screwGrab; screw.travelMm = 3; screw.fullDegrees = 540;
            screwGrab.InjectOptionalRigidbody(screwBody); screwGrab.InjectOptionalOneGrabTransformer(screw); screwGrab.MaxGrabPoints = 1;
            Enlarge(screwGo, .012f);
            AssetQABuilder.AddInteractable(screwGo.transform, screwBody, screwGrab, GrabTypeFlags.Pinch);
            interactables.Add(screwGo.transform.Find("HandGrabInteractable").gameObject);

            // Lead: end B1 is fixed in E1; the B2 plug head is the only grab point and must be inserted into E2.
            var lead = Instance("Trainer_FX1/TRAINER_FX1_LEAD", fixture.transform);
            // The plug bone moves under its own grab root; a prefab instance refuses that restructure.
            PrefabUtility.UnpackPrefabInstance(lead, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var e1 = Node(fixture, "SEAT_TRAINER_FX1_BASE_E1");
            var b1 = Node(lead, "B_TRAINER_FX1_LEAD_B1"); var mateB1 = Node(lead, "MATE_TRAINER_FX1_LEAD_EndB1");
            var align = e1.rotation * Quaternion.Inverse(mateB1.rotation);
            lead.transform.rotation = align * lead.transform.rotation;
            lead.transform.position += e1.position - mateB1.position;
            var b2 = Node(lead, "B_TRAINER_FX1_LEAD_B2");
            var head = new GameObject("TrainingLeadHead");
            head.transform.SetParent(fixture.transform, false);
            head.transform.SetPositionAndRotation(b2.position, b2.rotation);
            b2.SetParent(head.transform, true);
            if (b2.parent != head.transform) throw new InvalidOperationException("Lead plug bone could not move under its grab root.");
            var headCollider = head.AddComponent<BoxCollider>(); headCollider.size = new Vector3(.02f, .02f, .03f);
            var headBody = head.AddComponent<Rigidbody>(); headBody.isKinematic = true; headBody.useGravity = false;
            var leadPlug = Plug(head, "TRAINER_FX1_LEAD", "training.trainer.lead_end", headBody, GrabTypeFlags.Pinch, interactables);
            var mateB2 = Node(head, "MATE_TRAINER_FX1_LEAD_EndB2");
            leadPlug.mates = new List<Transform> { mateB2 };
            // Rest: unplugged, upright, 5 cm toward the participant from E2 and 2 cm above the base.
            var e2 = leadSocket.seat;
            var restMate = Matrix4x4.TRS(e2.position + Vector3.forward * .05f + Vector3.up * .02f, e2.rotation, Vector3.one);
            var restHead = restMate * (head.transform.worldToLocalMatrix * mateB2.localToWorldMatrix).inverse;
            head.transform.SetPositionAndRotation(restHead.GetColumn(3), restHead.rotation);

            // Turntable handle at the front edge; turning it yaws the whole station.
            var handle = new GameObject("TurntableHandle");
            handle.transform.SetParent(fixture.transform, false);
            handle.transform.position = Node(fixture, "GRAB_TRAINER_FX1_BASE_Handles").position;
            handle.transform.rotation = Quaternion.identity;
            var handleBox = handle.AddComponent<BoxCollider>(); handleBox.size = new Vector3(.14f, .04f, .05f);
            var handleBody = handle.AddComponent<Rigidbody>(); handleBody.isKinematic = true; handleBody.useGravity = false;
            var handleGrab = handle.AddComponent<Grabbable>();
            var turn = handle.AddComponent<ResearchTrainingTurntable>(); turn.pivot = pivot;
            handleGrab.InjectOptionalRigidbody(handleBody); handleGrab.InjectOptionalOneGrabTransformer(turn); handleGrab.MaxGrabPoints = 1;
            AssetQABuilder.AddInteractable(handle.transform, handleBody, handleGrab, GrabTypeFlags.Palm);
            interactables.Add(handle.transform.Find("HandGrabInteractable").gameObject);

            var station = root.AddComponent<ResearchTrainingStation>();
            station.controller = controller; station.turntable = pivot; station.turnHandle = turn;
            station.module = modulePlug; station.moduleSocket = moduleSocket;
            station.latch = latch; station.latchInteractable = latchInteractable; station.latchAnimationRoot = fixture;
            station.latchOpenClip = AssetDatabase.LoadAllAssetsAtPath(Fbx + "Trainer_FX1/TRAINER_FX1_BASE.fbx").OfType<AnimationClip>().Single(c => c.name == "TRAINER_FX1_BASE__ModuleLatch_open");
            station.screw = screw; station.leadHead = leadPlug; station.leadSocket = leadSocket; station.leadAnchor = e1;
            station.leadBones = Enumerable.Range(0, 6).Select(i => Node(lead, "B_TRAINER_FX1_LEAD_J0" + i)).ToArray();
            station.leadReach = .2f; station.accessYaw = 120;
            station.interactables = interactables.ToArray();
            foreach (var go in interactables) go.SetActive(false);
            AddExamine(controller, station, root.transform, module);

            var so = new SerializedObject(controller);
            so.FindProperty("trainingStation").objectReferenceValue = station;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            return "built " + RootName + " interactables=" + interactables.Count;
        }

        static GameObject Instance(string path, Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx + path + ".fbx") ?? throw new InvalidOperationException("Missing model " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string kind = t.name.Split('_')[0];
                var renderer = t.GetComponent<MeshRenderer>();
                if (kind == "COL" && t.GetComponent<MeshFilter>())
                {
                    var b = t.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var box = t.gameObject.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size;
                }
                if (renderer && (kind == "COL" || kind == "SEL" || kind == "INS" || t.name.Contains("_LOD") && !t.name.EndsWith("_LOD0"))) renderer.enabled = false;
            }
            var animator = go.GetComponent<Animator>(); if (animator) animator.enabled = false;
            return go;
        }

        static QAPlug Plug(GameObject go, string asset, string stableId, Rigidbody body, GrabTypeFlags types, List<GameObject> interactables)
        {
            var grabbable = go.AddComponent<Grabbable>();
            var plug = go.AddComponent<QAPlug>();
            plug.assetId = asset; plug.stableId = stableId; plug.body = body; plug.grabbable = grabbable;
            plug.mates = go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MATE_" + asset + "_")).ToList();
            grabbable.InjectOptionalRigidbody(body); grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.InjectOptionalThrowWhenUnselected(false); grabbable.InjectOptionalOneGrabTransformer(plug); grabbable.MaxGrabPoints = 1;
            AssetQABuilder.AddInteractable(go.transform, body, grabbable, types);
            interactables.Add(go.transform.Find("HandGrabInteractable").gameObject);
            return plug;
        }

        // Small parts get a generous selection volume (S1 §3: "พื้นที่เลือกที่พอดี") without changing their solid collider.
        static void Enlarge(GameObject go, float size)
        {
            var trigger = go.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = size;
        }

        static Transform Node(GameObject go, string name) =>
            go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? throw new InvalidOperationException("Missing node " + name);

        static void AddExamine(ResearchSessionFlowController controller, ResearchTrainingStation station, Transform parent, GameObject module)
        {
            var root = new GameObject("TrainingExamine", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            root.transform.SetParent(parent, false); root.transform.position = ModulePosition + Vector3.up * .2f; root.transform.localScale = Vector3.one * .001f;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(240, 96);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = controller.origin.Camera;
            var rect = ResearchFlowView.Rect(root.transform, "training.module", Vector2.zero, new Vector2(240, 96));
            var graphic = rect.gameObject.AddComponent<VRMaintenanceResearch.TrainingV2.TrainingRoundedGraphic>(); graphic.color = Color.white; graphic.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            var label = ResearchFlowView.Rect(rect, "Label", Vector2.zero, new Vector2(220, 88)).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = controller.view.thaiFont; label.fontSize = 28; label.alignment = TextAlignmentOptions.Center; label.color = ResearchUITheme.Ink; label.raycastTarget = false;
            var target = rect.gameObject.AddComponent<ResearchExamineTarget>();
            target.controller = controller; target.training = station; target.stableTarget = "training.module";
            target.stationCanvas = canvas; target.stationGroup = root.GetComponent<CanvasGroup>();
            target.ConfigureFocus(module.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray(), module.transform);
            var interactions = QuickActionsAPI.AddRayCanvasInteraction(root).Concat(QuickActionsAPI.AddPokeCanvasInteraction(root)).ToArray();
            target.interactionRoots = interactions;
            canvas.enabled = false;
            foreach (var interaction in interactions) interaction.SetActive(false);
        }
    }
}
#endif
