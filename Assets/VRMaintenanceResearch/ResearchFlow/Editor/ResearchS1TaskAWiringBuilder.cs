#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static partial class ResearchS1TaskABuilder
    {
        public static void ConfigureCaseCableRoutes(ResearchS1TaskAWiring wiring)
        {
            foreach(var cable in wiring.cables)
            {
                cable.preferredBow=cable.assetId=="CABLE_AC_C13_JP"?Vector3.zero:new Vector3(1,-1,0);
                cable.restBow=cable.assetId=="CABLE_SATA_DATA_S_500"?Vector3.up:Vector3.zero;
                foreach(var span in cable.spans){span.routeGuides=null;span.cached=false;span.cachedSide=Vector3.zero;}
                if(cable.assetId=="CABLE_MX_MB24")
                    for(int i=0;i<cable.spans.Length;i++)
                    {
                        var span=cable.spans[i];
                        // Owner 7 Nov: the 24-pin runs behind the tray in the channel beside the board edge (no tray past x .262),
                        // then turns out just above its connector, instead of arching across the open side. Fixed cable, visual only.
                        float depth=i==0?.095f:.089f;
                        span.routeGuides=new[]{new Vector3(.32f,.11f,depth),new Vector3(.32f,.35f,depth),new Vector3(.255f,.355f,.02f)};
                        span.routeSlack=Vector3.left;
                    }
                if(cable.assetId=="CABLE_MX_EPS8")
                {
                    cable.spans[0].routeGuides=new[]{new Vector3(.185f,.103f,.035f),new Vector3(0,.103f,.035f),new Vector3(0,.4f,.035f)};
                    cable.spans[0].routeSlack=Vector3.back;
                }
                if(cable.assetId=="CABLE_FANLEAD_4PIN_300")
                {
                    cable.spans[0].routeGuides=new[]{new Vector3(.04f,.37f,-.005f),new Vector3(.04f,.44f,.025f),new Vector3(.14f,.44f,.025f)};
                    cable.spans[0].routeSlack=Vector3.right;
                }
                // Owner 10 Nov: the four front-panel leads each bowed their own way across the drive cage. They now run as one
                // bundle from the front I/O back to the board-edge channel, down it and across to the header. Fixed leads, visual only.
                if(cable.assetId=="CABLE_CASE_FPANEL")
                    foreach(var span in cable.spans){span.routeGuides=FrontPanelRoute;span.routeSlack=Vector3.right;}
                cable.RefreshShape();
            }
        }
        static readonly Vector3[] FrontPanelRoute={new Vector3(.38f,.44f,.03f),new Vector3(.345f,.40f,.07f),new Vector3(.33f,.22f,.072f)};

        // Pivot-local metres. The case spans x -.013..+.432 and z -.11 (open side)..+.11; the mat top is y -.009; the
        // motherboard tray sheet is the plane z .0835 and ends at x .300, leaving the board-edge channel beside it.
        const string StartTrayName="S1_CableTray_Start";
        const float TrayTop=.002f,TrayPlane=.0835f,HeadColumn=-.155f,CornerColumn=-.075f;
        static readonly Vector3[] SataDataPass={new Vector3(.32f,.29f,.07f),new Vector3(.34f,.27f,.118f)},
            SataPowerPass={new Vector3(.27f,.15f,.02f),new Vector3(.31f,.165f,.09f)};
        // Spare daisy-chain connectors S1 never uses. They trailed 0.3 m over the mat beside the one head that can be picked up.
        static readonly string[] SpareHeads={"B_CABLE_MX_SATA3_C","B_CABLE_MX_SATA3_D","B_CABLE_MX_PCIE8X2_C1","B_CABLE_MX_PCIE8X2_C2"};

        // S1_LAYOUT_CABLE_TRAY_20261110 (owner 10 Nov): the three task cables are whole, loose cables. They start folded on one
        // tray beside the rear end of the case. That end is the turning axis, so the tray stays in reach however the case is
        // turned, and it turns with the case. The participant plugs both ends of each cable.
        // Idempotent so authored scenes can migrate.
        public const string CableRoutesPath = "Assets/VRMaintenanceResearch/ResearchFlow/Editor/S1TaskACableRoutes.json";
        sealed class CableRouteData { public string cable, state; public float[] from, to, slack, weights; public float[][] knots; }
        // Authored leads of the three task cables for the plugged states (ResearchS1Cable.routes), read from CableRoutesPath:
        // boxes as centre + size, knots as [anchor, x, y, z], all in cable space. Idempotent.
        public static void RouteTaskCables(ResearchS1TaskAMechanics task, string path = CableRoutesPath)
        {
            var all = Newtonsoft.Json.JsonConvert.DeserializeObject<CableRouteData[]>(System.IO.File.ReadAllText(path));
            Bounds Box(float[] v) => new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
            foreach (var cable in task.wiring.ends.Select(e => e.cable))
            {
                cable.routes = all.Where(r => r.cable == cable.assetId).Select(r => new S1CableRoute { from = Box(r.from), to = Box(r.to),
                    slack = new Vector3(r.slack[0], r.slack[1], r.slack[2]), weights = r.weights,
                    knots = r.knots.Select(k => new S1CableKnot { anchor = (int)k[0], offset = new Vector3(k[1], k[2], k[3]) }).ToArray() }).ToArray();
                foreach (var span in cable.spans) span.cached = false;
                EditorUtility.SetDirty(cable);
            }
        }

        // Owner 10 Nov: the base-kit cables (24-pin, EPS, fan lead, front panel, AC cord) were decoration that covered ports and cut
        // through panels. They are no longer drawn or updated. Their fixed contacts stay as authored, so the recorded base state
        // and the W2 evaluation are unchanged.
        public static void RemoveBaseCables(ResearchS1TaskAMechanics task)
        {
            var kept = task.wiring.ends.Select(e => e.cable).ToArray();
            foreach (var cable in task.wiring.cables.Where(c => !kept.Contains(c)))
            {
                foreach (var renderer in cable.spans.SelectMany(s => new[] { s.from, s.to }).Append(cable.transform).Distinct().SelectMany(t => t.GetComponentsInChildren<Renderer>(true)))
                { renderer.enabled = false; EditorUtility.SetDirty(renderer); }
                cable.enabled = false; EditorUtility.SetDirty(cable);
            }
        }

        // Applies the cable layout migrations to the open scene and saves it. Edit Mode.
        public static string ApplyCableLayout()
        {
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            PlaceCableTrays(task);
            var scene = task.gameObject.scene;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            return "cable layout applied saved=" + saved + " routes=" + string.Join(",", task.wiring.ends.Select(e => e.cable.assetId + ":" + (e.cable.routes?.Length ?? 0)))
                + " hidden=" + task.wiring.cables.Count(c => !c.enabled) + " valid=" + task.AuthoredBindingsValid();
        }

        public static void PlaceCableTrays(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task || !task.wiring || task.inputs == null) throw new ArgumentException("Place Task A cable trays in Edit Mode.");
            DetachCableSources(task);
            // The PSU ports are flush with the PSU body, so a housing that reaches its own mating face ends the last
            // centimetre of seating in contact with the PSU (run09: CONNECT rejected, Contact). It stops 1 mm short of the face.
            foreach (var end in task.wiring.ends.Where(e => e.sourceConnector != S1Connector.MotherboardSata))
            {
                var solid = end.sourcePlug.GetComponentsInChildren<BoxCollider>(true).Single(b => !b.isTrigger);
                var mate = end.sourcePlug.mates.Single();
                float face = mate.InverseTransformPoint(solid.transform.TransformPoint(solid.center)).z;
                var along = solid.transform.InverseTransformDirection(mate.forward); int axis = Mathf.Abs(along.x) > .9f ? 0 : Mathf.Abs(along.y) > .9f ? 1 : 2;
                if (Mathf.Abs(face) - solid.size[axis] / 2 > .0005f) continue;
                var size = solid.size; size[axis] -= .001f; solid.size = size;
                solid.center += along * (Mathf.Sign(face) * .0005f);
            }
            var pivot = task.servicePivot;
            foreach (string firstCut in new[] { "S1_CableTray_Open", "S1_CableTray_Rear" })
                if (pivot.Find(firstCut)) UnityEngine.Object.DestroyImmediate(pivot.Find(firstCut).gameObject);
            Tray(pivot, StartTrayName, new Vector3(-.24f, 0, 0), .39f, .42f);
            var toCase = Quaternion.LookRotation(Vector3.right, Vector3.up); var fromCase = Quaternion.LookRotation(Vector3.left, Vector3.up);
            var forward = Quaternion.LookRotation(Vector3.forward, Vector3.up); var back = Quaternion.LookRotation(Vector3.back, Vector3.up);
            for (int index = 0; index < task.wiring.ends.Length; index++)
            {
                var end = task.wiring.ends[index]; var cable = end.cable;
                bool data = cable.assetId == "CABLE_SATA_DATA_S_500", power = cable.assetId == "CABLE_MX_SATA3";
                // Folded, each cable lies as a hairpin: its two heads side by side at the case end of the tray, cable side away
                // from the case, clear of the corner slots. The PCIe cable lies nearest the open side, where both its ports are.
                // 25 mm between the two leads was too tight a turn for the solver.
                float centre = data ? .125f : power ? 0 : -.13f, half = data || power ? .04f : .05f;
                // With its other end in a port, a head waits in the tray corner on that side, lead toward the case.
                float corner = .125f + .03f * index;
                cable.parks = new[] {
                    Park(task, end.sourcePlug, new[] { new Vector3(HeadColumn, 0, centre - half), new Vector3(CornerColumn, 0, -corner), new Vector3(CornerColumn, 0, corner) },
                        new[] { toCase, fromCase, fromCase }),
                    // The SATA power lead leaves its device plug sideways.
                    Park(task, end.plug, new[] { new Vector3(HeadColumn, 0, centre + half), new Vector3(CornerColumn, 0, -corner), new Vector3(CornerColumn, 0, corner) },
                        power ? new[] { forward, back, back } : new[] { toCase, fromCase, fromCase }) };
                cable.passGuides = data ? SataDataPass : power ? SataPowerPass : null; cable.passPlane = TrayPlane; cable.passSlack = Vector3.up;
                foreach (var park in cable.parks) park.plug = park.head.GetComponent<QAPlug>();
                if (data) cable.parks[1].waitSlot = 2;
                Fold(cable);
                var from = cable.spans[0].from; var to = cable.spans[0].to;
                cable.fold = new S1CableFold { toPosition = from.InverseTransformPoint(to.position), toRotation = Quaternion.Inverse(from.rotation) * to.rotation,
                    bonePositions = cable.spans[0].bones.Select(b => from.InverseTransformPoint(b.position)).ToArray(),
                    boneRotations = cable.spans[0].bones.Select(b => Quaternion.Inverse(from.rotation) * b.rotation).ToArray() };
                EditorUtility.SetDirty(cable);
            }
            foreach (var cable in task.wiring.cables)
            {
                foreach (var span in cable.spans.Where(s => SpareHeads.Contains(s.to.name)))
                    foreach (var bone in span.bones.Append(span.to))
                    {
                        // Folded into the head they hang from, so no sliver of mesh is left stretched to them.
                        bone.SetParent(task.wiring.ends.Single(e => e.cable == cable).plug.transform, false);
                        bone.localPosition = Vector3.zero; bone.localScale = Vector3.zero;
                    }
                cable.spans = cable.spans.Where(s => !SpareHeads.Contains(s.to.name)).ToArray();
            }
            // The unplugged AC cord lies behind the case (owner 2 Nov), now past its rear end and further back, clear of the
            // cable tray and the service handle.
            task.wiring.cables.Single(c => c.assetId == "CABLE_AC_C13_JP").transform.localPosition = new Vector3(-.62f, .01f, .40f);
            ConfigureCaseCableRoutes(task.wiring);
            RemoveBaseCables(task);
            RouteTaskCables(task);
            EditorUtility.SetDirty(task.gameObject); EditorUtility.SetDirty(task.wiring);
        }

        // The board/PSU end of each task cable stops being a fixed contact: it becomes a plug like the device end, and every
        // free port of its family on the board or PSU takes it. The SATA data cable has two identical ends, so either fits either side.
        private static void DetachCableSources(ResearchS1TaskAMechanics task)
        {
            var wiring = task.wiring; var inputs = task.inputs.ToList();
            foreach (var end in wiring.ends.Where(e => !e.sourcePlug))
            {
                var contact = wiring.contacts[end.sourceContact]; var head = contact.mate.parent; string asset = end.cable.assetId;
                head.SetParent(end.plug.transform.parent, true);
                bool board = end.sourceConnector == S1Connector.MotherboardSata, sata = end.sourceConnector == S1Connector.PsuSata;
                end.sourcePlug = Plug(task, end.cable, head.name.Substring(("B_" + asset + "_").Length), end.plug.stableId + (board ? "_board" : "_psu"), inputs);
                var equipment = task.fixedAssembly[board ? 1 : 4].gameObject;
                var seats = board ? new[] { "SEAT_MB_B760_DDR4_SATA6G_1", "SEAT_MB_B760_DDR4_SATA6G_2", "SEAT_MB_B760_DDR4_SATA6G_3", "SEAT_MB_B760_DDR4_SATA6G_4" }
                    : sata ? new[] { "SEAT_PSU_ATX_MX650_PERIPH1", "SEAT_PSU_ATX_MX650_PERIPH2", "SEAT_PSU_ATX_MX650_PERIPH3" }
                    : new[] { "SEAT_PSU_ATX_MX650_CPUPCIE2", "SEAT_PSU_ATX_MX650_CPUPCIE3" };
                var mates = board ? new[] { "EndB", "EndA" } : new[] { "EndA" };
                end.sourceSockets = seats.Select(name =>
                {
                    var seat = Node(equipment, name);
                    var socket = seat.gameObject.AddComponent<QASocket>(); socket.fixtureId = end.sourcePartId; socket.seat = seat;
                    socket.seatName = name.Substring(name.LastIndexOf(board ? "SATA6G" : sata ? "PERIPH" : "CPUPCIE", StringComparison.Ordinal));
                    socket.spec = new SeatSpec { approachMm = board || sata ? 20 : 25, posTolMm = 3, angTolDeg = board || sata ? 10 : 6, hysteresisMm = 1, hysteresisDeg = 3, seatedDepthTolMm = .3f };
                    foreach (string mate in mates)
                    {
                        socket.rules.Add(new PairRule { plug = asset, mate = mate, outcome = PairOutcome.Seat, turnDeg = 0, testId = "S1_source_" + asset + "_" + socket.seatName });
                        if (board) socket.rules.Add(new PairRule { plug = asset, mate = mate, outcome = PairOutcome.Block, turnDeg = 180, reachMm = 4.79f, testId = "source_SATA_reversed" });
                    }
                    return socket;
                }).ToArray();
                if (board)
                    foreach (var target in wiring.targets.Where(t => t.connector == S1Connector.SsdData))
                        foreach (var rule in target.socket.rules.Where(r => r.plug == asset && r.mate == "EndA").ToArray())
                            target.socket.rules.Add(new PairRule { plug = asset, mate = "EndB", outcome = rule.outcome, turnDeg = rule.turnDeg, reachMm = rule.reachMm, testId = rule.testId });
            }
            wiring.contacts = wiring.contacts.Where(c => !c.circuit.EndsWith("_source", StringComparison.Ordinal)).ToArray();
            foreach (var end in wiring.ends) end.sourceContact = -1;
            task.inputs = inputs.ToArray();
        }

        // The solid stops 5 mm above the mat: resting on it, the tray's collider would block every turn of the case.
        private static void Tray(Transform pivot, string name, Vector3 centre, float width, float depth)
        {
            var existing = pivot.Find(name);
            if (existing) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var tray = new GameObject(name).transform;
            tray.SetParent(pivot, false); tray.localPosition = new Vector3(centre.x, 0, centre.z);
            var material = GameObject.Find("Computer_ComponentTray").GetComponentInChildren<Renderer>().sharedMaterial;
            void Slab(string part, Vector3 position, Vector3 size, bool solid)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube); slab.name = part;
                slab.transform.SetParent(tray, false); slab.transform.localPosition = position; slab.transform.localScale = size;
                slab.GetComponent<Renderer>().sharedMaterial = material;
                if (!solid) UnityEngine.Object.DestroyImmediate(slab.GetComponent<Collider>());
            }
            const float mat = -.009f, rim = .006f, lip = .012f;
            Slab("Base", new Vector3(0, TrayTop - .002f, 0), new Vector3(width, .004f, depth), true);
            Slab("Skirt", new Vector3(0, (mat + TrayTop - .004f) / 2, 0), new Vector3(width, TrayTop - .004f - mat, depth), false);
            foreach (float side in new[] { -1f, 1f })
            {
                Slab("RimX", new Vector3(side * (width + rim) / 2, (mat + TrayTop + lip) / 2, 0), new Vector3(rim, TrayTop + lip - mat, depth + 2 * rim), false);
                Slab("RimZ", new Vector3(0, (mat + TrayTop + lip) / 2, side * (depth + rim) / 2), new Vector3(width, TrayTop + lip - mat, rim), false);
            }
        }

        // Returns the head's slots in cable space and leaves it lying in the first; mate frames are given in pivot space.
        private static S1CablePark Park(ResearchS1TaskAMechanics task, QAPlug plug, Vector3[] positions, Quaternion[] rotations)
        {
            var head = plug.transform; var mate = plug.mates.Single(); var pivot = task.servicePivot; var cable = head.GetComponent<ResearchS1TaskAPlugMotion>().cable;
            var slots = new S1CableSlot[positions.Length];
            for (int i = positions.Length - 1; i >= 0; i--)
            {
                var pose = SocketRules.RootPose(pivot.localToWorldMatrix * Matrix4x4.TRS(positions[i], rotations[i], Vector3.one), 0, 0, head.worldToLocalMatrix * mate.localToWorldMatrix);
                head.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
                Physics.SyncTransforms();
                float lowest = head.GetComponentsInChildren<BoxCollider>(true).Where(b => !b.isTrigger).Min(b => b.bounds.min.y);
                head.position += Vector3.up * (pivot.TransformPoint(new Vector3(0, TrayTop + .001f, 0)).y - lowest);
                slots[i] = new S1CableSlot { position = cable.transform.InverseTransformPoint(head.position), rotation = Quaternion.Inverse(cable.transform.rotation) * head.rotation };
            }
            return new S1CablePark { head = head, slots = slots };
        }

        // Finds the hairpin the folded cable lies in on the start tray: two straight legs away from the case (cable -x) and a
        // half-turn, as deep as its length needs.
        private static void Fold(ResearchS1Cable cable)
        {
            var span = cable.spans[0]; var toCable = cable.transform.worldToLocalMatrix;
            cable.fold = null;
            Vector3 a = toCable.MultiplyPoint3x4(span.from.TransformPoint(span.fromExit)), b = toCable.MultiplyPoint3x4(span.to.TransformPoint(span.toExit));
            float centre = (a.z + b.z) / 2, radius = Mathf.Abs(b.z - a.z) / 2, side = Mathf.Sign(b.z - a.z), start = Mathf.Min(a.x, b.x) - .02f;
            for (float depth = .02f; depth < .3f; depth += .002f)
                foreach (var slack in new[] { Vector3.forward, Vector3.back, Vector3.up })
                {
                    // Each leg lies at its own head's height: a 3 mm step in the last 2 cm bent the lead tighter than 8 mm.
                    var guides = new List<Vector3> { new Vector3(start, a.y, a.z), new Vector3(start - depth / 2, a.y, a.z), new Vector3(start - depth, a.y, a.z) };
                    foreach (float angle in new[] { 45f, 90f, 135f })
                        guides.Add(new Vector3(start - depth - radius * Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Lerp(a.y, b.y, angle / 180), centre - side * radius * Mathf.Cos(angle * Mathf.Deg2Rad)));
                    guides.AddRange(new[] { new Vector3(start - depth, b.y, b.z), new Vector3(start - depth / 2, b.y, b.z), new Vector3(start, b.y, b.z) });
                    cable.parkGuides = guides.ToArray(); cable.parkSlack = slack;
                    foreach (var s2 in cable.spans) { s2.cached = false; s2.cachedSide = Vector3.zero; }
                    cable.RefreshShape();
                    if (span.cachedGuided) return;
                }
            throw new InvalidOperationException("No hairpin holds " + cable.assetId + " at its length.");
        }

        private static ResearchS1TaskAWiring Wiring(ResearchS1TaskAMechanics task, List<ResearchS1TaskAInput> inputs)
        {
            var wiring = task.gameObject.AddComponent<ResearchS1TaskAWiring>(); wiring.owner = task;
            var contacts = new List<S1FixedCableContact>();
            var board = task.fixedAssembly[1].gameObject; var psu = task.fixedAssembly[4].gameObject;
            var shell = task.fixedAssembly[0].gameObject; var cooler = task.fixedAssembly[3].gameObject;
            var atx = Cable(task, "PC_Power", "CABLE_MX_MB24");
            Fixed(contacts, atx, "A1", psu, "SEAT_PSU_ATX_MX650_MB18", Vector3.zero, "atx24");
            Fixed(contacts, atx, "A2", psu, "SEAT_PSU_ATX_MX650_MB10", Vector3.zero, "atx24");
            Fixed(contacts, atx, "B", board, "SEAT_MB_B760_DDR4_ATX24", Vector3.zero, "atx24");
            var eps = Cable(task, "PC_Power", "CABLE_MX_EPS8");
            Fixed(contacts, eps, "A", psu, "SEAT_PSU_ATX_MX650_CPUPCIE1", Vector3.zero, "eps8");
            Fixed(contacts, eps, "B1", board, "SEAT_MB_B760_DDR4_EPS8", new Vector3(-.0042f,0,0), "eps8");
            Fixed(contacts, eps, "B2", board, "SEAT_MB_B760_DDR4_EPS8", new Vector3(.0042f,0,0), "eps8");
            var fan = Cable(task, "PC_Cables", "CABLE_FANLEAD_4PIN_300");
            Fixed(contacts, fan, "A", cooler, "CABLE_COOLER_LGA1700_STOCK_Lead", Vector3.zero, "cpu_fan");
            Fixed(contacts, fan, "B", board, "SEAT_MB_B760_DDR4_CPU_FAN", Vector3.zero, "cpu_fan");
            var front = Cable(task, "PC_Case", "CABLE_CASE_FPANEL");
            Fixed(contacts, front, "A", shell, "CABLE_CASE_ATX_MID_FrontIO_FPanel", Vector3.zero, "front_panel", true);
            Fixed(contacts, front, "HDD_LED", board, "SEAT_MB_B760_DDR4_F_PANEL", new Vector3(-.00381f,-.00127f,0), "front_panel");
            Fixed(contacts, front, "RESET_SW", board, "SEAT_MB_B760_DDR4_F_PANEL", new Vector3(.00127f,-.00127f,0), "front_panel");
            Fixed(contacts, front, "PWR_LED", board, "SEAT_MB_B760_DDR4_F_PANEL", new Vector3(-.00381f,.00127f,0), "front_panel");
            Fixed(contacts, front, "PWR_SW", board, "SEAT_MB_B760_DDR4_F_PANEL", new Vector3(.00127f,.00127f,0), "front_panel");
            var data = Cable(task, "PC_Cables", "CABLE_SATA_DATA_S_500");
            var sata = Cable(task, "PC_Power", "CABLE_MX_SATA3");
            var pcie = Cable(task, "PC_Power", "CABLE_MX_PCIE8X2");
            var dataSource = Fixed(contacts, data, "B", board, "SEAT_MB_B760_DDR4_SATA6G_1", Vector3.zero, "data_source");
            var sataSource = Fixed(contacts, sata, "A", psu, "SEAT_PSU_ATX_MX650_PERIPH1", Vector3.zero, "sata_source");
            var pcieSource = Fixed(contacts, pcie, "A", psu, "SEAT_PSU_ATX_MX650_CPUPCIE2", Vector3.zero, "pcie_source");
            // S1 uses one assembled 6+2 head; spare daisy heads remain attached, not alternate task choices.
            foreach (string head in new[] { "B2", "C1", "C2" }) Head(pcie, head).SetParent(Head(pcie, "B1"), true);
            foreach (string head in new[] { "C", "D" }) Head(sata, head).SetParent(Head(sata, "B"), true);
            wiring.ends = new[] {
                End(task, data, "A", "A.sata_data", dataSource, contacts.IndexOf(dataSource), "A.motherboard", S1Connector.MotherboardSata, inputs),
                End(task, sata, "B", "A.sata_power", sataSource, contacts.IndexOf(sataSource), "A.psu", S1Connector.PsuSata, inputs),
                End(task, pcie, "B1", "A.pcie_power", pcieSource, contacts.IndexOf(pcieSource), "A.psu", S1Connector.PsuPcie8, inputs) };
            var targets = new List<S1CableTarget>();
            foreach (var part in task.ssds)
            {
                targets.Add(Target(part, "SEAT_SSD_SATA_25_7MM_SataData", S1Connector.SsdData, Vector3.zero, "CABLE_SATA_DATA_S_500", "EndA", 20,10));
                targets.Add(Target(part, "SEAT_SSD_SATA_25_7MM_SataPower", S1Connector.SsdPower, Vector3.zero, "CABLE_MX_SATA3", "EndB", 20,10));
            }
            foreach (var part in task.gpus)
                targets.Add(Target(part, "SEAT_GPU_GXB_8PIN_PCIE8", S1Connector.GpuPcie8, new Vector3(-.0021f,0,0), "CABLE_MX_PCIE8X2", "EndB1", 25,6));
            wiring.targets = targets.ToArray(); wiring.contacts = contacts.ToArray();
            var latchNode = Node(pcie.gameObject, "MOV_CABLE_MX_PCIE8X2_LatchB1");
            var latch = latchNode.gameObject.AddComponent<QALatch>(); latch.animationRoot = latchNode.gameObject;
            latch.interactables = Array.Empty<MonoBehaviour>(); latch.startOpen = true;
            latch.closeClip = Clip("PcieCableClosed", latchNode.localRotation);
            latch.openClip = Clip("PcieCablePinched", latchNode.localRotation * Quaternion.AngleAxis(-8, Vector3.right));
            var latchMotion = wiring.ends[2].plug.gameObject.AddComponent<ResearchS1CableLatch>();
            latchMotion.plug = wiring.ends[2].plug; latchMotion.input = wiring.ends[2].plug.GetComponent<ResearchS1TaskAInput>(); latchMotion.latch = latch;
            latchMotion.sockets = wiring.targets.Where(t => t.connector == S1Connector.GpuPcie8).Select(t => t.socket).ToArray();
            latch.socket = latchMotion.sockets[0];
            foreach (var socket in latchMotion.sockets) socket.latches.Add(latch);
            // Its rest place behind the case is set with the cable trays (PlaceCableTrays).
            var ac = Cable(task, "PC_Power", "CABLE_AC_C13_JP");
            wiring.acSeat = Node(psu, "SEAT_PSU_ATX_MX650_AC"); wiring.acMate = Mate(ac,"A");
            wiring.acSocket = wiring.acSeat.gameObject.AddComponent<QASocket>(); wiring.acSocket.fixtureId = "A.psu"; wiring.acSocket.seatName = "AC"; wiring.acSocket.seat = wiring.acSeat;
            wiring.cables = new[] { atx, eps, fan, front, data, sata, pcie, ac };
            ConfigureCaseCableRoutes(wiring);
            return wiring;
        }

        private static S1CableTarget Target(QAPlug part, string seatName, S1Connector connector, Vector3 offset, string asset, string mate, float approach, float angle)
        {
            var original = Node(part.gameObject, seatName);
            var seat = original;
            if (offset != Vector3.zero)
            {
                seat = new GameObject("S1_" + connector + "_6pin_offset").transform;
                seat.SetParent(original, false); seat.localPosition = offset;
            }
            var socket = seat.gameObject.AddComponent<QASocket>(); socket.fixtureId = part.stableId; socket.seatName = connector.ToString(); socket.seat = seat;
            socket.spec = new SeatSpec { approachMm = approach, posTolMm = 3, angTolDeg = angle, hysteresisMm = 1, hysteresisDeg = 3, seatedDepthTolMm = .3f };
            socket.rules.Add(new PairRule { plug = asset, mate = mate, outcome = PairOutcome.Seat, turnDeg = 0, testId = "S1_source_" + asset + "_" + connector });
            if (connector == S1Connector.SsdData || connector == S1Connector.SsdPower)
            {
                socket.rules.Add(new PairRule { plug = asset, mate = mate, outcome = PairOutcome.Block, turnDeg = 180, reachMm = 4.79f, testId = "source_SATA_reversed" });
                bool data = connector == S1Connector.SsdData;
                socket.rules.Add(new PairRule { plug = data ? "CABLE_MX_SATA3" : "CABLE_SATA_DATA_S_500", mate = data ? "EndB" : "EndA",
                    outcome = PairOutcome.Block, turnDeg = 0, reachMm = data ? 4.798f : 4.79f, testId = "source_SATA_wrong_family" });
            }
            return new S1CableTarget { part = part, socket = socket, connector = connector };
        }

        private static S1TaskACableEnd End(ResearchS1TaskAMechanics task, ResearchS1Cable cable, string headName, string id,
            S1FixedCableContact source, int sourceIndex, string sourcePart, S1Connector connector, List<ResearchS1TaskAInput> inputs)
        {
            var plug = Plug(task, cable, headName, id, inputs);
            Rest(plug.transform, plug.mates.Single(), source.seat, id);
            return new S1TaskACableEnd { cable = cable, plug = plug, sourceContact = sourceIndex, sourcePartId = sourcePart, sourceConnector = connector };
        }

        private static QAPlug Plug(ResearchS1TaskAMechanics task, ResearchS1Cable cable, string headName, string id, List<ResearchS1TaskAInput> inputs)
        {
            var head = Head(cable, headName); var mate = Mate(cable, headName);
            var body = head.gameObject.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true; body.mass = .02f;
            var grab = head.gameObject.AddComponent<Grabbable>();
            var plug = head.gameObject.AddComponent<QAPlug>(); plug.assetId = cable.assetId; plug.stableId = id; plug.body = body; plug.grabbable = grab; plug.mates = new List<Transform> { mate };
            foreach (var collider in head.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            var selection = head.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("SEL_" + cable.assetId + "_", StringComparison.Ordinal));
            var box = selection.gameObject.AddComponent<BoxCollider>(); var bounds = selection.GetComponent<MeshFilter>().sharedMesh.bounds; box.center = bounds.center; box.size = bounds.size;
            HeadSolid(cable, head, box);
            grab.InjectOptionalRigidbody(body); grab.InjectOptionalKinematicWhileSelected(true); grab.InjectOptionalThrowWhenUnselected(false); grab.MaxGrabPoints = 1;
            var hand = Hand(head, body, grab, GrabTypeFlags.Pinch);
            var input = head.gameObject.AddComponent<ResearchS1TaskAInput>(); input.owner = task; input.stableId = id; input.plug = plug; input.pointable = grab; input.interactable = hand; inputs.Add(input);
            var motion = head.gameObject.AddComponent<ResearchS1TaskAPlugMotion>(); motion.owner = task; motion.plug = plug; motion.input = input; motion.cable = cable; grab.InjectOptionalOneGrabTransformer(motion);
            var item = head.gameObject.AddComponent<HandsLabObject>(); item.stableId = id; item.body = body; item.grabbable = grab;
            return plug;
        }

        private static S1FixedCableContact Fixed(List<S1FixedCableContact> contacts, ResearchS1Cable cable, string headName, GameObject equipment,
            string seatName, Vector3 offset, string circuit, bool useHead = false)
        {
            var head = Head(cable, headName); var mate = useHead ? head : Mate(cable, headName); var seat = Node(equipment, seatName);
            var pose = seat.localToWorldMatrix * Matrix4x4.Translate(offset) * (head.worldToLocalMatrix * mate.localToWorldMatrix).inverse;
            head.SetPositionAndRotation(pose.GetColumn(3), pose.rotation); head.SetParent(equipment.transform, true);
            var contact = new S1FixedCableContact { circuit = circuit, cable = cable, assetId = cable.assetId, mate = mate, mateName = mate.name,
                seat = seat, seatName = seatName, equipment = equipment.transform, offset = offset };
            contacts.Add(contact); return contact;
        }
        // SEL is the source's padded grab zone (+10 mm per axis on SATA data), so it only stays as the grab trigger. The solid is the
        // authored COL head box; heads without one get the same convention from their geometry: housing only, on the cable side of the
        // mating face (the side the source GRAB anchor sits on), since the part past the face enters the target's header shroud.
        // Rebuilt on every call so scenes can migrate.
        private static void HeadSolid(ResearchS1Cable cable, Transform head, BoxCollider selection)
        {
            selection.isTrigger = true;
            foreach (var previous in head.GetComponentsInChildren<BoxCollider>(true).Where(b => !b.isTrigger)) UnityEngine.Object.DestroyImmediate(previous);
            var col = head.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("COL_" + cable.assetId + "_", StringComparison.Ordinal));
            if (col)
            {
                var authored = col.gameObject.AddComponent<BoxCollider>(); var colBounds = col.GetComponent<MeshFilter>().sharedMesh.bounds;
                authored.center = colBounds.center; authored.size = colBounds.size;
                return;
            }
            var mate = head.GetComponentsInChildren<Transform>(true).Single(t => t.name.StartsWith("MATE_" + cable.assetId + "_", StringComparison.Ordinal) && t.parent == head);
            var grab = head.GetComponentsInChildren<Transform>(true).Single(t => t.name.StartsWith("GRAB_" + cable.assetId + "_", StringComparison.Ordinal) && t.parent == head);
            float side = Mathf.Sign(mate.InverseTransformPoint(grab.position).z);
            Bounds bounds;
            if (cable.assetId == "CABLE_MX_SATA3")
                // conn.sata_plug_outer("POWER") is 22.65 x 5.9 mm with plug_len 14 mm; skinned bounds would also hold the angled exit and
                // both daisy-chain wire runs. SATA_GAP leaves only 0.1 mm between housings, so the width sits 0.5 mm inside per side to
                // keep hand-scale clearance beside the data plug.
                bounds = new Bounds(new Vector3(0, -.0009f, side * .007f), new Vector3(.02165f, .0059f, .014f));
            else
            {
                var mesh = cable.skin.sharedMesh; int bone = Array.IndexOf(cable.skin.bones, head);
                var weights = mesh.boneWeights; var toMate = mate.worldToLocalMatrix * head.localToWorldMatrix * mesh.bindposes[bone];
                var outside = mesh.vertices.Where((v, i) => weights[i].boneIndex0 == bone && weights[i].weight0 >= .5f)
                    .Select(v => toMate.MultiplyPoint3x4(v)).Where(v => v.z * side >= 0).ToArray();
                if (outside.Length == 0) throw new InvalidOperationException("No skinned housing vertices for " + head.name);
                bounds = new Bounds(outside[0], Vector3.zero);
                foreach (var v in outside) bounds.Encapsulate(v);
            }
            var solid = mate.gameObject.AddComponent<BoxCollider>(); solid.center = bounds.center; solid.size = bounds.size;
        }

        // Loose ends rest inside the case near their source, clear of every collider and with a solvable cable span. The former shared
        // 0.2 m offset left the motherboard SATA plug floating outside the bezel and pushed both PSU plugs into the case floor.
        private static void Rest(Transform head, Transform mate, Transform sourceSeat, string id)
        {
            var offset = id == "A.sata_data" ? new Vector3(.1f, .08f, -.12f) : new Vector3(.04f, -.1f, -.15f);
            var pose = SocketRules.RootPose(sourceSeat.localToWorldMatrix * Matrix4x4.Translate(offset), 0, 0, head.worldToLocalMatrix * mate.localToWorldMatrix);
            head.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }

        public static void CorrectCablePlugProxies(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task || !task.wiring) throw new ArgumentException("Correct Task A cable proxies in Edit Mode.");
            foreach (var end in task.wiring.ends)
                HeadSolid(end.cable, end.plug.transform, end.plug.GetComponentsInChildren<BoxCollider>(true).Single(c => c.name.StartsWith("SEL_", StringComparison.Ordinal)));
            PlaceCableTrays(task);
        }

        private static Transform Head(ResearchS1Cable cable, string id) => cable.skin.bones.Single(b => b.name == "B_" + cable.assetId + "_" + id);
        private static Transform Mate(ResearchS1Cable cable, string id) => Head(cable, id).GetComponentsInChildren<Transform>(true).Single(t => t.name == "MATE_" + cable.assetId + "_End" + id);

        private static ResearchS1Cable Cable(ResearchS1TaskAMechanics task, string family, string asset)
        {
            var model = Model(family, asset, task.servicePivot);
            // Cable heads must leave their imported rig to follow the equipment anchors.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var cable = model.AddComponent<ResearchS1Cable>(); cable.assetId = asset;
            cable.skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "VIS_" + asset + "_LOD0");
            cable.skin.updateWhenOffscreen = true;
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.enabled = renderer == cable.skin;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            switch (asset)
            {
                case "CABLE_MX_MB24":
                    cable.spans = new[] {
                        Span(cable, "A1", "B", 0.6f, new Vector3(-0.01305f,0.0f,-0.011f), new[] {"B_CABLE_MX_MB24_JA100","B_CABLE_MX_MB24_JA101","B_CABLE_MX_MB24_JA102","B_CABLE_MX_MB24_JA103","B_CABLE_MX_MB24_JA104","B_CABLE_MX_MB24_JA105","B_CABLE_MX_MB24_JA106","B_CABLE_MX_MB24_JA107","B_CABLE_MX_MB24_JA108","B_CABLE_MX_MB24_JA109","B_CABLE_MX_MB24_JA110","B_CABLE_MX_MB24_JA111","B_CABLE_MX_MB24_JA112"}),
                        Span(cable, "A2", "B", 0.6f, new Vector3(0.01305f,0.0f,-0.011f), new[] {"B_CABLE_MX_MB24_JA200","B_CABLE_MX_MB24_JA201","B_CABLE_MX_MB24_JA202","B_CABLE_MX_MB24_JA203","B_CABLE_MX_MB24_JA204","B_CABLE_MX_MB24_JA205","B_CABLE_MX_MB24_JA206","B_CABLE_MX_MB24_JA207","B_CABLE_MX_MB24_JA208","B_CABLE_MX_MB24_JA209","B_CABLE_MX_MB24_JA210","B_CABLE_MX_MB24_JA211","B_CABLE_MX_MB24_JA212"}),
                    }; break;
                case "CABLE_MX_EPS8":
                    cable.spans = new[] {
                        Span(cable, "A", "B1", 0.65f, new Vector3(-0.001f,-0.003f,-0.011f), new[] {"B_CABLE_MX_EPS8_J100","B_CABLE_MX_EPS8_J101","B_CABLE_MX_EPS8_J102","B_CABLE_MX_EPS8_J103","B_CABLE_MX_EPS8_J104","B_CABLE_MX_EPS8_J105","B_CABLE_MX_EPS8_J106","B_CABLE_MX_EPS8_J107","B_CABLE_MX_EPS8_J108","B_CABLE_MX_EPS8_J109","B_CABLE_MX_EPS8_J110","B_CABLE_MX_EPS8_J111","B_CABLE_MX_EPS8_J112","B_CABLE_MX_EPS8_J113"}),
                        Span(cable, "B1", "B2", 0.0517f, new Vector3(0.0f,0.0026f,-0.011f), new[] {"B_CABLE_MX_EPS8_PB200","B_CABLE_MX_EPS8_PB201"}),
                    }; break;
                case "CABLE_FANLEAD_4PIN_300":
                    cable.spans = new[] {
                        Span(cable, "A", "B", 0.3f, new Vector3(0.0f,0.0025f,-0.0065f), new[] {"B_CABLE_FANLEAD_4PIN_300_J00","B_CABLE_FANLEAD_4PIN_300_J01","B_CABLE_FANLEAD_4PIN_300_J02","B_CABLE_FANLEAD_4PIN_300_J03","B_CABLE_FANLEAD_4PIN_300_J04","B_CABLE_FANLEAD_4PIN_300_J05","B_CABLE_FANLEAD_4PIN_300_J06"}),
                    }; break;
                case "CABLE_CASE_FPANEL":
                    cable.spans = new[] {
                        Span(cable, "A", "HDD_LED", 0.55f, new Vector3(0.0f,0.0f,-0.012f), new[] {"B_CABLE_CASE_FPANEL_JHDD_LED00","B_CABLE_CASE_FPANEL_JHDD_LED01","B_CABLE_CASE_FPANEL_JHDD_LED02","B_CABLE_CASE_FPANEL_JHDD_LED03","B_CABLE_CASE_FPANEL_JHDD_LED04","B_CABLE_CASE_FPANEL_JHDD_LED05","B_CABLE_CASE_FPANEL_JHDD_LED06","B_CABLE_CASE_FPANEL_JHDD_LED07","B_CABLE_CASE_FPANEL_JHDD_LED08","B_CABLE_CASE_FPANEL_JHDD_LED09","B_CABLE_CASE_FPANEL_JHDD_LED10","B_CABLE_CASE_FPANEL_JHDD_LED11"}),
                        Span(cable, "A", "RESET_SW", 0.55f, new Vector3(0.0f,0.0f,-0.012f), new[] {"B_CABLE_CASE_FPANEL_JRESET_SW00","B_CABLE_CASE_FPANEL_JRESET_SW01","B_CABLE_CASE_FPANEL_JRESET_SW02","B_CABLE_CASE_FPANEL_JRESET_SW03","B_CABLE_CASE_FPANEL_JRESET_SW04","B_CABLE_CASE_FPANEL_JRESET_SW05","B_CABLE_CASE_FPANEL_JRESET_SW06","B_CABLE_CASE_FPANEL_JRESET_SW07","B_CABLE_CASE_FPANEL_JRESET_SW08","B_CABLE_CASE_FPANEL_JRESET_SW09","B_CABLE_CASE_FPANEL_JRESET_SW10","B_CABLE_CASE_FPANEL_JRESET_SW11"}),
                        Span(cable, "A", "PWR_LED", 0.55f, new Vector3(0.0f,0.0f,-0.012f), new[] {"B_CABLE_CASE_FPANEL_JPWR_LED00","B_CABLE_CASE_FPANEL_JPWR_LED01","B_CABLE_CASE_FPANEL_JPWR_LED02","B_CABLE_CASE_FPANEL_JPWR_LED03","B_CABLE_CASE_FPANEL_JPWR_LED04","B_CABLE_CASE_FPANEL_JPWR_LED05","B_CABLE_CASE_FPANEL_JPWR_LED06","B_CABLE_CASE_FPANEL_JPWR_LED07","B_CABLE_CASE_FPANEL_JPWR_LED08","B_CABLE_CASE_FPANEL_JPWR_LED09","B_CABLE_CASE_FPANEL_JPWR_LED10","B_CABLE_CASE_FPANEL_JPWR_LED11"}),
                        Span(cable, "A", "PWR_SW", 0.55f, new Vector3(0.0f,0.0f,-0.012f), new[] {"B_CABLE_CASE_FPANEL_JPWR_SW00","B_CABLE_CASE_FPANEL_JPWR_SW01","B_CABLE_CASE_FPANEL_JPWR_SW02","B_CABLE_CASE_FPANEL_JPWR_SW03","B_CABLE_CASE_FPANEL_JPWR_SW04","B_CABLE_CASE_FPANEL_JPWR_SW05","B_CABLE_CASE_FPANEL_JPWR_SW06","B_CABLE_CASE_FPANEL_JPWR_SW07","B_CABLE_CASE_FPANEL_JPWR_SW08","B_CABLE_CASE_FPANEL_JPWR_SW09","B_CABLE_CASE_FPANEL_JPWR_SW10","B_CABLE_CASE_FPANEL_JPWR_SW11"}),
                    }; break;
                case "CABLE_SATA_DATA_S_500":
                    cable.spans = new[] {
                        Span(cable, "A", "B", 0.5f, new Vector3(0.0f,-0.0009f,-0.013f), new[] {"B_CABLE_SATA_DATA_S_500_J00","B_CABLE_SATA_DATA_S_500_J01","B_CABLE_SATA_DATA_S_500_J02","B_CABLE_SATA_DATA_S_500_J03","B_CABLE_SATA_DATA_S_500_J04","B_CABLE_SATA_DATA_S_500_J05","B_CABLE_SATA_DATA_S_500_J06","B_CABLE_SATA_DATA_S_500_J07","B_CABLE_SATA_DATA_S_500_J08","B_CABLE_SATA_DATA_S_500_J09","B_CABLE_SATA_DATA_S_500_J10"}),
                    }; break;
                case "CABLE_MX_SATA3":
                    cable.spans = new[] {
                        Span(cable, "A", "B", 0.45f, new Vector3(-0.011325f,-0.0009f,-0.0096f), new[] {"B_CABLE_MX_SATA3_J100","B_CABLE_MX_SATA3_J101","B_CABLE_MX_SATA3_J102","B_CABLE_MX_SATA3_J103","B_CABLE_MX_SATA3_J104","B_CABLE_MX_SATA3_J105","B_CABLE_MX_SATA3_J106","B_CABLE_MX_SATA3_J107","B_CABLE_MX_SATA3_J108","B_CABLE_MX_SATA3_J109"}),
                        Span(cable, "B", "C", 0.15f, new Vector3(-0.011325f,-0.0009f,-0.0096f), new[] {"B_CABLE_MX_SATA3_JC00","B_CABLE_MX_SATA3_JC01","B_CABLE_MX_SATA3_JC02"}),
                        Span(cable, "C", "D", 0.15f, new Vector3(-0.011325f,-0.0009f,-0.0096f), new[] {"B_CABLE_MX_SATA3_JD00","B_CABLE_MX_SATA3_JD01","B_CABLE_MX_SATA3_JD02"}),
                    }; break;
                case "CABLE_MX_PCIE8X2":
                    cable.spans = new[] {
                        Span(cable, "A", "B1", 0.6f, new Vector3(-0.0031f,-0.003f,-0.011f), new[] {"B_CABLE_MX_PCIE8X2_J100","B_CABLE_MX_PCIE8X2_J101","B_CABLE_MX_PCIE8X2_J102","B_CABLE_MX_PCIE8X2_J103","B_CABLE_MX_PCIE8X2_J104","B_CABLE_MX_PCIE8X2_J105","B_CABLE_MX_PCIE8X2_J106","B_CABLE_MX_PCIE8X2_J107","B_CABLE_MX_PCIE8X2_J108","B_CABLE_MX_PCIE8X2_J109","B_CABLE_MX_PCIE8X2_J110","B_CABLE_MX_PCIE8X2_J111","B_CABLE_MX_PCIE8X2_J112"}),
                        Span(cable, "B1", "B2", 0.0494f, new Vector3(0.0f,0.0026f,-0.011f), new[] {"B_CABLE_MX_PCIE8X2_PB200","B_CABLE_MX_PCIE8X2_PB201"}),
                        Span(cable, "B1", "C1", 0.1046f, new Vector3(-0.0039f,0.0026f,-0.011f), new[] {"B_CABLE_MX_PCIE8X2_DC100","B_CABLE_MX_PCIE8X2_DC101"}),
                        Span(cable, "C1", "C2", 0.0494f, new Vector3(0.0f,0.0026f,-0.011f), new[] {"B_CABLE_MX_PCIE8X2_PC200","B_CABLE_MX_PCIE8X2_PC201"}),
                    }; break;
                case "CABLE_AC_C13_JP":
                    cable.spans = new[] {
                        Span(cable, "A", "B", 1.5f, new Vector3(0.0f,0.0f,-0.028f), new[] {"B_CABLE_AC_C13_JP_J00","B_CABLE_AC_C13_JP_J01","B_CABLE_AC_C13_JP_J02","B_CABLE_AC_C13_JP_J03","B_CABLE_AC_C13_JP_J04","B_CABLE_AC_C13_JP_J05","B_CABLE_AC_C13_JP_J06","B_CABLE_AC_C13_JP_J07","B_CABLE_AC_C13_JP_J08","B_CABLE_AC_C13_JP_J09","B_CABLE_AC_C13_JP_J10","B_CABLE_AC_C13_JP_J11","B_CABLE_AC_C13_JP_J12","B_CABLE_AC_C13_JP_J13","B_CABLE_AC_C13_JP_J14","B_CABLE_AC_C13_JP_J15","B_CABLE_AC_C13_JP_J16","B_CABLE_AC_C13_JP_J17","B_CABLE_AC_C13_JP_J18","B_CABLE_AC_C13_JP_J19","B_CABLE_AC_C13_JP_J20","B_CABLE_AC_C13_JP_J21","B_CABLE_AC_C13_JP_J22","B_CABLE_AC_C13_JP_J23","B_CABLE_AC_C13_JP_J24","B_CABLE_AC_C13_JP_J25","B_CABLE_AC_C13_JP_J26","B_CABLE_AC_C13_JP_J27","B_CABLE_AC_C13_JP_J28","B_CABLE_AC_C13_JP_J29","B_CABLE_AC_C13_JP_J30","B_CABLE_AC_C13_JP_J31","B_CABLE_AC_C13_JP_J32"}),
                    }; break;
                default: throw new InvalidOperationException("Cable outside the S1 source contract " + asset);
            }
            return cable;
        }
        private static S1CableSpan Span(ResearchS1Cable cable, string from, string to, float length, Vector3 toExitMate, string[] names)
        {
            var bones = names.Select(n => cable.skin.bones.Single(b => b.name == n)).ToArray();
            var a = Head(cable, from); var b = Head(cable, to); var end = Mate(cable, to).TransformPoint(toExitMate);
            var fromMate = a.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "MATE_" + cable.assetId + "_End" + from);
            var sourceDirection = fromMate ? fromMate.TransformDirection(cable.assetId == "CABLE_MX_SATA3" && from != "A" ? Vector3.right : Vector3.back) : a.up;
            var targetDirection = Mate(cable,to).TransformDirection(cable.assetId == "CABLE_MX_SATA3" ? Vector3.left : Vector3.back);
            return new S1CableSpan { from = a, to = b, bones = bones, length = length, minimumRadius = cable.assetId == "CABLE_SATA_DATA_S_500" ? .01f : 0,
                fromExit = a.InverseTransformPoint(bones[0].position), toExit = b.InverseTransformPoint(end),
                fromDirection = a.InverseTransformDirection(sourceDirection), toDirection = b.InverseTransformDirection(targetDirection),
                restToPosition = a.InverseTransformPoint(b.position), restToRotation = Quaternion.Inverse(a.rotation) * b.rotation,
                restFromRotation = Quaternion.Inverse(cable.transform.rotation) * a.rotation,
                restPositions = bones.Select(t => a.InverseTransformPoint(t.position)).ToArray(),
                restRotations = bones.Select(t => Quaternion.Inverse(cable.transform.rotation) * t.rotation).ToArray(),
                restDirections = bones.Select((t,i) => cable.transform.InverseTransformDirection((i+1 < bones.Length ? bones[i+1].position : end) - t.position).normalized).ToArray() };
        }
    }
}
#endif
