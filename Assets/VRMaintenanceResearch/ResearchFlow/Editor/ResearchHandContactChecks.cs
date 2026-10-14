#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using Oculus.Interaction.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchHandContactChecks
    {
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        public static IEnumerator NativeSequence(ResearchSessionFlowController controller, Action<bool, string, object> check)
        {
            if (!EditorApplication.isPlaying || !ResearchFlowVisualInput.Running) throw new InvalidOperationException("Existing native synthetic-input fixture required.");
            var proxies = controller.origin.GetComponentsInChildren<ResearchHandContactProxy>(true);
            check(proxies.Length == 2, "two live contact proxies installed on existing SDK visuals", CaptureLiveStatus());
            var right = proxies.Single(p => p.handVisual.Hand.Handedness == Handedness.Right);
            var input = controller.guards.Single(g => g.side == Handedness.Right).controllerReference as IController;
            var guard = controller.guards.Single(g => g.side == Handedness.Right);
            var env = controller.gameObject.scene.GetRootGameObjects().Single(g => g.name == "Environment_V2");
            var table = env.GetComponentsInChildren<Collider>(true).Single(c => c.name == "Training_Practice_A" && c.enabled);
            var original = ResearchFlowVisualInput.ControllerWorldPose(1);
            var pose = new Pose(new Vector3(table.bounds.center.x, table.bounds.max.y + .4f, table.bounds.center.z), Quaternion.identity);
            try
            {
                ResearchFlowVisualInput.SetControllerWorldPose(1, pose); yield return .2d;
                check(right.SeparationMetres < .005f && input != null && input.IsPoseValid,
                    "free hand pose remains unstuck after native teleport and snap turn", CaptureLiveStatus());
                for (int i = 0; i < 20; i++)
                {
                    pose.position.y -= .025f;
                    ResearchFlowVisualInput.SetControllerWorldPose(1, pose); yield return .025d;
                }
                check(right.ContactUpdates > 0 && right.SeparationMetres > .03f, "native controller hand render skeleton constrained at actual tabletop", CaptureLiveStatus());
                check(input != null && input.TryGetPose(out Pose native) && Vector3.Distance(native.position, pose.position) < .01f,
                    "raw native controller follows commanded target inside table", CaptureLiveStatus());
                right.ResetContact(); yield return .15d;
                check(right.SeparationMetres > .03f, "deep target reset recovers from camera-side sweep", CaptureLiveStatus());
                ResearchFlowVisualInput.SetControllerTracked(1, false);
                yield return .3d;
                check(!input.IsPoseValid && !guard.TrackingValid && right.SeparationMetres < .001f && !right.GhostVisible,
                    "actual simulated controller tracking loss clears contact and ghost", CaptureLiveStatus());
                ResearchFlowVisualInput.SetControllerTracked(1, true);
                yield return .3d;
                check(input.IsPoseValid && guard.TrackingValid && right.SeparationMetres > .03f,
                    "native source reacquires deep target with surface contact restored", CaptureLiveStatus());
                pose.position.y = table.bounds.max.y + .4f;
                ResearchFlowVisualInput.SetControllerWorldPose(1, pose); yield return .25d;
                check(right.SeparationMetres < .005f, "native hand pulls back freely after contact", CaptureLiveStatus());
                check(right.SaturatedQueries == 0, "native hand contact query buffers did not saturate", CaptureLiveStatus());
            }
            finally
            {
                if (ResearchFlowVisualInput.Running)
                {
                    ResearchFlowVisualInput.SetControllerTracked(1, true);
                    ResearchFlowVisualInput.SetControllerWorldPose(1, original);
                }
            }
        }

        public static string CaptureLiveStatus()
        {
            var proxies = UnityEngine.Object.FindObjectsByType<ResearchHandContactProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return JsonConvert.SerializeObject(new { hardware_verified = false, proxies = proxies.Select(p =>
            {
                bool valid = p.handVisual && p.handVisual.Hand != null && p.handVisual.Hand.GetRootPose(out _);
                Pose raw = default;
                if (valid) p.handVisual.Hand.GetRootPose(out raw);
                return new { path = PathOf(p.transform), valid, native_root = V(raw.position), visual_root = p.handVisual && p.handVisual.Root ? V(p.handVisual.Root.position) : null,
                    p.SeparationMetres, p.DeepPenetration, p.GhostAvailable, p.GhostVisible, p.ContactUpdates, p.UnresolvedContacts, p.SaturatedQueries,
                    p.LastUpdateMilliseconds, p.MaxUpdateMilliseconds, environment_count = p.environment.Length };
            }).ToArray() });
        }

        public static string ApplyRemainingStatic()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stable Edit Mode required.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity") throw new InvalidOperationException("Wrong scene.");
            var env = scene.GetRootGameObjects().Single(r => r.name == "Environment_V2");
            var meshes = env.GetComponentsInChildren<MeshFilter>(true);
            var classification = JObject.Parse(File.ReadAllText("Logs/QuestFinalIntegration/hand-contact-remaining-classification.json"));
            var rows = new List<object>(); int added = 0, backed = 0;
            foreach (var row in classification["objects"].Where(r => r.Value<string>("status") == "UNRESOLVED"))
            {
                string path = row.Value<string>("path");
                var mesh = meshes.Single(m => PathOf(m.transform) == path);
                if (mesh.GetComponents<Collider>().Any(c => c.enabled && !c.isTrigger)) { rows.Add(new { path, status = "Already solid" }); continue; }
                var behaviours = mesh.GetComponentsInParent<MonoBehaviour>(true).Where(b => b).ToArray();
                if (mesh.GetComponentInParent<Rigidbody>() || mesh.GetComponentInParent<Animator>() || behaviours.Length > 0)
                    throw new InvalidOperationException("Runtime movement/interaction needs review: " + path + " components=" + string.Join(",", behaviours.Select(b => b.GetType().Name)));
                if (!mesh.sharedMesh.isReadable || mesh.sharedMesh.vertexCount > 4000) throw new InvalidOperationException("Geometry cost/readability changed: " + path);
                bool decoration = row.Value<string>("classification").StartsWith("C ", StringComparison.Ordinal);
                bool hasBacking = decoration && mesh.sharedMesh.vertices.All(v => Physics.OverlapSphere(mesh.transform.TransformPoint(v), .025f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => c.enabled && !c.attachedRigidbody && c.transform.IsChildOf(env.transform)));
                if (hasBacking) { backed++; rows.Add(new { path, status = "Every actual vertex within 25mm of existing solid backing", triangles = mesh.sharedMesh.triangles.Length / 3 }); continue; }
                var unique = mesh.sharedMesh.vertices.Distinct().ToArray();
                bool exactBox = IsBox(unique) && mesh.sharedMesh.triangles.Length == 36;
                if (exactBox)
                {
                    var box = Undo.AddComponent<BoxCollider>(mesh.gameObject);
                    var min = new Vector3(unique.Min(v => v.x), unique.Min(v => v.y), unique.Min(v => v.z));
                    var max = new Vector3(unique.Max(v => v.x), unique.Max(v => v.y), unique.Max(v => v.z));
                    box.center = (min + max) * .5f; box.size = max - min;
                }
                else
                {
                    var collider = Undo.AddComponent<MeshCollider>(mesh.gameObject);
                    collider.sharedMesh = mesh.sharedMesh; collider.convex = false;
                }
                added++;
                rows.Add(new { path, status = exactBox ? "Exact cuboid BoxCollider" : "Low-poly authored static MeshCollider", triangles = mesh.sharedMesh.triangles.Length / 3,
                    runtime_movers = behaviours.Length, note = "Drawer source occurrences are Editor pivot fixtures only; live no Rigidbody, Animator or ancestor MonoBehaviour verified before treating as static." });
            }
            Physics.SyncTransforms();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            string report = "Logs/QuestFinalIntegration/hand-contact-remaining-applied.json";
            File.WriteAllText(report, JsonConvert.SerializeObject(new { hardware_verified = false, added, backed, rows }, Formatting.Indented));
            return "Remaining static contact added=" + added + "; backed decorative=" + backed + "; scene not saved; cost unverified on Quest";
        }

        static readonly string[] ContactFurniture = { "Training_Stool_2", "Computer_Stool_Parked", "Fan_Stool_Parked", "Polish_Training_ReturnCart", "Fan_Support",
            "Training_Practice_A", "Computer_Main", "Computer_Support", "Fan_Main", "Computer_RollingToolCabinet", "Computer_ComponentStorage",
            "Fan_SparesRack", "Fan_SparesLowerShelf", "Polish_Computer_UtilityShelf", "Polish_Fan_UtilityShelf" };

        // Parent invokes after auditing; intentionally leaves the scene dirty and never saves it.
        public static string ApplyAuditedFurniture()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || scene.path != "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity")
                throw new InvalidOperationException("Stable integration-scene Edit Mode required.");
            var env = scene.GetRootGameObjects().Single(r => r.name == "Environment_V2");
            var meshes = env.GetComponentsInChildren<MeshFilter>(true);
            var selected = ContactFurniture.Select(name => meshes.Single(m => m.name == name)).ToArray();
            foreach (var m in selected)
                if (!m.sharedMesh || !m.sharedMesh.isReadable || m.sharedMesh.vertexCount > 4000
                    || m.GetComponentInParent<Rigidbody>() || m.GetComponentInParent<Grabbable>() || m.GetComponentInParent<Animator>())
                    throw new InvalidOperationException("Audited static geometry changed: " + PathOf(m.transform));
            Directory.CreateDirectory("Logs/QuestFinalIntegration");
            string report = "Logs/QuestFinalIntegration/hand-contact-furniture-before.json";
            if (File.Exists(report)) throw new InvalidOperationException("Preserve previous installation evidence before rerunning.");
            File.WriteAllText(report, JsonConvert.SerializeObject(selected.Select(m => new
            { path = PathOf(m.transform), mesh = AssetDatabase.GetAssetPath(m.sharedMesh), m.sharedMesh.vertexCount,
                colliders = m.GetComponents<Collider>().Select(c => new { type = c.GetType().Name, state = EditorJsonUtility.ToJson(c) }).ToArray() }), Formatting.Indented));
            foreach (var m in selected)
            {
                // Bevelled/curved disconnected parts failed exact cuboid detection. Use the bounded authored static mesh, preserving all holes.
                foreach (var c in m.GetComponents<Collider>())
                { Undo.RecordObject(c, "Replace enclosing furniture collider with exact static mesh"); c.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
                var collider = Undo.AddComponent<MeshCollider>(m.gameObject);
                collider.sharedMesh = m.sharedMesh; collider.convex = false; collider.isTrigger = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            Physics.SyncTransforms();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            return "Installed exact static mesh contact on " + selected.Length + " named furniture objects; preserved disabled old shapes; scene not saved; Quest physics cost unverified.";
        }

        public static string RunSceneGeometry()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Scene geometry checks require Edit Mode.");
            var env = SceneManager.GetActiveScene().GetRootGameObjects().Single(r => r.name == "Environment_V2");
            var solids = env.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && !c.attachedRigidbody).ToArray();
            var host = new GameObject("HandContact_SceneCheckOnly");
            var rows = new List<object>();
            try
            {
                var proxy = host.AddComponent<ResearchHandContactProxy>();
                foreach (var name in ContactFurniture.Concat(new[] { "Computer_TaskCase", "Fan_TaskFan" }))
                {
                    var collider = solids.Single(c => c.name == name);
                    proxy.ConfigureEnvironment(new[] { collider });
                    var b = collider.bounds;
                    bool sampled = false;
                    // Find a real triangle surface; extents only choose rays, never substitute collision geometry.
                    for (int x = 0; x < 5 && !sampled; x++)
                        for (int z = 0; z < 5 && !sampled; z++)
                        {
                            var ray = new Ray(new Vector3(Mathf.Lerp(b.min.x, b.max.x, (x + .5f) / 5), b.max.y + .5f,
                                Mathf.Lerp(b.min.z, b.max.z, (z + .5f) / 5)), Vector3.down);
                            if (!collider.Raycast(ray, out RaycastHit hit, b.size.y + 1)) continue;
                            Vector3 start = hit.point + hit.normal * .15f, target = hit.point - hit.normal * .1f;
                            Vector3 resolved = proxy.ResolveSphere(start, target, .02f);
                            bool pass = Vector3.Dot(resolved - hit.point, hit.normal) >= .019f;
                            rows.Add(new { name, passed = pass, actual_surface = V(hit.point), target = V(target), resolved = V(resolved), collider = collider.GetType().Name });
                            if (!pass) throw new InvalidOperationException(name + " actual scene surface penetrated");
                            sampled = true;
                        }
                    if (!sampled) throw new InvalidOperationException(name + " could not sample actual collider surface");
                }
                string path = "Logs/QuestFinalIntegration/hand-contact-scene-checks.json";
                File.WriteAllText(path, JsonConvert.SerializeObject(new { hardware_verified = false, cases = rows }, Formatting.Indented));
                return "HAND_CONTACT_SCENE_PASS actual authored geometry cases=" + rows.Count;
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        public static string InspectGeometry()
        {
            var names = new[] { "Training_Stool_2", "Computer_Stool_Parked", "Fan_Stool_Parked", "Polish_Training_ReturnCart", "Fan_Support" };
            var meshes = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true));
            var rows = meshes.Where(m => names.Contains(m.name)).Select(m => new
            {
                path = PathOf(m.transform), mesh = AssetDatabase.GetAssetPath(m.sharedMesh),
                components = Components(m.sharedMesh).Select(v => new
                {
                    vertex_count = v.Length, exact_axis_aligned_box = IsBox(v),
                    min = V(new Vector3(v.Min(p => p.x), v.Min(p => p.y), v.Min(p => p.z))),
                    max = V(new Vector3(v.Max(p => p.x), v.Max(p => p.y), v.Max(p => p.z)))
                }).ToArray()
            }).ToArray();
            const string path = "Logs/QuestFinalIntegration/hand-contact-geometry.json";
            File.WriteAllText(path, JsonConvert.SerializeObject(rows, Formatting.Indented));
            return System.IO.Path.GetFullPath(path);
        }

        static bool IsBox(Vector3[] v) => v.Length == 8 && v.Select(p => p.x).Distinct().Count() == 2
            && v.Select(p => p.y).Distinct().Count() == 2 && v.Select(p => p.z).Distinct().Count() == 2;

        static IEnumerable<Vector3[]> Components(Mesh mesh)
        {
            if (!mesh.isReadable) throw new InvalidOperationException("Geometry audit requires readable mesh.");
            var vertices = mesh.vertices;
            var unique = new Dictionary<Vector3, int>();
            var indices = new int[vertices.Length];
            var parent = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                if (!unique.TryGetValue(v, out int index)) { index = unique.Count; unique.Add(v, index); parent.Add(index); }
                indices[i] = index;
            }
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int first = Find(indices[triangles[i]]);
                parent[Find(indices[triangles[i + 1]])] = first;
                parent[Find(indices[triangles[i + 2]])] = first;
            }
            return unique.GroupBy(p => Find(p.Value)).Select(g => g.Select(p => p.Key).ToArray()).ToArray();
        }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Contact geometry fixtures require Edit Mode.");
            var host = new GameObject("HandContact_CheckOnly");
            var obstacle = new GameObject("HandContact_Surface_CheckOnly");
            int count = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); count++; }
            try
            {
                var proxy = host.AddComponent<ResearchHandContactProxy>();
                var box = obstacle.AddComponent<BoxCollider>();
                obstacle.transform.position = new Vector3(0, 100, 0);
                box.size = new Vector3(2, .02f, 2);
                proxy.ConfigureEnvironment(new Collider[] { box }); Physics.SyncTransforms();
                foreach (string name in new[] { "palm tabletop", "finger tabletop", "chair seat", "computer exterior", "fan exterior" })
                {
                    float radius = name.StartsWith("finger") ? .009f : .035f;
                    float thickness = name == "chair seat" ? .06f : name == "computer exterior" ? .6f : name == "fan exterior" ? .25f : .02f;
                    box.size = new Vector3(2, thickness, 2); Physics.SyncTransforms();
                    float top = 100 + thickness * .5f;
                    Vector3 result = proxy.ResolveSphere(new Vector3(0, top + .3f, 0), new Vector3(0, 99.5f, 0), radius);
                    Check(result.y >= top + radius - .0001f, name + " swept contact");
                    Check(proxy.ResolveSphere(result, new Vector3(.3f, 99.8f, 0), radius).x > .29f, name + " slides along surface");
                    Check(proxy.ResolveSphere(result, new Vector3(0, top + .4f, 0), radius).y > top + .39f, name + " pull back");
                }
                box.size = new Vector3(.02f, 2, 2); Physics.SyncTransforms();
                Check(proxy.ResolveSphere(new Vector3(.3f, 100, 0), new Vector3(-.3f, 100, 0), .035f).x > .044f, "wall sweep");
                box.isTrigger = true; Physics.SyncTransforms();
                Check(proxy.ResolveSphere(new Vector3(.3f, 100, 0), new Vector3(-.3f, 100, 0), .035f).x < -.29f, "trigger excluded");
                box.isTrigger = false;
                var body = obstacle.AddComponent<Rigidbody>(); body.isKinematic = true;
                proxy.ConfigureEnvironment(new Collider[] { box }); Physics.SyncTransforms();
                Check(proxy.ResolveSphere(new Vector3(.3f, 100, 0), new Vector3(-.3f, 100, 0), .035f).x < -.29f, "held/movable body excluded");
                Check(body.isKinematic && body.linearVelocity == Vector3.zero, "no held object impulse or body mutation");
                UnityEngine.Object.DestroyImmediate(body); proxy.ConfigureEnvironment(new Collider[] { box }); Physics.SyncTransforms();
                proxy.ResetContact();
                Check(proxy.ResolveSphere(new Vector3(.3f, 100, 0), new Vector3(-.3f, 100, 0), .035f).x > .044f, "reset/reacquire retains static collision set");
                Check(proxy.SeparationMetres == 0 && !proxy.DeepPenetration, "reset clears contact state");
                return "HAND_CONTACT_GEOMETRY_PASS checks=" + count + "; synthetic primitives only; actual SDK grab, skeleton, teleport and Quest acceptance remain separate";
            }
            finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(obstacle); }
        }

        // Inspection only. Reachability is conservative, not a replacement for a walk-through.
        public static string Audit()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for a stable editor before collider audit.");
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var meshes = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
                .Where(m => m.sharedMesh && !m.GetComponentInParent<Canvas>()).ToArray();
            var colliders = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
            var objects = meshes.Select(m =>
            {
                var candidates = m.GetComponents<Collider>().Concat(m.GetComponentsInParent<Collider>(true)).Distinct().ToArray();
                var solid = candidates.Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
                var body = m.GetComponentInParent<Rigidbody>();
                bool movable = body || m.GetComponentInParent<Grabbable>();
                var bounds = m.sharedMesh.bounds;
                var corners = Enumerable.Range(0, 8).Select(i => m.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))).ToArray();
                // Only height rules out reachability. All low environment geometry stays in the audit.
                bool reachable = m.gameObject.activeInHierarchy && corners.Min(v => v.y) <= 2.3f && corners.Max(v => v.y) >= 0;
                float? vertexGap = null;
                bool closestPointSupported = solid.All(c => c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider mc && mc.convex);
                if (m.sharedMesh.isReadable && solid.Length > 0 && closestPointSupported)
                    vertexGap = m.sharedMesh.vertices.Select(v => m.transform.TransformPoint(v))
                        .Max(v => solid.Min(c => Vector3.Distance(v, c.ClosestPoint(v))));
                return new
                {
                    path = PathOf(m.transform), classification = movable ? "B Interactable/grabbable" : reachable ? "A Static environment (conservative reachable)" : "C Decorative/non-reachable by height or inactive",
                    reachable_candidate = reachable, active = m.gameObject.activeInHierarchy,
                    mesh = AssetDatabase.GetAssetPath(m.sharedMesh), m.sharedMesh.vertexCount, readable = m.sharedMesh.isReadable,
                    position = V(m.transform.position), scale = V(m.transform.lossyScale), layer = m.gameObject.layer,
                    geometry_min = new[] { corners.Min(v => v.x), corners.Min(v => v.y), corners.Min(v => v.z) },
                    geometry_max = new[] { corners.Max(v => v.x), corners.Max(v => v.y), corners.Max(v => v.z) },
                    colliders = candidates.Select(c => PathOf(c.transform)).ToArray(),
                    missing = candidates.Length == 0, disabled_only = candidates.Length > 0 && candidates.All(c => !c.enabled || !c.gameObject.activeInHierarchy),
                    no_enabled_solid = solid.Length == 0,
                    trigger_only = candidates.Length > 0 && candidates.All(c => c.isTrigger), closest_point_supported = closestPointSupported, max_vertex_distance_from_solid = vertexGap,
                    geometry_coverage_suspect = vertexGap > .025f,
                    coverage_note = "Own/ancestor colliders only; sibling compound colliders and holes require geometry review. Bounds used for audit extent only, never physical collision."
                };
            }).ToArray();
            var result = new
            {
                utc = DateTime.UtcNow, scene = scene.path, scene.isDirty, hardware_verified = false,
                objects,
                colliders = colliders.Select(c => new
                {
                    path = PathOf(c.transform), type = c.GetType().Name, c.enabled, c.isTrigger, active = c.gameObject.activeInHierarchy,
                    layer = c.gameObject.layer, layer_name = LayerMask.LayerToName(c.gameObject.layer), scale = V(c.transform.lossyScale),
                    rigidbody = c.attachedRigidbody ? PathOf(c.attachedRigidbody.transform) : null,
                    center = V(c.bounds.center), size = V(c.bounds.size), shape = EditorJsonUtility.ToJson(c),
                    thin_axis = c.enabled && Mathf.Min(c.bounds.size.x, c.bounds.size.y, c.bounds.size.z) < .01f
                }).ToArray(),
                layers = Enumerable.Range(0, 32).Select(i => new
                {
                    id = i, name = LayerMask.LayerToName(i), collides_with = Enumerable.Range(0, 32).Where(j => !Physics.GetIgnoreLayerCollision(i, j)).ToArray()
                }).ToArray(),
                hand_visuals = roots.SelectMany(r => r.GetComponentsInChildren<HandVisual>(true)).Select(h => new
                {
                    path = PathOf(h.transform), active = h.isActiveAndEnabled, root = h.Root ? PathOf(h.Root) : null,
                    source = h.Hand is Component c ? PathOf(c.transform) : null, serialized = EditorJsonUtility.ToJson(h)
                }).ToArray()
            };
            const string folder = "Logs/QuestFinalIntegration";
            Directory.CreateDirectory(folder);
            string path = folder + "/hand-contact-audit-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json";
            File.WriteAllText(path, JsonConvert.SerializeObject(result, Formatting.Indented));
            return System.IO.Path.GetFullPath(path);
        }
    }
}
#endif
