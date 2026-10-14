#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Integration-scene-only visual pass. Existing task objects, meshes and physics remain intact.</summary>
    public static class ResearchEnvironmentSecondPass
    {
        private const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        private const string MaterialRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Materials/SecondPass";
        private const string TextureRoot = "Assets/VRMaintenanceResearch/Assets/V2/Environment/textures/";
        private const string DetailName = "ResearchFlow_EnvironmentSecondPass";
        private const string EvidenceRoot = "Logs/ResearchFlowEvidence/2026-09-22-UXSecondPass";

        [MenuItem("VR Research/Research Flow/Apply Environment Second Pass")]
        public static void ApplyMenu() => Apply();

        public static string Apply()
        {
            var scene = Guard();
            var roots = scene.GetRootGameObjects();
            var environment = roots.Single(g => g.name == "Environment_V2");
            var renderers = environment.GetComponentsInChildren<Renderer>(true);
            var key = roots.SelectMany(g => g.GetComponentsInChildren<Light>(true))
                .Single(l => l.name == "ResearchFlow_KeyLight" && l.type == LightType.Directional);
            var before = Snapshot(scene, environment, key);
            var physics = PhysicsSignature(scene);
            var transforms = TransformSignature(environment);
            var benches = new[] { "Training_Practice_A", "Computer_Main", "Fan_Main" }
                .Select(n => renderers.Single(r => r.name == n)).ToArray();
            var ceilings = new[] { "Training_Ceiling", "Computer_Ceiling", "Fan_Ceiling" }
                .Select(n => renderers.Single(r => r.name == n)).ToArray();
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                throw new InvalidOperationException("Installed URP Lit shader is required; no pipeline changes are performed.");
            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory(EvidenceRoot);
            var palette = Materials();
            int slotsChanged = 0;
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string path = AssetDatabase.GetAssetPath(slots[i]);
                    if (!path.StartsWith("Assets/VRMaintenanceResearch/Assets/V2/Environment/", StringComparison.Ordinal)
                        && !path.StartsWith("Assets/VRMaintenanceResearch/ResearchFlow/Materials/", StringComparison.Ordinal)) continue;
                    string role = Role(renderer.name, slots[i].name);
                    if (role == null) continue;
                    slots[i] = palette[role]; changed = true; slotsChanged++;
                }
                if (changed) { renderer.sharedMaterials = slots; renderer.receiveShadows = true; EditorUtility.SetDirty(renderer); }
                // ponytail: one shadowed key approximates indoor key lighting; bake lightmaps if headset profiling requires it.
                // The outer roof and 7.14m-high support frames sit above the lab ceilings.
                // Their daylight shadows cannot represent the lab's overhead luminaires.
                if (renderer.name.EndsWith("_Ceiling", StringComparison.Ordinal)
                    || renderer.name == "Suite_Ceiling_Grid" || renderer.name == "Factory_Roof"
                    || renderer.name == "Hall_Suite_SupportBay" || renderer.name == "Hall_Suite_SupportBay_Linked")
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            key.transform.rotation = Quaternion.Euler(82f, -32f, 0f);
            key.color = new Color(1f, .955f, .87f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .52f;
            key.shadowBias = .025f;
            key.shadowNormalBias = .12f;
            RenderSettings.sun = key;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.53f, .55f, .56f);
            RenderSettings.ambientEquatorColor = new Color(.41f, .405f, .38f);
            RenderSettings.ambientGroundColor = new Color(.38f, .375f, .36f);
            RenderSettings.reflectionIntensity = .7f;

            // Reapply replaces only this pass's named decorative root. It never removes imported props.
            var previous = roots.SingleOrDefault(g => g.name == DetailName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var detail = new GameObject(DetailName);
            SceneManager.MoveGameObjectToScene(detail, scene);
            for (int i = 0; i < benches.Length; i++)
            {
                string room = new[] { "Training", "Computer", "Fan" }[i];
                AddRoomDetail(detail.transform, benches[i], ceilings[i], palette, i != 0);
                AddVisibleDetail(detail.transform, renderers.Single(r => r.name == room + "_Window_Frame"),
                    renderers.Single(r => r.name == room + "_Floor"), palette, i);
            }
            AddStoredBoard(detail.transform, renderers.Single(r => r.name == "Computer_Support"));
            AddFacadeBaseCloser(detail.transform, renderers);

            if (physics != PhysicsSignature(scene) || transforms != TransformSignature(environment))
                throw new InvalidOperationException("Existing transforms or physics changed. Do not save this scene.");
            if (detail.GetComponentsInChildren<Collider>(true).Length != 0
                || detail.GetComponentsInChildren<Light>(true).Length != 3
                || detail.GetComponentsInChildren<Light>(true).Any(l => l.shadows != LightShadows.None))
                throw new InvalidOperationException("Decoration budget/physics validation failed. Do not save this scene.");
            if (detail.GetComponentsInChildren<TextMesh>(true).Length != 0
                || detail.GetComponentsInChildren<TextMeshPro>(true).Length != 3
                || detail.GetComponentsInChildren<TextMeshPro>(true).Any(t => t.fontSharedMaterial.shader != palette["PlaqueText"].shader))
                throw new InvalidOperationException("All three lab plaques must use the depth-tested world SDF shader.");
            foreach (var material in palette.Values) AssetDatabase.SaveAssetIfDirty(material);
            EditorUtility.SetDirty(key);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save ResearchFlow integration scene.");
            string evidence = EvidenceRoot + "/environment-second-pass-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json";
            File.WriteAllText(evidence, JsonConvert.SerializeObject(new
            {
                before, after = Snapshot(scene, environment, key), slots_changed = slotsChanged,
                detail_renderers = detail.GetComponentsInChildren<Renderer>().Length,
                existing_transforms_and_physics_unchanged = true, added_shadowless_spot_lights = 3,
                source_assets_modified = false, task_logic_modified = false, post_effects_added = false,
                actual_pixel_review_required = true, hardware_verified = false, scene_dirty = scene.isDirty
            }, Formatting.Indented));
            Debug.Log("RESEARCH_ENVIRONMENT_SECOND_PASS_APPLIED " + evidence);
            return evidence;
        }

        private static Dictionary<string, Material> Materials()
        {
            var result = new Dictionary<string, Material>();
            result["Wall"] = Material("Wall", new Color(.79f, .77f, .71f), 0, .16f);
            result["Ceiling"] = Material("Ceiling", new Color(.71f, .73f, .73f), .02f, .16f);
            result["Floor"] = Material("Floor", new Color(.9f, .94f, .96f), .04f, .34f, "V2_Floor_BaseColor_1K.png");
            result["Timber"] = Material("Timber", new Color(1f, .94f, .84f), 0, .32f, "V2_Wood_BaseColor_1K.png");
            result["TimberEdge"] = Material("TimberEdge", new Color(.32f, .20f, .10f), 0, .28f);
            result["Steel"] = Material("Steel", new Color(.49f, .53f, .56f), .7f, .46f);
            result["Frame"] = Material("Frame", new Color(.12f, .16f, .19f), .42f, .3f);
            result["Paint"] = Material("Paint", new Color(.20f, .28f, .32f), .18f, .25f);
            result["Blue"] = Material("Blue", new Color(.14f, .24f, .31f), .15f, .3f);
            result["Rubber"] = Material("Rubber", new Color(.08f, .11f, .12f), 0, .12f);
            result["Bin"] = Material("Bin", new Color(.17f, .25f, .29f), 0, .24f);
            result["Trim"] = Material("Trim", new Color(.53f, .43f, .25f), .22f, .35f);
            result["Seam"] = Material("Seam", new Color(.26f, .30f, .30f), 0, .1f);
            result["Diffuser"] = Material("Diffuser", new Color(.97f, .97f, .88f), 0, .15f);
            result["Diffuser"].EnableKeyword("_EMISSION");
            result["Diffuser"].SetColor("_EmissionColor", new Color(1.25f, 1.23f, 1.1f));
            EditorUtility.SetDirty(result["Diffuser"]);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_English.asset");
            var textShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-URP Unlit.shadergraph");
            if (font == null || textShader == null) throw new InvalidOperationException("Existing English font and depth-tested URP SDF shader are required.");
            string textPath = MaterialRoot + "/SP_PlaqueText.mat";
            var textMaterial = AssetDatabase.LoadAssetAtPath<Material>(textPath);
            if (textMaterial == null)
            {
                textMaterial = new Material(font.material);
                AssetDatabase.CreateAsset(textMaterial, textPath);
            }
            // Installed URP Unlit graph fixes ZTest to LEqual (m_ZTestMode: 4), unlike legacy GUI/Text Shader.
            textMaterial.shader = textShader;
            textMaterial.SetTexture("_MainTex", font.atlasTexture);
            ConfigurePlaqueTextMaterial(textMaterial);
            EditorUtility.SetDirty(textMaterial);
            result["PlaqueText"] = textMaterial;
            return result;
        }

        // This URP SDF graph always composites its three outline layers and local
        // lighting, even when the legacy _OutlineWidth is zero. Keep permanent
        // room IDs at their authored face/vertex tint instead of inherited effects.
        internal static void ConfigurePlaqueTextMaterial(Material material)
        {
            var face = material.GetColor("_FaceColor");
            foreach (var property in new[] { "_OutlineColor1", "_OutlineColor2", "_OutlineColor3" })
            {
                var outline = material.GetColor(property);
                material.SetColor(property, new Color(face.r, face.g, face.b, outline.a));
            }
            material.SetFloat("_Diffuse", 0);
            material.SetFloat("_Ambient", 1);
            material.SetFloat("_SpecularPower", 0);
        }

        private static Material Material(string role, Color color, float metallic, float smoothness, string texture = null)
        {
            string path = MaterialRoot + "/SP_" + role + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                if (File.Exists(path)) throw new InvalidOperationException("Unexpected owned material asset: " + path);
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.enableInstancing = true;
            if (texture != null)
            {
                var map = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + texture);
                if (map == null) throw new FileNotFoundException("Existing environment texture is missing: " + texture);
                material.SetTexture("_BaseMap", map);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string Role(string renderer, string material)
        {
            if (material.StartsWith("SP_", StringComparison.Ordinal)) return material.Substring(3);
            if (renderer.EndsWith("_Ceiling", StringComparison.Ordinal) && material.Contains("Wall")) return "Ceiling";
            if (material.Contains("FactoryFloor")) return "Floor";
            if (material.Contains("Oak_Export")) return "Timber";
            if (material.Contains("TimberEdge")) return "TimberEdge";
            if (material.Contains("V2_Wall")) return "Wall";
            if (material.Contains("V2_Frame")) return "Frame";
            if (material.Contains("V2_PaintedMetal")) return "Paint";
            if (material.Contains("BlueCabinet")) return "Blue";
            if (material.Contains("V2_RawMetal") || material.StartsWith("Steel", StringComparison.Ordinal)
                || material.StartsWith("Aluminium", StringComparison.Ordinal)) return "Steel";
            if (material.StartsWith("Rubber", StringComparison.Ordinal)) return "Rubber";
            if (material.Contains("V2_Tote")) return "Bin";
            return null;
        }

        private static void AddRoomDetail(Transform parent, Renderer bench, Renderer ceiling,
            Dictionary<string, Material> materials, bool taskRoom)
        {
            var group = new GameObject(bench.name + "_ArchitecturalDetail").transform;
            group.SetParent(parent, false);
            var top = bench.bounds;
            float y = ceiling.bounds.min.y - .22f;
            for (int i = -1; i <= 1; i += 2)
            {
                var center = new Vector3(top.center.x + i * 1.25f, y, top.center.z);
                Box(group, "Pendant housing", center, new Vector3(.18f, .09f, 2.15f), materials["Frame"]);
                Box(group, "Warm white diffuser", center + Vector3.down * .052f,
                    new Vector3(.13f, .012f, 2.04f), materials["Diffuser"]);
                for (int end = -1; end <= 1; end += 2)
                    Box(group, "Suspension", center + new Vector3(0, .1f, end * .83f),
                        new Vector3(.013f, .2f, .013f), materials["Steel"]);
            }
            float trayZ = top.center.z + 1.9f;
            float trayWidth = Mathf.Min(ceiling.bounds.size.x - 1.1f, 7.5f);
            for (int side = -1; side <= 1; side += 2)
                Box(group, "Ceiling service tray rail", new Vector3(top.center.x, y + .09f, trayZ + side * .15f),
                    new Vector3(trayWidth, .075f, .025f), materials["Steel"]);
            for (int i = 0; i < 9; i++)
                Box(group, "Service tray crossbar", new Vector3(top.center.x - trayWidth * .5f + trayWidth * i / 8f, y + .065f, trayZ),
                    new Vector3(.025f, .025f, .31f), materials["Steel"]);
            var fill = new GameObject("Ceiling pool light").AddComponent<Light>();
            fill.transform.SetParent(group, false);
            fill.transform.position = new Vector3(top.center.x, y - .1f, top.center.z - .6f);
            // The participant sees the interior facade facing +Z. Downward-only pools never reached it.
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(top.center.x, 1.8f, .12f) - fill.transform.position);
            fill.type = LightType.Spot; fill.spotAngle = 120; fill.innerSpotAngle = 70;
            fill.range = 7.5f; fill.intensity = 6.5f; fill.color = new Color(1f, .96f, .88f);
            fill.shadows = LightShadows.None;

            // Bench edges stay clear. Neutral parts trays sit outside the existing central work mat.
            if (taskRoom)
            {
                float width = .27f, depth = .21f;
                var center = new Vector3(top.max.x - .22f, top.max.y + .018f, top.max.z - .17f);
                Box(group, "Empty parts tray", center, new Vector3(width, .018f, depth), materials["Steel"]);
                for (int side = -1; side <= 1; side += 2)
                {
                    Box(group, "Tray rim", center + new Vector3(side * width / 2, .025f, 0), new Vector3(.012f, .05f, depth), materials["Steel"]);
                    Box(group, "Tray rim", center + new Vector3(0, .025f, side * depth / 2), new Vector3(width, .05f, .012f), materials["Steel"]);
                }
            }
        }

        private static void AddVisibleDetail(Transform parent, Renderer window, Renderer floor,
            Dictionary<string, Material> materials, int room)
        {
            var group = new GameObject(window.name + "_InteriorDetails").transform;
            group.SetParent(parent, false);
            var w = window.bounds;
            float z = w.max.z + .018f;
            Box(group, "Brushed sill trim", new Vector3(w.center.x, w.min.y - .04f, z),
                new Vector3(w.size.x, .045f, .035f), materials["Trim"]);
            var plaque = new Vector3(w.center.x, w.max.y + .28f, z);
            Box(group, "Wall mounted lab plaque", plaque, new Vector3(2.5f, .31f, .035f), materials["Blue"]);
            Box(group, "Plaque rule", plaque + new Vector3(0, -.112f, .025f), new Vector3(2.26f, .012f, .008f), materials["Trim"]);
            var label = new GameObject("Permanent neutral lab identification").AddComponent<TextMeshPro>();
            label.transform.SetParent(group, false);
            label.transform.position = plaque + new Vector3(0, .012f, .025f);
            label.transform.rotation = Quaternion.Euler(0, 180, 0);
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/ResearchFlow_English.asset");
            label.fontSharedMaterial = materials["PlaqueText"];
            label.rectTransform.sizeDelta = new Vector2(2.25f, .19f);
            label.enableAutoSizing = true; label.fontSizeMin = .5f; label.fontSizeMax = 2f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center; label.color = new Color(.92f, .94f, .93f);
            label.text = room == 0 ? "01  /  PRACTICE LAB" : room == 1 ? "02  /  MAINTENANCE LAB" : "03  /  MAINTENANCE LAB";
            label.ForceMeshUpdate();
            var textRenderer = label.GetComponent<MeshRenderer>();
            textRenderer.shadowCastingMode = ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
            var outlet = new Vector3(w.min.x + .48f, .48f, z);
            Box(group, "Service outlet plate", outlet, new Vector3(.17f, .1f, .025f), materials["Wall"]);
            for (int i = -1; i <= 1; i += 2)
                Box(group, "Outlet recess", outlet + new Vector3(i * .045f, 0, .014f),
                    new Vector3(.032f, .043f, .006f), materials["Frame"]);
            // No floor seams: 9 mm lines on the floor are under 1 px beyond ~3 m on Quest 3 and broke into
            // dashes in the headset (wear-20260924_1516). The existing scene seams are kept inactive.
        }

        private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }

        private static void AddStoredBoard(Transform parent, Renderer support)
        {
            const string path = "Assets/VRMaintenanceResearch/Assets/Motherboard_Quest_Optimized.glb";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new FileNotFoundException("Existing neutral motherboard display asset unavailable: " + path);
            var filter = source.GetComponentInChildren<MeshFilter>(true);
            var renderer = filter == null ? null : filter.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Expected imported motherboard mesh/materials were not found.");
            // Reuse only the existing 18,856-triangle mesh and materials; no task scripts, input or colliders.
            var board = new GameObject("Organized spare board on support bench", typeof(MeshFilter), typeof(MeshRenderer));
            board.transform.SetParent(parent, false);
            board.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var display = board.GetComponent<MeshRenderer>();
            display.sharedMaterials = renderer.sharedMaterials;
            board.transform.rotation = Quaternion.Euler(0, 90, 0);
            var bounds = display.bounds;
            // P9: near bench end, 2 cm in. The old centre -.25 m cut 22 mm into Computer_ComponentTray (z 4.18+).
            board.transform.position += new Vector3(support.bounds.center.x - bounds.center.x,
                support.bounds.max.y + .004f - bounds.min.y, support.bounds.min.z + .02f + bounds.extents.z - bounds.center.z);
            board.isStatic = true;
        }

        // P9: the facades stand on the corridor floor edge (z -0.2) and Computer/Fan lift their corridor face 8 mm, so the
        // corridor looked through the wall base into the void under the walls (a floor-level line that dashes with distance).
        // Fill the wall footprint between the door thresholds, which already fill it inside the doorways.
        public static void AddFacadeBaseCloser(Transform parent, Renderer[] renderers)
        {
            var floor = renderers.Single(r => r.name == "Corridor_Floor");
            var b = floor.bounds;
            float x = b.min.x;
            foreach (var door in renderers.Where(r => r.name.EndsWith("_Door_Threshold", StringComparison.Ordinal)).Select(r => r.bounds)
                .OrderBy(d => d.min.x).Append(new Bounds(new Vector3(b.max.x, 0, 0), Vector3.zero)))
            {
                Box(parent, "Facade base closer", new Vector3((x + door.min.x) / 2, b.center.y, b.max.z + .1f),
                    new Vector3(door.min.x - x, b.size.y, .2f), floor.sharedMaterial);
                x = door.max.x;
            }
            // The corridor landings stop at x ±15.9 but Factory_Perimeter reaches ±16.2; end-wall slits showed that gap.
            var perimeter = renderers.Single(r => r.name == "Factory_Perimeter").bounds;
            foreach (var landing in renderers.Where(r => r.name.StartsWith("Corridor_", StringComparison.Ordinal) && r.name.EndsWith("_Landing", StringComparison.Ordinal)).Select(r => r.bounds))
            {
                float edge = landing.center.x > 0 ? landing.max.x : landing.min.x, wall = landing.center.x > 0 ? perimeter.max.x : perimeter.min.x;
                Box(parent, "Landing end closer", new Vector3((edge + wall) / 2, landing.center.y, landing.center.z),
                    new Vector3(Mathf.Abs(wall - edge), landing.size.y, landing.size.z), floor.sharedMaterial);
            }
        }

        private static Scene Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Apply only after Unity has settled in Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve/save all loaded dirty scenes before applying.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
                throw new InvalidOperationException("Open the existing ResearchFlow integration scene first.");
            return scene;
        }

        private static object Snapshot(Scene scene, GameObject environment, Light key) => new
        {
            scene = scene.path,
            renderers = environment.GetComponentsInChildren<Renderer>(true).Select(r => new
            {
                r.name, bounds = r.bounds.ToString(), cast_shadows = r.shadowCastingMode.ToString(), r.receiveShadows,
                materials = r.sharedMaterials.Select(m => m == null ? null : new { m.name, shader = m.shader.name, asset = AssetDatabase.GetAssetPath(m) }).ToArray()
            }).ToArray(),
            key = new { rotation = key.transform.eulerAngles.ToString(), key.intensity, shadows = key.shadows.ToString(), key.shadowStrength },
            ambient = new { sky = RenderSettings.ambientSkyColor.ToString(), equator = RenderSettings.ambientEquatorColor.ToString(), ground = RenderSettings.ambientGroundColor.ToString() },
            reflection_probes = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<ReflectionProbe>(true).Length),
            light_probe_groups = scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<LightProbeGroup>(true).Length)
        };

        private static string TransformSignature(GameObject environment) => string.Join("\n", environment.GetComponentsInChildren<Transform>(true)
            .Select(t => t.GetInstanceID() + ":" + t.localToWorldMatrix.ToString("R")));

        private static string PhysicsSignature(Scene scene) => string.Join("\n", scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Component>(true)).Where(c => c is Collider || c is Rigidbody)
            .Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)));
    }
}
#endif
