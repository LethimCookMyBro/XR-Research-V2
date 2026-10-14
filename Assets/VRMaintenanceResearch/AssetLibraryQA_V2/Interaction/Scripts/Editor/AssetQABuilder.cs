#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.AssetLibraryQA.Editor
{
    // Builds the interaction QA prefabs and scene from the library FBX files and QA/unity/interaction_rules.json.
    // Writes only under AssetLibraryQA_V2/Interaction; the HandsLab prefab is instanced, never edited.
    public static class AssetQABuilder
    {
        public const string Lib = "D:/VR_TMU/XR-Research-V3-Assets/ModularMaintenanceLibrary_V2";
        public const string QA = "Assets/VRMaintenanceResearch/AssetLibraryQA_V2";
        public const string Out = QA + "/Interaction";
        public const string ScenePath = Out + "/AssetQA_Interaction.unity";
        const string HandsLabPrefab = "Assets/VRMaintenanceResearch/HandsLabQuest3/Prefabs/HandsLab_Quest3.prefab";
        static readonly string[] HiddenHelpers = { "COL", "SEL", "INS" };

        public static JObject Rules => JObject.Parse(File.ReadAllText(Lib + "/QA/unity/interaction_rules.json"));
        public static string StableId(string asset) => "assetqa." + asset.ToLowerInvariant();

        // discardDirtyScene: the path of an open dirty scene the caller has diffed and accepts to drop unsaved.
        public static string Build(string discardDirtyScene = null)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty && SceneManager.GetSceneAt(i).path != discardDirtyScene)
                    throw new InvalidOperationException("Unsaved scene open: " + SceneManager.GetSceneAt(i).path);

            var rules = Rules;
            var plugs = new HashSet<string>(rules["bundles"].SelectMany(b => b["parts"].Select(p => (string)p)));
            Directory.CreateDirectory(Out + "/Prefabs");
            var prefabs = new Dictionary<string, GameObject>();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var part in rules["parts"])
                {
                    string asset = (string)part["asset"];
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{QA}/FBX/{part["family"]}/{asset}.fbx");
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                    Configure(go, part, rules, plugs);
                    prefabs[asset] = PrefabUtility.SaveAsPrefabAsset(go, $"{Out}/Prefabs/{asset}.prefab");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            // Prefabs and the scene are written by their own save calls. No AssetDatabase.SaveAssets(): it saves every
            // dirty asset in the project, and wrote the dynamic Thai TMP font atlas outside this folder on 2026-10-10.
            BuildScene(rules, prefabs);
            return $"built {prefabs.Count} prefabs and {ScenePath}";
        }

        static void Configure(GameObject go, JToken part, JObject rules, HashSet<string> plugs)
        {
            string asset = (string)part["asset"];
            bool plug = plugs.Contains(asset);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string kind = t.name.Split('_')[0];
                var r = t.GetComponent<MeshRenderer>();
                if (kind == "COL")
                {
                    var b = t.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var box = t.gameObject.AddComponent<BoxCollider>();
                    box.center = b.center; box.size = b.size;
                }
                if (r && (HiddenHelpers.Contains(kind) || t.name.Contains("_LOD") && !t.name.EndsWith("_LOD0"))) r.enabled = false;
            }
            var animator = go.GetComponent<Animator>();
            if (animator) animator.enabled = false;

            Rigidbody body = null; Grabbable grabbable = null;
            if (plug)
            {
                body = go.AddComponent<Rigidbody>();
                body.mass = (float)part["mass_kg"];
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                grabbable = go.AddComponent<Grabbable>();
                var qaPlug = go.AddComponent<QAPlug>();
                qaPlug.assetId = asset; qaPlug.stableId = StableId(asset); qaPlug.body = body; qaPlug.grabbable = grabbable;
                qaPlug.mates = go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MATE_" + asset + "_")).ToList();
                grabbable.InjectOptionalRigidbody(body);
                grabbable.InjectOptionalKinematicWhileSelected(true);
                grabbable.InjectOptionalThrowWhenUnselected(true);
                grabbable.InjectOptionalOneGrabTransformer(qaPlug);
                grabbable.MaxGrabPoints = 1;
                var types = part["grab_types"].Select(t => (string)t == "Pinch" ? GrabTypeFlags.Pinch : GrabTypeFlags.Palm).Aggregate((a, b) => a | b);
                AddInteractable(go.transform, body, grabbable, types);
                var item = go.AddComponent<HandsLabObject>();
                item.stableId = qaPlug.stableId; item.body = body; item.grabbable = grabbable;
            }

            var sockets = new Dictionary<string, QASocket>();
            var used = rules["pairs"].Where(p => (string)p["socket"] == asset && plugs.Contains((string)p["plug"])).Select(p => (string)p["seat"]).ToHashSet();
            foreach (var s in rules["seats"].Where(s => (string)s["asset"] == asset && used.Contains((string)s["seat"])))
            {
                var socket = go.AddComponent<QASocket>();
                socket.fixtureId = asset; socket.seatName = (string)s["seat"];
                socket.seat = go.GetComponentsInChildren<Transform>(true).Single(t => t.name == (string)s["node"]);
                socket.spec = new SeatSpec
                {
                    approachMm = (float)s["approach_mm"], posTolMm = (float)s["pos_tol_mm"], angTolDeg = (float)s["ang_tol_deg"],
                    hysteresisMm = (float)s["hysteresis_mm"], hysteresisDeg = (float)s["hysteresis_deg"], seatedDepthTolMm = (float)s["seated_depth_tol_mm"]
                };
                socket.rules = rules["pairs"].Where(p => (string)p["socket"] == asset && (string)p["seat"] == socket.seatName).Select(p => new PairRule
                {
                    plug = (string)p["plug"], mate = (string)p["mate"], testId = (string)p["test_id"], category = (string)p["category"],
                    outcome = (string)p["outcome"] == "Seat" ? PairOutcome.Seat : PairOutcome.Block,
                    turnDeg = (float)p["turn_deg"], reachMm = (float)p["reach_mm"]
                }).ToList();
                sockets[asset + "/" + socket.seatName] = socket;
                foreach (var latchNode in s["latch_nodes"].Select(n => (string)n))
                {
                    var spec = part["latches"].First(l => (string)l["node"] == latchNode);
                    socket.latches.Add(AddLatch(go, latchNode, socket, (string)part["family"], asset, spec));
                }
            }
            foreach (var s in rules["seats"].Where(s => sockets.ContainsKey((string)s["asset"] + "/" + (string)s["seat"])))
            {
                var socket = sockets[(string)s["asset"] + "/" + (string)s["seat"]];
                socket.requires = s["requires"].Select(k => sockets[(string)k]).ToList();
                socket.retainedBy = s["retained_by"].Where(k => sockets.ContainsKey((string)k)).Select(k => sockets[(string)k]).ToList();
            }
        }

        // Only the controller-driven hand grab selects in the HandsLab rig, so that is the one interactable added.
        // A Touch grip button curls middle, ring and pinky; the trigger curls the index. The SDK default palm rule
        // also needs the index, so with it a grip-button grab only ever matched the pinch rule and a palm-only part
        // could not be picked up. Palm here is the grip button, pinch the trigger, so the two never cross.
        static readonly GrabbingRule PalmRule = Rule(FingerRequirement.Optional, FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Required, FingerRequirement.Optional);
        static readonly GrabbingRule PinchRule = Rule(FingerRequirement.Optional, FingerRequirement.Required, FingerRequirement.Ignored, FingerRequirement.Ignored, FingerRequirement.Ignored);

        static GrabbingRule Rule(params FingerRequirement[] thumbToPinky)
        {
            var rule = new GrabbingRule();
            for (int i = 0; i < thumbToPinky.Length; i++) rule[(Oculus.Interaction.Input.HandFinger)i] = thumbToPinky[i];
            return rule;
        }

        public static void AddInteractable(Transform root, Rigidbody body, IPointableElement pointable, GrabTypeFlags types)
        {
            var hand = new GameObject("HandGrabInteractable").AddComponent<HandGrabInteractable>();
            hand.transform.SetParent(root, false);
            hand.InjectAllHandGrabInteractable(types, body, PinchRule, PalmRule);
            if (pointable != null) hand.InjectOptionalPointableElement(pointable);
        }

        static QALatch AddLatch(GameObject root, string node, QASocket socket, string family, string asset, JToken spec)
        {
            var t = root.GetComponentsInChildren<Transform>(true).Single(x => x.name == node);
            var b = t.GetComponent<MeshFilter>().sharedMesh.bounds;
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.center = b.center; box.size = b.size + Vector3.one * .004f;
            var body = t.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false;
            AddInteractable(t, body, null, GrabTypeFlags.All);
            var clips = AssetDatabase.LoadAllAssetsAtPath($"{QA}/FBX/{family}/{asset}.fbx").OfType<AnimationClip>().ToList();
            var latch = t.gameObject.AddComponent<QALatch>();
            latch.socket = socket; latch.animationRoot = root;
            latch.openClip = clips.Single(c => c.name == (string)spec["open_clip"]);
            latch.closeClip = clips.Single(c => c.name == (string)spec["close_clip"]);
            latch.interactables = t.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m is IInteractableView && m.transform.parent == t).ToArray();
            return latch;
        }

        // Rests the part 2 mm above whatever collider is below its footprint centre.
        static void DropOnto(GameObject go, Vector3 xz)
        {
            go.transform.position = new Vector3(xz.x, 0, xz.z);
            var b = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            Physics.SyncTransforms();
            var mine = go.GetComponentsInChildren<Collider>();
            var hits = Physics.RaycastAll(new Vector3(b.center.x, 3, b.center.z), Vector3.down, 5).Where(h => !mine.Contains(h.collider) && h.point.y < 1.2f).OrderBy(h => h.distance);
            float support = hits.Any() ? hits.First().point.y : 0;
            go.transform.position += new Vector3(0, support - b.min.y + .002f, 0);
        }

        static void BuildScene(JObject rules, Dictionary<string, GameObject> prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The HandsLab scene is this prefab plus a Pointable Canvas Module root; mirror both.
            var module = new GameObject("Pointable Canvas Module");
            SceneManager.MoveGameObjectToScene(module, scene);
            module.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var canvasModule = new SerializedObject(module.AddComponent<PointableCanvasModule>());
            canvasModule.FindProperty("_useInitialPressPositionForDrag").boolValue = true;
            canvasModule.FindProperty("_exclusiveMode").boolValue = true;
            canvasModule.ApplyModifiedPropertiesWithoutUndo();
            var lab = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HandsLabPrefab), scene);
            var session = lab.GetComponent<HandsLabSession>();
            var table = lab.transform.Find("Table").GetComponent<Collider>().bounds;

            var placed = new List<HandsLabObject>();
            var groups = new Dictionary<string, Transform>();
            // asset, bundle, x, z (table space), flat: lay the part down; fixtures and drives stay as authored.
            var layout = new (string asset, string bundle, float x, float z, bool flat)[]
            {
                ("SLOT_DIMM_DDR4_TEST", "ram_slot", -.42f, .80f, false),
                ("RAM_DDR4_UDIMM_BARE", "ram_slot", -.45f, .42f, true),
                ("RAM_DDR5_UDIMM_BARE", "ram_slot", -.45f, .56f, true),
                ("CASE_SLED_25", "ssd_cable_tray", -.02f, .70f, false),
                ("SSD_SATA_25_7MM", "ssd_cable_tray", -.06f, .42f, false),
                ("CABLE_SATA_DATA_S_500", "ssd_cable_tray", -.16f, .345f, false),
                ("FAN_PED_GUARD_REAR", "fan_guard_lock", .45f, .70f, true),
                ("FAN_PED_GUARD_LOCK", "fan_guard_lock", .45f, .37f, true),
            };
            foreach (var (asset, bundle, x, z, flat) in layout)
            {
                if (!groups.TryGetValue(bundle, out var group))
                {
                    group = groups[bundle] = new GameObject("QA_" + bundle).transform;
                    SceneManager.MoveGameObjectToScene(group.gameObject, scene);
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[asset], scene);
                go.transform.SetParent(group, false);
                if (flat) go.transform.rotation = Quaternion.Euler(-90, 0, 0) * go.transform.rotation;
                DropOnto(go, new Vector3(x, 0, z));
                foreach (var s in go.GetComponents<QASocket>()) s.session = session;
                if (go.TryGetComponent<HandsLabObject>(out var item)) placed.Add(item);
            }
            var head = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["FAN_PED_HEAD"], scene);
            head.transform.SetParent(groups["fan_guard_lock"], false);
            head.transform.SetPositionAndRotation(new Vector3(table.max.x + .35f, 0, table.center.z), Quaternion.Euler(0, 180, 0) * head.transform.rotation);
            foreach (var s in head.GetComponents<QASocket>()) s.session = session;

            var so = new SerializedObject(session);
            var objects = so.FindProperty("objects");
            int start = objects.arraySize;
            objects.arraySize = start + placed.Count;
            for (int i = 0; i < placed.Count; i++) objects.GetArrayElementAtIndex(start + i).objectReferenceValue = placed[i];
            so.FindProperty("logRoot").stringValue = Lib + "/QA/unity/interaction/logs";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
#endif
