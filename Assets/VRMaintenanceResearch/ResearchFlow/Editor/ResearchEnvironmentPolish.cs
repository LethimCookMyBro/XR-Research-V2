#if UNITY_EDITOR
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
    /// <summary>Explicit, integration-only material and existing-light pass. No geometry or input changes.</summary>
    public static class ResearchEnvironmentPolish
    {
        private const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        private const string MaterialRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Materials/VisualPolish";
        private const string EvidenceRoot = "Logs/ResearchFlowEvidence/2026-09-22-VisualPolish";
        private const string MaterialPrefix = "ResearchFlowVP_";

        // Exact nodes/materials audited from the static GLBs and the existing integration scene.
        private static readonly HashSet<string> Walls = new HashSet<string>(StringComparer.Ordinal)
        {
            "Training_Ceiling", "Training_Facade", "Training_RearWall", "Training_WallShell",
            "Computer_Ceiling", "Computer_Facade", "Computer_RearWall",
            "Fan_Ceiling", "Fan_Facade", "Fan_RearWall", "Fan_WallShell",
            "Corridor_Ceiling", "Corridor_East_Portal", "Corridor_West_Portal", "Hall_Suite_Backwall", "Suite_Ceiling_Grid"
        };
        private static readonly HashSet<string> Frames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Training_Door_Frame", "Training_Window_Frame", "Computer_Door_Frame", "Computer_Window_Frame",
            "Fan_Door_Frame", "Fan_Window_Frame", "Corridor_East_Door_Frame", "Corridor_West_Door_Frame",
            "Corridor_Window_Frames", "Factory_Portal_Frames", "Suite_Ceiling_Grid"
        };
        private static readonly HashSet<string> Floors = new HashSet<string>(StringComparer.Ordinal)
        { "Training_Floor", "Computer_Floor", "Fan_Floor", "Corridor_Floor", "Factory_Floor" };
        private static readonly Dictionary<string, string> Timber = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Training_Practice_A"] = "Oak_Export_Training_Practice_A", ["Computer_Main"] = "Oak_Export_Computer_Main",
            ["Computer_Support"] = "Oak_Export_Computer_Support", ["Fan_Main"] = "Oak_Export_Fan_Main",
            ["Fan_Support"] = "Oak_Export_Fan_Support", ["Computer_BenchFrontEdge"] = "TimberEdge",
            ["Fan_ServiceBenchFrontEdge"] = "TimberEdge"
        };
        private static readonly HashSet<string> StaticSources = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/VRMaintenanceResearch/Assets/V2/Environment/01_Training_Static.glb",
            "Assets/VRMaintenanceResearch/Assets/V2/Environment/02_Computer_Static.glb",
            "Assets/VRMaintenanceResearch/Assets/V2/Environment/03_Fan_Static.glb",
            "Assets/VRMaintenanceResearch/Assets/V2/Environment/04_Corridor_Static.glb",
            "Assets/VRMaintenanceResearch/Assets/V2/Environment/05_Factory_Static.glb"
        };

        private sealed class Assignment
        {
            public Renderer renderer;
            public Material source;
            public int slot;
            public string role, sourceName;
        }

        [MenuItem("VR Research/Research Flow/Apply Controlled Environment Polish")]
        public static void ApplyMenu() => Apply();

        public static string Apply()
        {
            var scene = Guard();
            var roots = scene.GetRootGameObjects();
            var environment = roots.Single(g => g.name == "Environment_V2");
            var transforms = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var legacy = transforms.Where(t => t.name == "Training Left Controller Hand" || t.name == "Training Right Controller Hand").Select(PathOf).ToArray();
            if (legacy.Length > 0) throw new InvalidOperationException("Legacy hand candidates require separate hierarchy review: " + string.Join(", ", legacy));
            var handRenderers = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => PathOf(r.transform).IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(r => new { path = PathOf(r.transform), active = r.gameObject.activeInHierarchy, r.enabled, renderer = r.GetType().Name }).ToArray();
            var lights = roots.SelectMany(g => g.GetComponentsInChildren<Light>(false)).Where(l => l.enabled).ToArray();
            if (lights.Length != 1 || lights[0].name != "ResearchFlow_KeyLight" || lights[0].type != LightType.Directional)
                throw new InvalidOperationException("Expected the existing single active ResearchFlow_KeyLight directional light; no light graph is replaced.");
            var light = lights[0];
            var beforeLight = LightState(light);
            var beforeAmbient = AmbientState();
            var beforeGeometry = GeometrySignature(environment);
            var assignments = new List<Assignment>();
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var source = materials[slot];
                    if (source == null) continue;
                    var assetPath = AssetDatabase.GetAssetPath(source);
                    if (!StaticSources.Contains(assetPath) && !assetPath.StartsWith(MaterialRoot + "/", StringComparison.Ordinal)) continue;
                    var sourceName = StaticSources.Contains(assetPath) ? source.name : OwnedSourceName(assetPath);
                    if (sourceName == null) continue;
                    var role = Role(renderer.name, sourceName);
                    if (role == null) continue;
                    ValidateProperties(source);
                    assignments.Add(new Assignment { renderer = renderer, source = source, slot = slot, role = role, sourceName = sourceName });
                }
            }
            if (assignments.Count(a => a.role == "Floor") != 5 || assignments.Count(a => a.role == "Timber") != 7 || assignments.Count(a => a.role == "Wall") < 12)
                throw new InvalidOperationException("Audited static surface mapping differs from the integration scene. Inspect before applying.");

            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory(EvidenceRoot);
            var changes = new List<object>();
            var owned = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var assignment in assignments)
            {
                string safeName = string.Concat(assignment.sourceName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                string path = MaterialRoot + "/" + assignment.role + "_" + safeName + ".mat";
                var before = MaterialState(assignment.source);
                if (!owned.TryGetValue(path, out var material))
                {
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        if (File.Exists(path)) throw new InvalidOperationException("Owned material path contains an unexpected asset: " + path);
                        material = new Material(assignment.source) { name = MaterialPrefix + assignment.sourceName };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    Tune(material, assignment.role, assignment.sourceName);
                    EditorUtility.SetDirty(material);
                    owned.Add(path, material);
                }
                var slots = assignment.renderer.sharedMaterials;
                slots[assignment.slot] = material;
                assignment.renderer.sharedMaterials = slots;
                if (assignment.role == "Floor") assignment.renderer.receiveShadows = true;
                EditorUtility.SetDirty(assignment.renderer);
                changes.Add(new { renderer = PathOf(assignment.renderer.transform), assignment.slot, assignment.role, original_asset = AssetDatabase.GetAssetPath(assignment.source), owned_asset = path, before, after = MaterialState(material) });
            }

            // Existing single directional light and inexpensive ambient fill; no new render effects.
            light.intensity = 1.15f;
            light.color = new Color(1f, .975f, .94f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .63f;
            light.shadowBias = .04f;
            light.shadowNormalBias = .25f;
            EditorUtility.SetDirty(light);
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.65f, .68f, .71f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .49f, .49f);
            RenderSettings.ambientGroundColor = new Color(.26f, .27f, .28f);
            RenderSettings.reflectionIntensity = .55f;
            if (beforeGeometry != GeometrySignature(environment)) throw new InvalidOperationException("Unexpected environment geometry/physics change; do not save.");
            EditorSceneManager.MarkSceneDirty(scene);
            foreach (var material in owned.Values) AssetDatabase.SaveAssetIfDirty(material);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the integration scene after environment polish.");
            string evidence = EvidenceRoot + "/environment-polish-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json";
            File.WriteAllText(evidence, JsonConvert.SerializeObject(new
            {
                scene = ScenePath, changed_material_slots = changes.Count, owned_materials = owned.Keys.ToArray(), changes,
                before_light = beforeLight, after_light = LightState(light), before_ambient = beforeAmbient, after_ambient = AmbientState(),
                legacy_exact_candidates = legacy, hand_renderer_audit = handRenderers, hand_objects_modified = false,
                geometry_and_physics_unchanged = true, new_lights = 0, new_post_effects = 0, source_assets_modified = false,
                color_contract = "glTF baseColorFactor stores intended linear factors converted with Color.gamma, matching the installed glTFast importer. Other Unity color properties retain ordinary SetColor behavior.",
                caveat = "Material/light configuration applied. Actual GameView and Quest visual/performance verification are separate; no hardware claim.",
                scene_dirty = scene.isDirty
            }, Formatting.Indented));
            Debug.Log("RESEARCH_ENVIRONMENT_POLISH_APPLIED " + evidence);
            return evidence;
        }

        private static Scene Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Apply only in stable Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded.isDirty) throw new InvalidOperationException("Preserve/save dirty loaded scene before applying: " + loaded.path);
            }
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath) throw new InvalidOperationException("Open the existing ResearchFlow integration scene; no scene switching is performed.");
            return scene;
        }

        private static string Role(string renderer, string material)
        {
            if (Walls.Contains(renderer) && material == "V2_Wall") return "Wall";
            if (Walls.Contains(renderer) && material == "V2_PaintedMetal") return "Trim";
            if (Frames.Contains(renderer) && material == "V2_Frame") return "Frame";
            if (Floors.Contains(renderer) && material == "FactoryFloor.001") return "Floor";
            if (Timber.TryGetValue(renderer, out var expected) && material == expected) return "Timber";
            return null;
        }

        private static string ColorProperty(Material material) => material.HasProperty("baseColorFactor") ? "baseColorFactor" : material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;
        private static string OwnedSourceName(string assetPath)
        {
            // CreateAsset names materials after their files. Resolve the exact audited names on reapply.
            switch (Path.GetFileNameWithoutExtension(assetPath))
            {
                case "Wall_V2_Wall": return "V2_Wall";
                case "Trim_V2_PaintedMetal": return "V2_PaintedMetal";
                case "Frame_V2_Frame": return "V2_Frame";
                case "Floor_FactoryFloor_001": return "FactoryFloor.001";
                case "Timber_TimberEdge": return "TimberEdge";
                case "Timber_Oak_Export_Training_Practice_A": return "Oak_Export_Training_Practice_A";
                case "Timber_Oak_Export_Computer_Main": return "Oak_Export_Computer_Main";
                case "Timber_Oak_Export_Computer_Support": return "Oak_Export_Computer_Support";
                case "Timber_Oak_Export_Fan_Main": return "Oak_Export_Fan_Main";
                case "Timber_Oak_Export_Fan_Support": return "Oak_Export_Fan_Support";
                default: return null;
            }
        }
        private static void ValidateProperties(Material material)
        {
            if (ColorProperty(material) == null || (!material.HasProperty("roughnessFactor") && !material.HasProperty("_Smoothness") && !material.HasProperty("_Glossiness")))
                throw new InvalidOperationException("Unsupported audited surface shader: " + material.name + " / " + material.shader.name);
        }

        private static void Tune(Material material, string role, string name)
        {
            Color color; float roughness, metallic;
            switch (role)
            {
                case "Wall": color = new Color(.78f, .77f, .73f); roughness = .78f; metallic = 0; break;
                case "Trim": color = new Color(.115f, .15f, .17f); roughness = .56f; metallic = .25f; break;
                case "Frame": color = new Color(.07f, .085f, .095f); roughness = .48f; metallic = .55f; break;
                case "Floor": color = new Color(.32f, .335f, .33f); roughness = .68f; metallic = .02f; break;
                default: color = name == "TimberEdge" ? new Color(.30f, .18f, .085f) : new Color(1f, .96f, .89f); roughness = .61f; metallic = 0; break;
            }
            var colorProperty = ColorProperty(material);
            // glTFast ShaderGraphMaterialGenerator explicitly stores baseColorLinear.gamma.
            // Assigning linear factors directly here darkens walls and crushes steel trim to black.
            var storedColor = colorProperty == "baseColorFactor" ? color.gamma : color;
            material.SetColor(colorProperty, storedColor);
            if (Vector4.Distance(material.GetColor(colorProperty), storedColor) > .002f)
                throw new InvalidOperationException("Material color contract failed: " + material.name);
            if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", roughness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1 - roughness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1 - roughness);
            if (material.HasProperty("metallicFactor")) material.SetFloat("metallicFactor", metallic);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        }

        private static string PathOf(Transform transform) => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
        private static object MaterialState(Material material)
        {
            string color = ColorProperty(material);
            return new { material.name, shader = material.shader.name, color_property = color, color = color == null ? "unavailable" : material.GetColor(color).ToString("F3"), gltf_linear_factor = color == "baseColorFactor" ? material.GetColor(color).linear.ToString("F3") : "not_glTF", roughness = material.HasProperty("roughnessFactor") ? material.GetFloat("roughnessFactor") : material.HasProperty("_Smoothness") ? 1 - material.GetFloat("_Smoothness") : 1 - material.GetFloat("_Glossiness"), texture = material.mainTexture != null ? AssetDatabase.GetAssetPath(material.mainTexture) : "" };
        }
        private static object LightState(Light light) => new { path = PathOf(light.transform), light.intensity, color = light.color.ToString("F3"), shadows = light.shadows.ToString(), light.shadowStrength, light.shadowBias, light.shadowNormalBias };
        private static object AmbientState() => new { mode = RenderSettings.ambientMode.ToString(), sky = RenderSettings.ambientSkyColor.ToString("F3"), equator = RenderSettings.ambientEquatorColor.ToString("F3"), ground = RenderSettings.ambientGroundColor.ToString("F3"), RenderSettings.reflectionIntensity };
        private static string GeometrySignature(GameObject environment)
        {
            var transforms = environment.GetComponentsInChildren<Transform>(true).Select(t => PathOf(t) + ":" + t.localPosition.ToString("F6") + ":" + t.localRotation.ToString("F6") + ":" + t.localScale.ToString("F6"));
            var meshes = environment.GetComponentsInChildren<MeshFilter>(true).Select(m => PathOf(m.transform) + ":mesh:" + (m.sharedMesh != null ? m.sharedMesh.GetInstanceID() : 0));
            var physics = environment.GetComponentsInChildren<Component>(true).Where(c => c is Collider || c is Rigidbody || c is Joint).Select(c => PathOf(c.transform) + ":" + c.GetType().Name + ":" + EditorJsonUtility.ToJson(c));
            return string.Join("|", transforms.Concat(meshes).Concat(physics));
        }
    }
}
#endif
