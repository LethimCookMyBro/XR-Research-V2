#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
            var front = task.frontGuard.mate.Find(ResearchS1TaskBMechanics.FrontMarkName);
            var rear = task.frontGuard.seat.Find(ResearchS1TaskBMechanics.RearMarkName);
            if (!front || !rear || front.GetComponentsInChildren<Collider>(true).Length + rear.GetComponentsInChildren<Collider>(true).Length > 0
                || Vector3.Distance(front.position, rear.position) > .0005f)
                throw new InvalidOperationException("Authored Task B lacks collider-free, aligned " + ResearchS1TaskBMechanics.AlignmentMarksVersion + " marks.");
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
                // Owner 7 Nov: the base's control strip faced away from the participant and its cord toward them. Turning the base
                // half a turn about its stand axis fixes both; the stand seat is on that axis and the screw seats and collider are symmetric.
                basis.transform.localRotation = Quaternion.AngleAxis(180, Vector3.up) * basis.transform.localRotation;
                task.fixedAssembly = new[] { basis.transform, stand.transform, head.transform };
                task.fixedPositions = task.fixedAssembly.Select(t => t.localPosition).ToArray();
                task.fixedRotations = task.fixedAssembly.Select(t => t.localRotation).ToArray();
                task.motorShaft = Node(head, "MOV_FAN_PED_HEAD_Shaft");
                task.isolatedShaftRotation = task.motorShaft.localRotation;

                task.rearGuard = Part(task, "FAN_PED_GUARD_REAR", "B.rear_guard", S1FanPartKind.RearGuard,
                    "Plate", Node(head, "SEAT_FAN_PED_HEAD_GuardRear"), .45f, GrabTypeFlags.Palm, 30, 4);
                task.rearLock = Part(task, "FAN_PED_GUARD_LOCK", "B.rear_guard_lock", S1FanPartKind.RearLock,
                    "Thread", Node(head, "SEAT_FAN_PED_HEAD_GuardLock"), .04f, GrabTypeFlags.Pinch, 20, 3);
                task.rearLock.fullTurnDegrees = ResearchS1TaskBMechanics.RearLockTurnDegrees;
                task.rearLock.threadTravelMm = 16.9f;
                task.rearLock.rightHanded = true;
                // One initial loose joint, short of the stop by more than grip jitter so touching it cannot fix it by accident.
                task.rearLock.AuthorInitialPose(ResearchS1TaskBMechanics.RearLockTurnDegrees - ResearchS1TaskBMechanics.RearLockFaultGapDegrees);
                task.blade = Part(task, "FAN_PED_BLADE", "B.blade", S1FanPartKind.Blade,
                    "Bore", Node(head, "SEAT_FAN_PED_HEAD_Blade"), .22f, GrabTypeFlags.Palm, 60, 3);
                task.spinner = Part(task, "FAN_PED_SPINNER", "B.spinner", S1FanPartKind.Spinner,
                    "Thread", Node(head, "SEAT_FAN_PED_HEAD_Spinner"), .02f, GrabTypeFlags.Pinch, 15, 3);
                task.spinner.fullTurnDegrees = ResearchS1TaskBMechanics.SpinnerTurnDegrees;
                task.spinner.threadTravelMm = 5;
                task.spinner.rightHanded = false;
                task.spinner.AuthorInitialPose(ResearchS1TaskBMechanics.SpinnerTurnDegrees);
                task.frontGuard = Part(task, "FAN_PED_GUARD_FRONT", "B.front_guard", S1FanPartKind.FrontGuard,
                    "Rim", Node(task.rearGuard.gameObject, "SEAT_FAN_PED_GUARD_REAR_FrontLocked"), .5f, GrabTypeFlags.Palm, 20, 6);
                task.frontGuard.insertTurnDegrees = SocketRules.Measure(task.frontGuard.seat.localToWorldMatrix,
                    Node(task.rearGuard.gameObject, "SEAT_FAN_PED_GUARD_REAR_FrontInsert").localToWorldMatrix).turnDeg;
                if (Mathf.Abs(Mathf.Abs(task.frontGuard.insertTurnDegrees) - 12) > .1f)
                    throw new InvalidOperationException("Imported front guard anchor twist differs from the source 12-degree bayonet.");
                task.frontClip = Clip(task);
                AddAlignmentMarks(task);
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

        // Applies the marks to the Task B already in the open scene; the caller saves the scene.
        public static string ApplyAlignmentMarksToScene()
        {
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (Application.isPlaying || !task) throw new InvalidOperationException("Open the S1 scene in Edit Mode.");
            AddAlignmentMarks(task);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(task.gameObject.scene);
            return CheckAuthored(task);
        }

        // A triangle on the front-guard rim meets a tab on the rear-guard rim only at the locked turn (spec 6.3 reads alignment marks;
        // the imported model had none). Visual only: no collider, one neutral non-emissive colour that never changes with state.
        public static void AddAlignmentMarks(ResearchS1TaskBMechanics task)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MarkFolder + "Materials/M_S1_AlignmentMark.mat");
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_S1_AlignmentMark" };
                AssetDatabase.CreateAsset(material, MarkFolder + "Materials/M_S1_AlignmentMark.mat");
            }
            material.SetColor("_BaseColor", new Color(.18f, .19f, .21f));
            material.SetFloat("_Smoothness", .2f);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            // Seat-local metres: -y is 12 o'clock, -z faces the participant. The front rim band spans r .229-.234 and z -.004..005;
            // the rear rim ends at r .228, so its tab reaches past the front rim to stay visible from the front.
            Mark(task.frontGuard.mate, ResearchS1TaskBMechanics.FrontMarkName, "Triangle",
                new[] { new Vector2(-.013f, -.214f), new Vector2(0, -.238f), new Vector2(.013f, -.214f) }, -.012f, 0, material);
            Mark(task.frontGuard.seat, ResearchS1TaskBMechanics.RearMarkName, "Tab",
                new[] { new Vector2(-.004f, -.224f), new Vector2(-.004f, -.254f), new Vector2(.004f, -.254f), new Vector2(.004f, -.224f) }, .002f, .008f, material);
            AssetDatabase.SaveAssets();
        }

        private const string MarkFolder = "Assets/VRMaintenanceResearch/ResearchFlow/";

        private static void Mark(Transform frame, string name, string shape, Vector2[] outline, float zFront, float zBack, Material material)
        {
            if ((frame.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) throw new InvalidOperationException(name + " frame is scaled; the mark size would drift.");
            string path = MarkFolder + "Meshes/S1_AlignmentMark_" + shape + ".asset";
            var built = Prism(outline, zFront, zBack);
            built.name = "S1_AlignmentMark_" + shape;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) EditorUtility.CopySerialized(built, mesh); else AssetDatabase.CreateAsset(mesh = built, path);
            var found = frame.Find(name);
            var mark = found ? found.gameObject : new GameObject(name);
            mark.transform.SetParent(frame, false);
            mark.transform.localPosition = Vector3.zero;
            mark.transform.localRotation = Quaternion.identity;
            mark.transform.localScale = Vector3.one;
            var filter = mark.GetComponent<MeshFilter>();
            if (!filter) filter = mark.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = mark.GetComponent<MeshRenderer>();
            if (!renderer) renderer = mark.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        // Convex outline extruded along z, flat-shaded, faces wound to point outward.
        private static Mesh Prism(Vector2[] outline, float zFront, float zBack)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector2 centre = outline.Aggregate(Vector2.zero, (a, b) => a + b) / outline.Length;
            void Face(Vector3 outward, params Vector3[] corners)
            {
                for (int i = 1; i + 1 < corners.Length; i++)
                {
                    Vector3 a = corners[0], b = corners[i], c = corners[i + 1];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) (b, c) = (c, b);
                    foreach (var v in new[] { a, b, c }) { triangles.Add(vertices.Count); vertices.Add(v); }
                }
            }
            Face(Vector3.back, outline.Select(p => new Vector3(p.x, p.y, zFront)).ToArray());
            Face(Vector3.forward, outline.Select(p => new Vector3(p.x, p.y, zBack)).ToArray());
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % outline.Length], m = (p + q) / 2 - centre;
                Face(new Vector3(m.x, m.y, 0), new Vector3(p.x, p.y, zFront), new Vector3(q.x, q.y, zFront), new Vector3(q.x, q.y, zBack), new Vector3(p.x, p.y, zBack));
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
            Grab(part, .01f, GrabTypeFlags.Pinch);
            BakeClipPoses(part);
            part.AuthorInitialPose();
            return part;
        }

        // Owner 10 Nov: the clip answered Grip as well as Trigger, at the rim where the guard is gripped. Headset log bb2a9106
        // (30 Oct) holds 19 clip grabs and 38 clip toggles in one task while the wearer tried to pull the guard. Trigger opens or
        // closes the clip, as the reference media say; Grip there now always takes the guard. Idempotent so authored scenes can migrate.
        public static void ClipAnswersTriggerOnly(ResearchS1TaskBMechanics task)
        {
            if (EditorApplication.isPlaying || !task || !task.frontClip || !task.frontClip.interactable)
                throw new InvalidOperationException("A stopped authored fan clip is required.");
            task.frontClip.interactable.InjectSupportedGrabTypes(GrabTypeFlags.Pinch);
            EditorUtility.SetDirty(task.frontClip.interactable);
        }

        public static void BakeClipPoses(ResearchS1FanPart part)
        {
            if (EditorApplication.isPlaying || !part || part.kind != S1FanPartKind.FrontClip)
                throw new InvalidOperationException("A stopped authored fan clip is required.");
            string path = AnimationUtility.CalculateTransformPath(part.transform, part.clipAnimationRoot.transform);
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(part.clipAnimationRoot);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                var clip = root.transform.Find(path) ?? throw new InvalidOperationException("Authored clip node missing.");
                part.clipOpen.SampleAnimation(root, part.clipOpen.length);
                var open = new Pose(clip.localPosition, clip.localRotation);
                part.clipClose.SampleAnimation(root, part.clipClose.length);
                var closed = new Pose(clip.localPosition, clip.localRotation);
                part.AuthorClipPoses(open, closed);
                EditorUtility.SetDirty(part);
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
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
