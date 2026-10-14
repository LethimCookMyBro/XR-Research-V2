#if UNITY_EDITOR
using System;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskBBuilder
    {
        private const string Folder = "Assets/VRMaintenanceResearch/AssetLibraryQA_V2/FBX/Fan_Pedestal/";
        private const string RootName = "ResearchS1TaskB";

        public static string CheckAuthored(ResearchS1TaskBMechanics task)
        {
            if (Application.isPlaying || !task) throw new ArgumentException("Check the authored fixture in Edit Mode.");
            var state = task.Capture();
            var result = ResearchS1TaskEvaluator.Evaluate(state);
            if (!state.systemIntegrityValid || !state.powerIsolated || state.motorRunning || !state.baseStable || state.assemblyObstructed ||
                state.frontGuardPlacement != S1Placement.Seated || !state.frontGuardAligned || !state.frontGuardClipped ||
                state.bladePlacement != S1Placement.Seated || !state.bladeCompatible || !state.bladeKeyAligned || state.spinner != S1ThreadState.Tight ||
                state.rearGuardPlacement != S1Placement.Seated || !state.rearGuardAligned || state.rearLock != S1ThreadState.EngagedLoose ||
                result.Category != S1InspectionCategory.SymptomPresent ||
                state.original_fault_id != "B04.rear_guard_lock_engaged_loose" || state.original_fault_active != true ||
                state.user_created_issues == null || state.user_created_issues.Length != 0)
                throw new InvalidOperationException("Authored Task B differs from the one-fault initial state: " + JsonUtility.ToJson(state));
            return "S1_TASK_B_AUTHORED_INITIAL_PASS category=" + result.Category + "; rear_lock=" + state.rearLock +
                "; spinner=" + state.spinner + "; input_e2e=false; source_dimensions=estimated";
        }

        public static ResearchS1TaskBMechanics Build(Transform parent, ResearchTaskPort port, ResearchSessionFlowController controller)
        {
            if (Application.isPlaying || !parent || !port || port.taskId != "B" || !controller)
                throw new ArgumentException("Build Task B in Edit Mode with an explicit parent, B port and current controller.");
            var existing = parent.Find(RootName);
            if (existing)
            {
                var previous = existing.GetComponent<ResearchS1TaskBMechanics>();
                if (previous && previous.port == port && previous.controller == controller) return previous;
                throw new InvalidOperationException("An unrelated object occupies the Task B authoring name.");
            }
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            try
            {
                var task = root.AddComponent<ResearchS1TaskBMechanics>();
                task.port = port;
                task.controller = controller;
                task.servicePivot = root.transform;
                var basis = Model("FAN_PED_BASE", root.transform);
                var stand = Model("FAN_PED_STAND", root.transform);
                var head = Model("FAN_PED_HEAD", root.transform);
                Align(stand, Node(stand, "MATE_FAN_PED_STAND_Bottom"), Node(basis, "SEAT_FAN_PED_BASE_Stand"));
                Align(head, Node(head, "MATE_FAN_PED_HEAD_PipeBottom"), Node(stand, "SEAT_FAN_PED_STAND_Pipe"));
                task.fixedAssembly = new[] { basis.transform, stand.transform, head.transform };
                task.fixedPositions = task.fixedAssembly.Select(t => t.localPosition).ToArray();
                task.fixedRotations = task.fixedAssembly.Select(t => t.localRotation).ToArray();
                task.motorShaft = Node(head, "MOV_FAN_PED_HEAD_Shaft");
                task.isolatedShaftRotation = task.motorShaft.localRotation;

                task.rearGuard = Part(task, "FAN_PED_GUARD_REAR", "B.rear_guard", S1FanPartKind.RearGuard,
                    "Plate", Node(head, "SEAT_FAN_PED_HEAD_GuardRear"), .45f, GrabTypeFlags.Palm, 30, 4);
                task.rearLock = Part(task, "FAN_PED_GUARD_LOCK", "B.rear_guard_lock", S1FanPartKind.RearLock,
                    "Thread", Node(head, "SEAT_FAN_PED_HEAD_GuardLock"), .04f, GrabTypeFlags.Pinch, 20, 3);
                task.rearLock.fullTurnDegrees = 540;
                task.rearLock.threadTravelMm = 16.9f;
                task.rearLock.rightHanded = true;
                // Source dimensions are estimated. This fixed simulated gap leaves one initial loose joint.
                task.rearLock.AuthorInitialPose(540 * (1 - .6f / 16.9f));
                task.blade = Part(task, "FAN_PED_BLADE", "B.blade", S1FanPartKind.Blade,
                    "Bore", Node(head, "SEAT_FAN_PED_HEAD_Blade"), .22f, GrabTypeFlags.Palm, 60, 3);
                task.spinner = Part(task, "FAN_PED_SPINNER", "B.spinner", S1FanPartKind.Spinner,
                    "Thread", Node(head, "SEAT_FAN_PED_HEAD_Spinner"), .02f, GrabTypeFlags.Pinch, 15, 3);
                task.spinner.fullTurnDegrees = 720;
                task.spinner.threadTravelMm = 5;
                task.spinner.rightHanded = false;
                task.spinner.AuthorInitialPose(720);
                task.frontGuard = Part(task, "FAN_PED_GUARD_FRONT", "B.front_guard", S1FanPartKind.FrontGuard,
                    "Rim", Node(task.rearGuard.gameObject, "SEAT_FAN_PED_GUARD_REAR_FrontLocked"), .5f, GrabTypeFlags.Palm, 20, 6);
                task.frontGuard.insertTurnDegrees = SocketRules.Measure(task.frontGuard.seat.localToWorldMatrix,
                    Node(task.rearGuard.gameObject, "SEAT_FAN_PED_GUARD_REAR_FrontInsert").localToWorldMatrix).turnDeg;
                if (Mathf.Abs(Mathf.Abs(task.frontGuard.insertTurnDegrees) - 12) > .1f)
                    throw new InvalidOperationException("Imported front guard anchor twist differs from the source 12-degree bayonet.");
                task.frontClip = Clip(task);
                port.contentApproved = false;
                root.SetActive(true);
                EditorUtility.SetDirty(port);
                return task;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static GameObject Model(string asset, Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + asset + ".fbx");
            if (!model) throw new InvalidOperationException("Missing imported original fan model " + asset);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = asset;
            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            {
                string kind = transform.name.Split('_')[0];
                var renderer = transform.GetComponent<MeshRenderer>();
                if (kind == "COL")
                {
                    var mesh = transform.GetComponent<MeshFilter>().sharedMesh;
                    var collider = transform.gameObject.AddComponent<BoxCollider>();
                    collider.center = mesh.bounds.center;
                    collider.size = mesh.bounds.size;
                }
                if (renderer && (kind == "COL" || kind == "SEL" || kind == "INS" || transform.name.Contains("_LOD") && !transform.name.EndsWith("_LOD0")))
                    renderer.enabled = false;
            }
            foreach (var animator in go.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            return go;
        }

        private static ResearchS1FanPart Part(ResearchS1TaskBMechanics owner, string asset, string stableId, S1FanPartKind kind,
            string mate, Transform seat, float mass, GrabTypeFlags grabTypes, float approach, float tolerance)
        {
            var go = Model(asset, owner.transform);
            var point = Node(go, "MATE_" + asset + "_" + mate);
            Align(go, point, seat);
            var part = go.AddComponent<ResearchS1FanPart>();
            part.owner = owner;
            part.stableId = stableId;
            part.kind = kind;
            part.seat = seat;
            part.mate = point;
            part.approachMm = approach;
            part.positionToleranceMm = tolerance;
            Grab(part, mass, grabTypes);
            if (!part.IsThread) part.AuthorInitialPose();
            return part;
        }

        private static ResearchS1FanPart Clip(ResearchS1TaskBMechanics owner)
        {
            var node = Node(owner.rearGuard.gameObject, "MOV_FAN_PED_GUARD_REAR_GuardClip");
            var part = node.gameObject.AddComponent<ResearchS1FanPart>();
            part.owner = owner;
            part.stableId = "B.front_guard_clip";
            part.kind = S1FanPartKind.FrontClip;
            part.clipAnimationRoot = owner.rearGuard.gameObject;
            var clips = AssetDatabase.LoadAllAssetsAtPath(Folder + "FAN_PED_GUARD_REAR.fbx").OfType<AnimationClip>().ToArray();
            part.clipOpen = clips.Single(c => c.name == "FAN_PED_GUARD_REAR__GuardClip_open");
            part.clipClose = clips.Single(c => c.name == "FAN_PED_GUARD_REAR__GuardClip_close");
            var mesh = node.GetComponent<MeshFilter>().sharedMesh;
            var collider = node.gameObject.AddComponent<BoxCollider>();
            collider.center = mesh.bounds.center;
            collider.size = mesh.bounds.size + Vector3.one * .004f;
            Grab(part, .01f, GrabTypeFlags.All);
            part.AuthorInitialPose();
            return part;
        }

        private static void Grab(ResearchS1FanPart part, float mass, GrabTypeFlags types)
        {
            var body = part.gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            part.body = body;
            var grabbable = part.gameObject.AddComponent<Grabbable>();
            part.grabbable = grabbable;
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalOneGrabTransformer(part);
            grabbable.MaxGrabPoints = 1;
            var hand = new GameObject("HandGrabInteractable").AddComponent<HandGrabInteractable>();
            hand.transform.SetParent(part.transform, false);
            hand.InjectAllHandGrabInteractable(types, body,
                Rule(FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Ignored, FingerRequirement.Ignored, FingerRequirement.Ignored),
                Rule(FingerRequirement.Optional, FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Required, FingerRequirement.Optional));
            hand.InjectOptionalPointableElement(grabbable);
            part.interactable = hand;
            var item = part.gameObject.AddComponent<HandsLabObject>();
            item.stableId = part.stableId;
            item.body = body;
            item.grabbable = grabbable;
        }

        private static GrabbingRule Rule(params FingerRequirement[] values)
        {
            var rule = new GrabbingRule();
            for (int i = 0; i < values.Length; i++) rule[(Oculus.Interaction.Input.HandFinger)i] = values[i];
            return rule;
        }

        private static Transform Node(GameObject go, string name) => go.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        private static void Align(GameObject go, Transform mate, Transform seat)
        {
            var pose = SocketRules.RootPose(seat.localToWorldMatrix, 0, 0, go.transform.worldToLocalMatrix * mate.localToWorldMatrix);
            go.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }
    }
}
#endif
