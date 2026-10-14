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
    /// <summary>Explicit ResearchFlow-only material, existing-light and visual floor correction.</summary>
    public static class ResearchEnvironmentSimplification
    {
        private const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        private const string MaterialRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Materials/Simplification";
        private const string EvidenceRoot = "Logs/ResearchFlowEvidence/2026-09-22-UXSimplification";

        public static string FixCoplanarApron()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != ScenePath)
                throw new InvalidOperationException("Stable ResearchFlow Edit Mode required.");
            var apron = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).Single(r => r.name == "Suite_Service_Apron");
            if (apron.GetComponentsInChildren<Collider>(true).Length != 0 || apron.GetComponentInParent<Rigidbody>())
                throw new InvalidOperationException("Apron must remain visual-only.");
            float before = apron.bounds.max.y;
            Undo.RecordObject(apron.transform, "Separate coplanar visual floor");
            apron.transform.position += Vector3.up * (-.003f - before);
            if (Mathf.Abs(apron.bounds.max.y + .003f) > .00001f) throw new InvalidOperationException("Apron visual separation failed.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string path = EvidenceRoot + "/floor-separation.json";
            File.WriteAllText(path, JsonConvert.SerializeObject(new { renderer = apron.name, beforeTopY = before, afterTopY = apron.bounds.max.y, collidersChanged = 0, hardware_verified = false }, Formatting.Indented));
            return path;
        }

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stable Edit Mode required.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Dirty loaded scene: " + SceneManager.GetSceneAt(i).path);
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
                throw new InvalidOperationException("ResearchFlow V2 must be the active scene.");
            var roots = scene.GetRootGameObjects();
            var environment = roots.Single(g => g.name == "Environment_V2");
            var detail = roots.SingleOrDefault(g => g.name == "ResearchFlow_EnvironmentSecondPass");
            var renderers = environment.GetComponentsInChildren<Renderer>(true)
                .Concat(detail ? detail.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>()).ToArray();
            var key = roots.SelectMany(g => g.GetComponentsInChildren<Light>(true)).Single(l => l.name == "ResearchFlow_KeyLight");
            if (key.type != LightType.Directional) throw new InvalidOperationException("Expected existing directional key light.");
            var geometry = Geometry(scene);
            var before = Snapshot(scene, renderers, key);
            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
            Directory.CreateDirectory(EvidenceRoot);
            File.WriteAllText(EvidenceRoot + "/environment-" + stamp + "-before.json", JsonConvert.SerializeObject(before, Formatting.Indented));
            var owned = new Dictionary<string, Material>();
            int changed = 0;
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool assigned = false;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var source = materials[slot];
                    if (!source) continue;
                    string role = Role(renderer.name, source.name);
                    if (role == null) continue;
                    if (ColorProperty(source) == null || (!source.HasProperty("_Smoothness") && !source.HasProperty("roughnessFactor") && !source.HasProperty("_Glossiness")))
                        throw new InvalidOperationException("Unsupported environment material: " + source.name + ". Scene not saved.");
                    string keyName = role + "_" + source.GetInstanceID();
                    if (!owned.TryGetValue(keyName, out var material))
                    {
                        if (!AssetDatabase.IsValidFolder(MaterialRoot))
                            AssetDatabase.CreateFolder("Assets/VRMaintenanceResearch/ResearchFlow/Materials", "Simplification");
                        // Clone instead of recoloring imported/shared assets used by other scenes.
                        material = new Material(source) { name = "UXS_" + role };
                        ApplySurface(material, role);
                        string path = AssetDatabase.GenerateUniqueAssetPath(MaterialRoot + "/UXS_" + role + ".mat");
                        AssetDatabase.CreateAsset(material, path);
                        owned.Add(keyName, material);
                    }
                    materials[slot] = material;
                    assigned = true;
                    changed++;
                }
                if (!assigned) continue;
                renderer.sharedMaterials = materials;
                renderer.receiveShadows = true;
                EditorUtility.SetDirty(renderer);
            }
            if (changed == 0) throw new InvalidOperationException("No audited environment material roles matched. Scene not saved.");
            key.color = new Color(.96f, .98f, 1f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .68f;
            key.shadowBias = .015f;
            key.shadowNormalBias = .08f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.51f, .55f, .59f);
            RenderSettings.ambientEquatorColor = new Color(.39f, .43f, .46f);
            RenderSettings.ambientGroundColor = new Color(.25f, .28f, .30f);
            RenderSettings.reflectionIntensity = .38f;
            if (Geometry(scene) != geometry) throw new InvalidOperationException("Geometry or physics changed. Scene not saved.");
            foreach (var material in owned.Values) AssetDatabase.SaveAssetIfDirty(material);
            EditorUtility.SetDirty(key);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("ResearchFlow scene save failed.");
            string evidence = EvidenceRoot + "/environment-" + stamp + "-after.json";
            File.WriteAllText(evidence, JsonConvert.SerializeObject(new
            {
                before, after = Snapshot(scene, renderers, key), material_slots_changed = changed,
                source_assets_modified = false, geometry_and_physics_preserved = true,
                new_lights = 0, new_geometry = 0, input_changes = false,
                actual_pixel_review_required = true, hardware_verified = false, scene_dirty = scene.isDirty
            }, Formatting.Indented));
            return evidence;
        }

        private static string Role(string renderer, string material)
        {
            if (material.StartsWith("UXS_", StringComparison.Ordinal)) return material.Substring(4);
            if (material.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0) return "Glass";
            if (material == "SP_Ceiling" || (renderer.EndsWith("_Ceiling", StringComparison.Ordinal) && material.Contains("Wall"))) return "Ceiling";
            if (material == "SP_Wall" || material.Contains("V2_Wall")) return "Wall";
            if (material == "SP_Floor" || material.Contains("FactoryFloor")) return "Floor";
            return null;
        }

        private static string ColorProperty(Material material) => material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("baseColorFactor") ? "baseColorFactor" : material.HasProperty("_Color") ? "_Color" : null;

        private static void ApplySurface(Material material, string role)
        {
            string property = ColorProperty(material);
            Color old = material.GetColor(property);
            Color color = role == "Wall" ? new Color(.80f, .84f, .85f, old.a)
                : role == "Ceiling" ? new Color(.83f, .86f, .87f, old.a)
                : role == "Floor" ? new Color(.70f, .76f, .79f, old.a) : old;
            if (role != "Glass") material.SetColor(property, property == "baseColorFactor" ? color.gamma : color);
            float roughness = role == "Glass" ? .48f : role == "Floor" ? .84f : .88f;
            if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", roughness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1 - roughness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1 - roughness);
            if (role == "Glass" && material.HasProperty("_SpecularHighlights"))
            {
                material.SetFloat("_SpecularHighlights", 0);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
        }

        private static string Geometry(Scene scene) => string.Join("\n", scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Component>(true))
            .Where(c => c is Transform || c is Collider || c is Rigidbody || c is Joint)
            .Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)));

        private static object Snapshot(Scene scene, Renderer[] renderers, Light key) => new
        {
            scene = scene.path,
            surfaces = renderers.Select(r => new { r.name, r.receiveShadows, materials = r.sharedMaterials.Select(m => !m ? null : new
            {
                m.name, asset = AssetDatabase.GetAssetPath(m), shader = m.shader.name,
                color = ColorProperty(m) == null ? "unavailable" : m.GetColor(ColorProperty(m)).ToString("F3"),
                smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1,
                roughness = m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor") : -1
            }).ToArray() }).ToArray(),
            light = new { key.intensity, color = key.color.ToString("F3"), key.shadowStrength, key.shadowBias, key.shadowNormalBias },
            ambient = new { sky = RenderSettings.ambientSkyColor.ToString("F3"), equator = RenderSettings.ambientEquatorColor.ToString("F3"), ground = RenderSettings.ambientGroundColor.ToString("F3"), RenderSettings.reflectionIntensity }
        };
    }
}
#endif
