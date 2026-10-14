#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static partial class ResearchS1TaskABuilder
    {
        private const string Models = "Assets/VRMaintenanceResearch/AssetLibraryQA_V2/FBX/";
        private const string GpuModel = "Assets/VRMaintenanceResearch/ResearchFlow/Meshes/S1_GPU_GXB_8PIN.fbx";
        private const string Generated = "Assets/VRMaintenanceResearch/ResearchFlow/S1Authored";
        private const string RootName = "ResearchS1TaskA";

        public static ResearchS1TaskAMechanics Build(Transform parent, ResearchTaskPort port, ResearchSessionFlowController controller, string layout)
        {
            if (Application.isPlaying || !parent || !port || port.taskId != "A" || !controller)
                throw new ArgumentException("Build Task A in Edit Mode with explicit parent, A port and controller.");
            ResearchS1TaskACatalog.LayoutIndex(0, 1, layout);
            var existing = parent.Find(RootName);
            if (existing)
            {
                var task = existing.GetComponent<ResearchS1TaskAMechanics>();
                if (task && task.port == port && task.controller == controller && task.layout == layout && task.AuthoredBindingsValid()) return task;
                throw new InvalidOperationException("Existing Task A differs from this authoring request.");
            }
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            try
            {
                var task = root.AddComponent<ResearchS1TaskAMechanics>();
                task.port = port; task.controller = controller; task.layout = layout;
                task.servicePivot = new GameObject("ServicePivot").transform;
                task.servicePivot.SetParent(root.transform, false);
                var shell = Model("PC_Case", "CASE_ATX_MID", task.servicePivot);
                var board = Model("PC_Board", "MB_B760_DDR4", task.servicePivot);
                var cpu = Model("PC_Board", "CPU_LGA1700", task.servicePivot);
                var cooler = Model("PC_Cooling", "COOLER_LGA1700_STOCK", task.servicePivot);
                var psu = Model("PC_Power", "PSU_ATX_MX650", task.servicePivot);
                Align(board, "MATE_MB_B760_DDR4_Hole1", Node(shell, "SEAT_CASE_ATX_MID_Standoff1"));
                Align(cpu, "MATE_CPU_LGA1700_Socket", Node(board, "SEAT_MB_B760_DDR4_CPU"));
                Align(cooler, "MATE_COOLER_LGA1700_STOCK_Base", Node(board, "SEAT_MB_B760_DDR4_Cooler"));
                Align(psu, "MATE_PSU_ATX_MX650_Bay", Node(shell, "SEAT_CASE_ATX_MID_PSU"));
                task.fixedAssembly = new[] { shell.transform, board.transform, cpu.transform, cooler.transform, psu.transform };
                task.fixedPositions = task.fixedAssembly.Select(t => t.localPosition).ToArray();
                task.fixedRotations = task.fixedAssembly.Select(t => t.localRotation).ToArray();
                var inputs = new List<ResearchS1TaskAInput>();
                string[] slots = { "DIMM_A1", "DIMM_A2", "DIMM_B1", "DIMM_B2" };
                task.ramSlots = slots.Select((slot, i) => Slot(task, board, slot, "DimmLatch" + (i + 1), true, inputs)).ToArray();
                task.gpuSlots = new[] { "PCIEX16_1", "PCIEX16_2" }.Select(slot => Slot(task, board, slot, slot + "_Latch", false, inputs)).ToArray();
                task.ram = new[] { "M01", "M02", "M03", "M04", "M05" }.Select((id, i) =>
                {
                    var part = Part(task, id, "PC_Memory", inputs);
                    part.transform.localRotation = Quaternion.AngleAxis(90, Vector3.up) * part.transform.localRotation;
                    PlaceOption(part.gameObject, new Vector3(-1.125f + .06f * ResearchS1TaskACatalog.LayoutIndex(i, 5, layout), -.0085f, -.05f));
                    return part;
                }).ToArray();
                task.gpus = new[] { "G01", "G02" }.Select((id, i) =>
                {
                    var part = Part(task, id, "PC_GPU", inputs);
                    PlaceOption(part.gameObject, new Vector3(-1.1f + 2f * ResearchS1TaskACatalog.LayoutIndex(i, 2, layout), .0075f, -.35f));
                    return part;
                }).ToArray();
                task.ssds = new[] { "S01", "S02", "S03" }.Select((id, i) =>
                {
                    var sled = Model("PC_Case", "CASE_SLED_25", root.transform);
                    var ssd = Model("PC_Storage", ResearchS1TaskACatalog.Asset(id), sled.transform);
                    Align(ssd, "MATE_SSD_SATA_25_7MM_Bottom", Node(sled, "SEAT_CASE_SLED_25_Drive"));
                    var part = ConfigurePart(task, sled, "CASE_SLED_25", id, inputs);
                    ReplaceLabel(ssd, id);
                    PlaceOption(sled, new Vector3(.85f + .13f * ResearchS1TaskACatalog.LayoutIndex(i, 3, layout), -.006f, .31f));
                    return part;
                }).ToArray();
                task.traySeats = new[] { 1, 2 }.Select(i => Node(shell, "SEAT_CASE_ATX_MID_Sled25_" + i)).ToArray();
                task.gpuScrewSeats = new[] { 1, 4 }.Select(i => Node(shell, "SEAT_CASE_ATX_MID_SlotScrew" + i)).ToArray();
                task.sleds = task.ssds.Select(part => Sled(task, part, inputs)).ToArray();
                task.gpuScrews = new[] { 1, 2 }.Select(i =>
                {
                    var model = Model("Tools_Fasteners", "SCREW_632_CASE", root.transform);
                    var screw = Fastener(task, model, "A.gpu_fastener" + i, Node(model, "MATE_SCREW_632_CASE_Thread"), 5.1f, inputs);
                    PlaceOption(model, new Vector3(.94f + .06f * i, -.0025f, -.25f));
                    screw.AuthorOpen();
                    return screw;
                }).ToArray();
                CorrectCollisionProxies(task);
                foreach (var gpu in task.ram.Concat(task.gpus))
                {
                    var retention = gpu.gameObject.AddComponent<ResearchS1TaskAPlugMotion>();
                    retention.owner = task; retention.plug = gpu; retention.input = gpu.GetComponent<ResearchS1TaskAInput>();
                    gpu.grabbable.InjectOptionalOneGrabTransformer(retention);
                }
                task.wiring = Wiring(task, inputs);
                task.serviceYaw = ServiceYaw(task, inputs);
                task.inputs = inputs.ToArray();
                var left = Model("PC_Case", "CASE_PANEL_L", root.transform);
                var right = Model("PC_Case", "CASE_PANEL_R", root.transform);
                PlaceOption(left, new Vector3(-1.28f, -.0273f, .51f));
                PlaceOption(right, new Vector3(.86f, -.0273f, .30f));
                port.contentApproved = false;
                root.SetActive(true);
                if (!task.AuthoredBindingsValid()) throw new InvalidOperationException("Task A authoring self-check failed.");
                EditorUtility.SetDirty(port);
                return task;
            }
            catch { UnityEngine.Object.DestroyImmediate(root); throw; }
        }

        public static string CheckAuthored(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task || !task.AuthoredBindingsValid()) throw new ArgumentException("Check valid authored Task A in Edit Mode.");
            var state = task.Capture();
            if (state.ram.Any(p => p.placement != S1Placement.NotInstalled) || state.gpus.Any(p => p.placement != S1Placement.NotInstalled) ||
                state.ssds.Any(p => p.placement != S1Placement.NotInstalled) || ResearchS1TaskEvaluator.Evaluate(state).Category != S1InspectionCategory.AssemblyIncomplete)
                throw new InvalidOperationException("Partial Task A authoring must not report experiment readiness.");
            return "S1_TASK_A_MOUNTING_AUTHORED options=10; sockets=6; single_latches=6; sleds=3; thumbs=3; gpu_screws=2; layout=" + task.layout +
                "; system_integrity=true";
        }

        // Imported COL proxies are single bounding boxes, and three claim material the visual model lacks: the case rear sheet spans
        // the slot pocket opening and the tray spans its three cable routing slots (fam_case.py chassis_steel), and the screw box
        // carries head width down the whole shaft. They refused every GPU bracket screw turn and every rear SATA route.
        // Idempotent so authored scenes can migrate.
        public static void CorrectCollisionProxies(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task) throw new ArgumentException("Correct Task A proxies in Edit Mode.");
            var shell = task.fixedAssembly[0].gameObject;
            // Sheet-local metres: source x and y are kept, z is reflected about each sheet centre (244.5 mm).
            var rear = Node(shell, "COL_CASE_ATX_MID_Rear");
            float tab = rear.InverseTransformPoint(task.gpuScrewSeats[0].position).y;
            CarveSheet(rear.GetComponent<BoxCollider>(), 1, 2, Rect.MinMaxRect(-.086f, -.024f, tab, .11742f));
            var tray = Node(shell, "COL_CASE_ATX_MID_Tray");
            var slots = new[] { 150f, 250f, 350f }.Select(z => Rect.MinMaxRect(.11075f, (244.5f - z - 60) * .001f, .13075f, (244.5f - z) * .001f)).ToArray();
            if (CarveSheet(tray.GetComponent<BoxCollider>(), 0, 2, slots))
                for (int i = 0; i < slots.Length; i++)
                {
                    var marker = new GameObject("S1_TrayRoutingSlot" + (i + 1)).transform;
                    marker.SetParent(tray, false); marker.localPosition = new Vector3(slots[i].center.x, 0, slots[i].center.y);
                }
            foreach (var screw in task.gpuScrews)
            {
                var body = Node(screw.gameObject, "COL_SCREW_632_CASE_Body").GetComponent<BoxCollider>();
                if (!body) continue;
                UnityEngine.Object.DestroyImmediate(body);
                // Imported mate +Z runs bearing face to tip: 9.5 x 3.4 mm hex-washer head, 3.44 x 6 mm #6-32 major diameter.
                RoundSolid(screw.mate, "S1_ScrewHeadSolid", .0095f, -.0017f, .0034f);
                RoundSolid(screw.mate, "S1_ScrewShaftSolid", .00344f, .003f, .006f);
            }
            if (task.wiring) CorrectCablePlugProxies(task);
        }

        // Splits a thin-sheet BoxCollider into boxes around rectangular openings given in its local (u, v) plane axes.
        private static bool CarveSheet(BoxCollider box, int u, int v, params Rect[] openings)
        {
            if (box.GetComponents<BoxCollider>().Length > 1) return false;
            Vector3 c = box.center, s = box.size;
            float v0 = c[v] - s[v] / 2, v1 = c[v] + s[v] / 2;
            var edges = openings.SelectMany(o => new[] { o.xMin, o.xMax }).Concat(new[] { c[u] - s[u] / 2, c[u] + s[u] / 2 }).Distinct().OrderBy(e => e).ToArray();
            var unused = box;
            for (int i = 0; i + 1 < edges.Length; i++)
            {
                float mid = (edges[i] + edges[i + 1]) / 2, from = v0;
                foreach (var o in openings.Where(o => o.xMin < mid && mid < o.xMax).OrderBy(o => o.yMin).Append(Rect.MinMaxRect(0, v1, 0, v1)))
                {
                    if (o.yMin > from)
                    {
                        var piece = unused ? unused : box.gameObject.AddComponent<BoxCollider>(); unused = null;
                        Vector3 center = c, size = s;
                        center[u] = mid; size[u] = edges[i + 1] - edges[i]; center[v] = (from + o.yMin) / 2; size[v] = o.yMin - from;
                        piece.center = center; piece.size = size;
                    }
                    from = Mathf.Max(from, o.yMax);
                }
            }
            return true;
        }

        private static void RoundSolid(Transform parent, string name, float diameter, float z, float length)
        {
            for (int i = 0; i < 4; i++)
            {
                var solid = new GameObject(name + i).transform;
                solid.SetParent(parent, false); solid.localPosition = new Vector3(0, 0, z);
                solid.localRotation = Quaternion.AngleAxis(i * 45, Vector3.forward);
                solid.gameObject.AddComponent<BoxCollider>().size = new Vector3(diameter, diameter * Mathf.Sin(Mathf.PI / 8), length);
            }
        }

        private static ResearchS1Sled Sled(ResearchS1TaskAMechanics task, QAPlug part, List<ResearchS1TaskAInput> inputs)
        {
            var sled = part.gameObject.AddComponent<ResearchS1Sled>();
            sled.owner = task; sled.part = part; sled.input = part.GetComponent<ResearchS1TaskAInput>();
            sled.input.customMotion = true;
            sled.mate = Node(part.gameObject, "MATE_CASE_SLED_25_Tray");
            part.grabbable.InjectOptionalOneGrabTransformer(sled);
            part.enabled = false;
            var thumb = Node(part.gameObject, "MOV_CASE_SLED_25_Thumb");
            var seat = new GameObject("S1_ThumbBearing").transform;
            seat.SetParent(thumb.parent, false); seat.SetPositionAndRotation(thumb.position, thumb.rotation);
            var mate = new GameObject("S1_ThumbMate").transform; mate.SetParent(thumb, false);
            var mesh = thumb.GetComponent<MeshFilter>().sharedMesh;
            var collider = thumb.gameObject.AddComponent<BoxCollider>(); collider.center = mesh.bounds.center; collider.size = mesh.bounds.size + Vector3.one * .004f; collider.isTrigger = true;
            // shortcut: the mating shaft uses guide constraints, add shaft solids only with matching bores and shaft/world verification.
            RoundSolid(thumb, "S1_ThumbHeadSolid", mesh.bounds.size.x, -.0028f, .0044f);
            // A 5 um inset at each face avoids the observed flush tray cast contact.
            var plate = Node(part.gameObject, "COL_CASE_SLED_25_Plate").GetComponent<BoxCollider>();
            plate.size = plate.size - new Vector3(0, 0, .000010f);
            sled.thumb = Fastener(task, thumb.gameObject, part.stableId + ".tray_fastener", mate, 2.5f, inputs);
            sled.thumb.sled = sled; sled.thumb.captiveSeat = seat; sled.thumb.AuthorOpen();
            return sled;
        }

        private static ResearchS1TaskAFastener Fastener(ResearchS1TaskAMechanics task, GameObject model, string id, Transform mate,
            float travelMm, List<ResearchS1TaskAInput> inputs)
        {
            var body = model.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true; body.mass = .01f;
            var grab = model.AddComponent<Grabbable>();
            var fastener = model.AddComponent<ResearchS1TaskAFastener>();
            fastener.owner = task; fastener.body = body; fastener.grabbable = grab; fastener.mate = mate; fastener.travelMm = travelMm;
            grab.InjectOptionalRigidbody(body); grab.InjectOptionalKinematicWhileSelected(true); grab.InjectOptionalThrowWhenUnselected(false);
            grab.InjectOptionalOneGrabTransformer(fastener); grab.MaxGrabPoints = 1;
            var hand = Hand(model.transform, body, grab, GrabTypeFlags.All);
            var input = model.AddComponent<ResearchS1TaskAInput>(); input.owner = task; input.stableId = id; input.pointable = grab;
            input.interactable = hand; input.customMotion = true; fastener.input = input; inputs.Add(input);
            var item = model.AddComponent<HandsLabObject>(); item.stableId = id; item.body = body; item.grabbable = grab;
            return fastener;
        }

        private static QASocket Slot(ResearchS1TaskAMechanics task, GameObject board, string name, string latchName, bool ram,
            List<ResearchS1TaskAInput> inputs)
        {
            var socket = board.AddComponent<QASocket>();
            socket.fixtureId = "A.motherboard"; socket.seatName = name; socket.seat = Node(board, "SEAT_MB_B760_DDR4_" + name);
            socket.spec = new SeatSpec { approachMm = 30, posTolMm = ram ? 4 : 3, angTolDeg = ram ? 8 : 5,
                hysteresisMm = ram ? 1.5f : 1, hysteresisDeg = ram ? 3 : 2, seatedDepthTolMm = .3f };
            if (ram)
            {
                socket.rules.Add(Rule("RAM_DDR4_UDIMM_BARE", 0, PairOutcome.Seat, 0, "S1_focused_DDR4_" + name));
                socket.rules.Add(Rule("RAM_DDR4_UDIMM_BARE", 180, PairOutcome.Block, 2.89f, "source_DDR4_reversed"));
                socket.rules.Add(Rule("RAM_DDR5_UDIMM_BARE", 0, PairOutcome.Block, 2.89f, "source_DDR5_in_DDR4"));
            }
            else socket.rules.Add(Rule("GPU_GXB_8PIN", 0, PairOutcome.Seat, 0, "S1_candidate_GPU_" + name));
            var node = Node(board, "MOV_MB_B760_DDR4_" + latchName);
            var latch = node.gameObject.AddComponent<QALatch>();
            latch.socket = socket; latch.animationRoot = node.gameObject;
            latch.startOpen = false; latch.interactables = Array.Empty<MonoBehaviour>();
            // Source FBX coordinates reflect Z. Axial Y rotations therefore reverse sign in imported local coordinates.
            var closed = node.localRotation;
            var open = closed * Quaternion.AngleAxis(ram ? -40 : -25, Vector3.up);
            latch.openClip = Clip(ram ? "DimmOpen" : "PcieOpen", open);
            latch.closeClip = Clip(ram ? "DimmClosed" : "PcieClosed", closed);
            var mesh = node.GetComponent<MeshFilter>().sharedMesh;
            var box = node.gameObject.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = mesh.bounds.size + Vector3.one * .004f;
            var body = node.gameObject.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var pointable = node.gameObject.AddComponent<PointableElement>();
            var hand = Hand(node, body, pointable, GrabTypeFlags.All);
            var input = node.gameObject.AddComponent<ResearchS1TaskAInput>();
            input.owner = task; input.stableId = "A." + name + ".latch"; input.latch = latch; input.pointable = pointable; input.interactable = hand;
            inputs.Add(input); socket.latches.Add(latch);
            return socket;
        }

        private static PairRule Rule(string asset, float turn, PairOutcome outcome, float reach, string evidence) => new PairRule
        { plug = asset, mate = "Edge", turnDeg = turn, outcome = outcome, reachMm = reach, testId = evidence, category = outcome == PairOutcome.Seat ? "compatible" : "mechanically_blocked" };

        private static AnimationClip Clip(string name, Quaternion rotation)
        {
            if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder("Assets/VRMaintenanceResearch/ResearchFlow", "S1Authored");
            string path = Generated + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                if (!LatchClipMatches(existing, rotation)) throw new InvalidOperationException("Existing S1 latch clip differs: " + path);
                return existing;
            }
            var clip = new AnimationClip { name = name, legacy = true };
            string[] axes = { "x", "y", "z", "w" };
            for (int i = 0; i < axes.Length; i++) AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation." + axes[i]), AnimationCurve.Constant(0, 1, rotation[i]));
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        public static bool LatchClipMatches(AnimationClip clip, Quaternion rotation)
        {
            if (!clip || !clip.legacy || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0) return false;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            string[] fields = { "x", "y", "z", "w" };
            if (bindings.Length != fields.Length) return false;
            for (int i = 0; i < fields.Length; i++)
            {
                string property = "m_LocalRotation." + fields[i];
                if (bindings.Count(b => b.path == "" && b.type == typeof(Transform) && b.propertyName == property) != 1) return false;
                var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), property));
                float value = curve == null ? float.NaN : curve.Evaluate(clip.length);
                if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value - rotation[i]) > .0001f) return false;
            }
            return true;
        }

        private static QAPlug Part(ResearchS1TaskAMechanics owner, string id, string family, List<ResearchS1TaskAInput> inputs)
        {
            string asset = ResearchS1TaskACatalog.Asset(id);
            var model = Model(family, asset, owner.transform);
            var part = ConfigurePart(owner, model, asset, id, inputs);
            ReplaceLabel(model, id);
            return part;
        }

        private static QAPlug ConfigurePart(ResearchS1TaskAMechanics owner, GameObject model, string asset, string id, List<ResearchS1TaskAInput> inputs)
        {
            var body = model.AddComponent<Rigidbody>(); body.mass = id.StartsWith("G", StringComparison.Ordinal) ? .7f : .15f;
            body.useGravity = false; body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = model.AddComponent<Grabbable>();
            var plug = model.AddComponent<QAPlug>(); plug.assetId = asset; plug.stableId = id; plug.body = body; plug.grabbable = grab;
            plug.mates = model.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MATE_" + asset + "_", StringComparison.Ordinal)).ToList();
            grab.InjectOptionalRigidbody(body); grab.InjectOptionalKinematicWhileSelected(true); grab.InjectOptionalThrowWhenUnselected(false);
            grab.InjectOptionalOneGrabTransformer(plug); grab.MaxGrabPoints = 1;
            var hand = Hand(model.transform, body, grab, GrabTypeFlags.Palm);
            var item = model.AddComponent<HandsLabObject>(); item.stableId = id; item.body = body; item.grabbable = grab;
            var input = model.AddComponent<ResearchS1TaskAInput>(); input.owner = owner; input.stableId = id; input.plug = plug; input.pointable = grab; input.interactable = hand;
            inputs.Add(input);
            return plug;
        }

        private static HandGrabInteractable Hand(Transform parent, Rigidbody body, IPointableElement pointable, GrabTypeFlags types)
        {
            var hand = new GameObject("HandGrabInteractable").AddComponent<HandGrabInteractable>(); hand.transform.SetParent(parent, false);
            hand.InjectAllHandGrabInteractable(types, body,
                GrabRule(FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Ignored, FingerRequirement.Ignored, FingerRequirement.Ignored),
                GrabRule(FingerRequirement.Optional, FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Required, FingerRequirement.Optional));
            hand.InjectOptionalPointableElement(pointable); return hand;
        }

        private static GrabbingRule GrabRule(params FingerRequirement[] requirements)
        {
            var rule = new GrabbingRule();
            for (int i = 0; i < requirements.Length; i++) rule[(Oculus.Interaction.Input.HandFinger)i] = requirements[i];
            return rule;
        }

        private static GameObject Model(string family, string asset, Transform parent)
        {
            string path = asset == "GPU_GXB_8PIN" ? GpuModel : Models + family + "/" + asset + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new InvalidOperationException("Missing original/imported S1 model " + path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); model.name = asset;
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
            {
                string kind = node.name.Split('_')[0];
                var renderer = node.GetComponent<MeshRenderer>();
                if (kind == "COL")
                {
                    var bounds = node.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var collider = node.gameObject.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
                }
                if (renderer && (kind == "COL" || kind == "SEL" || kind == "INS" || node.name.Contains("_LOD") && !node.name.EndsWith("_LOD0", StringComparison.Ordinal))) renderer.enabled = false;
            }
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            return model;
        }

        private static void ReplaceLabel(GameObject model, string id)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i] || !materials[i].name.StartsWith("M_Label_", StringComparison.Ordinal)) continue;
                    string name = materials[i].name;
                    string path = Generated + "/" + name + "_S1Blank.mat";
                    var blank = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!blank)
                    {
                        blank = new Material(materials[i]) { name = name + "_S1Blank" };
                        blank.mainTexture = null;
                        if (blank.HasProperty("_BaseMap")) blank.SetTexture("_BaseMap", null);
                        blank.color = Color.white;
                        AssetDatabase.CreateAsset(blank, path);
                    }
                    materials[i] = blank; changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            var anchor = model.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("LABEL_", StringComparison.Ordinal));
            var label = new GameObject("S1_" + id + "_label").AddComponent<TextMeshPro>();
            label.transform.SetParent(anchor, false);
            label.transform.localPosition = new Vector3(0, 0, -.0005f);
            label.transform.localRotation = Quaternion.Euler(0, 180, 0);
            label.text = ResearchS1TaskACatalog.Label(id); label.fontSize = .05f; label.color = ResearchUITheme.Ink;
            label.alignment = TextAlignmentOptions.Center; label.rectTransform.sizeDelta = new Vector2(.12f, .03f);
            label.textWrappingMode = TextWrappingModes.Normal;
        }

        private static void PlaceOption(GameObject model, Vector3 localPosition) => model.transform.localPosition = localPosition;
        private static Transform Node(GameObject model, string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        private static void Align(GameObject model, string mateName, Transform seat)
        {
            var mate = Node(model, mateName);
            var pose = SocketRules.RootPose(seat.localToWorldMatrix, 0, 0, model.transform.worldToLocalMatrix * mate.localToWorldMatrix);
            model.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }
    }
}
#endif



