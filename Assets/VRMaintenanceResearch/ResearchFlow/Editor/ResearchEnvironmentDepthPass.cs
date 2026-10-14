using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>
    /// Quest-safe depth/grounding pass: baked contact-shadow quads, work-zone floor markings, baked per-room
    /// reflection probes, warm key / cool fill, and removal of two decorative shimmer labels.
    /// No post-processing, no new realtime lights, no new shadow casters. Idempotent: rebuilds its own root.
    /// </summary>
    public static class ResearchEnvironmentDepthPass
    {
        const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const string AssetRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Materials/DepthPass";
        const string RootName = "ResearchFlow_EnvironmentDepthPass";
        // Floors were audited at y = 0 by ResearchEnvironmentGrounding.
        static readonly string[] Grounded = { "Training_Practice_A", "Computer_Main", "Computer_Support", "Fan_Main", "Fan_Support",
            "Training_Stool_2", "Computer_Stool_Parked", "Fan_Stool_Parked", "Polish_Training_ReturnCart", "Robot_A_Base", "Robot_B_Base", "Vehicle_R35", "Vehicle_Supra" };
        static readonly string[] Workstations = { "Training_Practice_A", "Computer_Main", "Fan_Main" };
        static readonly string[] Rooms = { "Training", "Computer", "Fan", "Corridor", "Factory" };
        static readonly string[] ShimmerLabels = { "Factory_Title", "Production_PASS_Label" };

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static float[] F(Vector3 v) => new[] { v.x, v.y, v.z };
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && scene.path == ScenePath, "Stable ResearchFlow Edit Mode required.");
            Require(!scene.isDirty, "Preserve the dirty scene before applying the depth pass.");
            string sceneHashBefore = Hash(File.ReadAllBytes(ScenePath));
            var environment = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2").transform;
            var all = environment.GetComponentsInChildren<Transform>(true);
            Transform Find(string name) => all.Single(t => t.name == name);

            Directory.CreateDirectory(AssetRoot);
            var contactMaterial = ContactMaterial();
            var markingMaterial = Material("DepthPass_SafetyMarking", "Universal Render Pipeline/Lit", m =>
            { m.SetColor("_BaseColor", new Color(.86f, .66f, .12f)); m.SetFloat("_Smoothness", .35f); m.SetFloat("_Metallic", 0); });

            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName)) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var changes = new List<object>();

            // 1. Contact shadows: yaw-aligned footprint of each grounded object, 4 mm above the floor.
            foreach (string name in Grounded)
            {
                var target = Find(name);
                var renderers = target.GetComponentsInChildren<MeshRenderer>(false).Where(r => r.enabled).ToArray();
                Require(renderers.Length > 0, name + " has no visible renderers.");
                float minY = renderers.Min(r => r.bounds.min.y);
                if (Mathf.Abs(minY) > .06f) { changes.Add(new { contact = name, skipped = "not floor-grounded", min_y = minY }); continue; }
                // Sampled mesh vertices: some imported meshes have loose (diagonal) local bounds.
                var corners = renderers.SelectMany(r =>
                {
                    var vertices = r.GetComponent<MeshFilter>()?.sharedMesh?.vertices ?? Array.Empty<Vector3>();
                    int step = Mathf.Max(1, vertices.Length / 3000);
                    return Enumerable.Range(0, vertices.Length / step).Select(i => r.transform.TransformPoint(vertices[i * step]));
                }).ToArray();
                Require(corners.Length > 0, name + " has no readable mesh vertices.");
                // Minimum-area yaw: import roots (e.g. glTF vehicles) need not align with the mesh's long axis.
                Quaternion frame = Quaternion.identity; var local = new Bounds(); float bestArea = float.MaxValue;
                for (int yawStep = 0; yawStep < 180; yawStep += 3)
                {
                    var candidate = Quaternion.Euler(0, target.eulerAngles.y + yawStep, 0);
                    var box = new Bounds(Quaternion.Inverse(candidate) * (corners[0] - target.position), Vector3.zero);
                    foreach (var corner in corners) box.Encapsulate(Quaternion.Inverse(candidate) * (corner - target.position));
                    if (box.size.x * box.size.z < bestArea - 1e-4f) { bestArea = box.size.x * box.size.z; frame = candidate; local = box; }
                }
                var center = target.position + frame * local.center; center.y = .004f;
                var size = new Vector2(local.size.x * 1.12f + .22f, local.size.z * 1.12f + .22f);
                Place(root, "Contact_" + name, quad, contactMaterial, center, frame * Quaternion.Euler(90, 0, 0), new Vector3(size.x, size.y, 1));
                changes.Add(new { contact = name, center = F(center), footprint = new[] { size.x, size.y }, yaw = frame.eulerAngles.y });
            }

            // 2. Work-zone outline around each participant workstation: scale reference and "fixed station" cue.
            foreach (string name in Workstations)
            {
                var contact = root.Find("Contact_" + name);
                if (!contact) continue;
                var yaw = Quaternion.Euler(0, contact.eulerAngles.y, 0);
                float halfX = contact.localScale.x / 2 + .45f, halfZ = contact.localScale.y / 2 + .45f, width = .05f;
                var c = contact.position; c.y = .006f;
                var sides = new[] { (new Vector3(0, 0, halfZ), new Vector2(halfX * 2 + width, width)), (new Vector3(0, 0, -halfZ), new Vector2(halfX * 2 + width, width)),
                    (new Vector3(halfX, 0, 0), new Vector2(width, halfZ * 2 - width)), (new Vector3(-halfX, 0, 0), new Vector2(width, halfZ * 2 - width)) };
                for (int i = 0; i < sides.Length; i++)
                    Place(root, "WorkZone_" + name + "_" + i, quad, markingMaterial, c + yaw * sides[i].Item1, yaw * Quaternion.Euler(90, 0, 0), new Vector3(sides[i].Item2.x, sides[i].Item2.y, 1));
                changes.Add(new { work_zone = name, half_extents = new[] { halfX, halfZ } });
            }

            // 3. Decorative lettering that aliases at distance (audited 1.15 deg thin strokes at 22.7 m; flat PASS label at grazing angle).
            foreach (string name in ShimmerLabels)
            {
                var r = Find(name).GetComponent<MeshRenderer>();
                changes.Add(new { label = name, enabled_before = r.enabled });
                r.enabled = false;
            }

            // 4. Warm key / cool fill. Same light count, same shadow caster; stronger key shadow for grounding.
            var key = scene.GetRootGameObjects().Single(g => g.name == "ResearchFlow_KeyLight").GetComponent<Light>();
            changes.Add(new { key_before = new { color = Hex(key.color), key.intensity, key.shadowStrength, elevation = key.transform.eulerAngles.x }, ambient_before = new { sky = Hex(RenderSettings.ambientSkyColor), equator = Hex(RenderSettings.ambientEquatorColor), ground = Hex(RenderSettings.ambientGroundColor) } });
            key.color = new Color(1f, .95f, .88f); key.intensity = 1.25f; key.shadowStrength = .82f;
            // Elevation 82 -> 64 deg: same light and shadow map, but vertical faces get form and shadows read as contact.
            key.transform.rotation = Quaternion.Euler(64, key.transform.eulerAngles.y, 0);
            RenderSettings.ambientSkyColor = new Color(.52f, .58f, .65f);
            RenderSettings.ambientEquatorColor = new Color(.40f, .43f, .45f);
            RenderSettings.ambientGroundColor = new Color(.25f, .22f, .20f);
            changes.Add(new { key_after = new { color = Hex(key.color), key.intensity, key.shadowStrength, elevation = key.transform.eulerAngles.x }, ambient_after = new { sky = Hex(RenderSettings.ambientSkyColor), equator = Hex(RenderSettings.ambientEquatorColor), ground = Hex(RenderSettings.ambientGroundColor) } });

            // 5. Baked reflection probe per room (no runtime rendering). Participant UI and rig are hidden while baking.
            var hidden = new[] { GameObject.Find("ResearchFlowUI"), GameObject.Find("HandsLab_Quest3") }.Where(g => g && g.activeSelf).ToArray();
            foreach (var g in hidden) g.SetActive(false);
            try
            {
                foreach (string room in Rooms)
                {
                    var floor = Find(room + "_Floor").GetComponent<Renderer>().bounds;
                    var probe = new GameObject("ReflectionProbe_" + room, typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
                    probe.transform.SetParent(root, false);
                    probe.transform.position = new Vector3(floor.center.x, 1.6f, floor.center.z);
                    probe.mode = ReflectionProbeMode.Baked; probe.resolution = 128; probe.boxProjection = false; probe.intensity = 1;
                    probe.size = new Vector3(floor.size.x, 3.4f, floor.size.z); probe.center = new Vector3(0, .1f, 0);
                    string path = AssetRoot + "/ReflectionProbe_" + room + ".exr";
                    Require(Lightmapping.BakeReflectionProbe(probe, path), "Reflection bake failed: " + room);
                    changes.Add(new { probe = room, position = F(probe.transform.position), size = F(probe.size), path });
                }
            }
            finally { foreach (var g in hidden) g.SetActive(true); }

            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Scene save failed.");
            string evidence = Path.GetFullPath("Logs/QuestFinalIntegration/continuation-20260928b/environment-depth-pass.json");
            File.WriteAllText(evidence, JsonConvert.SerializeObject(new { scene = ScenePath, scene_sha256_before = sceneHashBefore, scene_sha256_after = Hash(File.ReadAllBytes(ScenePath)),
                changes, performance = new { new_realtime_lights = 0, new_shadow_casters = 0, post_processing = false, transparent_quads = Grounded.Length, opaque_marking_quads = Workstations.Length * 4,
                reflection_probes_baked = Rooms.Length, probe_resolution = 128 }, hardware_verified = false, utc = DateTime.UtcNow.ToString("O") }, Formatting.Indented));
            return evidence;
        }

        static void Place(Transform root, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(position, rotation); go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // Unlit alpha-blended radial falloff; darkest under the object's centre, soft at the footprint edge.
        static Material ContactMaterial()
        {
            string texturePath = AssetRoot + "/DepthPass_ContactFalloff.png";
            if (!File.Exists(texturePath))
            {
                const int size = 128;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Abs(x + .5f - size / 2f) / (size / 2f), dy = Mathf.Abs(y + .5f - size / 2f) / (size / 2f);
                        // Rounded-rectangle distance so rectangular benches get a rectangular shadow.
                        float d = Mathf.Pow(Mathf.Pow(dx, 4) + Mathf.Pow(dy, 4), .25f);
                        texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - d), 1.6f)));
                    }
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = true; importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            return Material("DepthPass_ContactShadow", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                m.SetColor("_BaseColor", new Color(.03f, .035f, .04f, .62f));
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent - 10;
            });
        }

        static Material Material(string name, string shader, Action<Material> configure)
        {
            string path = AssetRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find(shader)) { name = name }; AssetDatabase.CreateAsset(material, path); }
            configure(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static string Hash(byte[] bytes) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
