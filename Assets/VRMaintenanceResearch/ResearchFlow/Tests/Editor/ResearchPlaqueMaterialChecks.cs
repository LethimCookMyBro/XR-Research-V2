#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Neutral permanent room IDs must keep their text color rather than acquire
    // inherited SDF outline tints or material-local lighting. Editor checks only.
    public static class ResearchPlaqueMaterialChecks
    {
        const string MaterialPath = "Assets/VRMaintenanceResearch/ResearchFlow/Materials/SecondPass/SP_PlaqueText.mat";
        static readonly string[] Outlines = { "_OutlineColor1", "_OutlineColor2", "_OutlineColor3" };

        [MenuItem("VR Research/Research Flow/Check Plaque Material")]
        public static void RunMenu() => Run();

        public static void Run(string outputPath = null)
        {
            var checks = new List<object>();
            var scene = SceneManager.GetActiveScene();
            bool dirtyBefore = scene.isDirty;
            var rootsBefore = scene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(x => x).ToArray();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Material clone = null;
            void Check(bool pass, string name)
            {
                checks.Add(new { pass, name });
                if (!pass) throw new InvalidOperationException("Plaque material check failed: " + name);
            }
            bool passed = false;
            try
            {
                Check(scene.path == "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity" && !Application.isPlaying,
                    "canonical scene in Edit mode");
                Check(material && material.shader && material.shader.name == "TextMeshPro/SRP/TMP_SDF-URP Unlit",
                    "existing depth-tested URP SDF shader retained");
                var labels = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TextMeshPro>(true))
                    .Where(t => t.name == "Permanent neutral lab identification").ToArray();
                Check(labels.Length == 3 && labels.All(t => t.fontSharedMaterial == material),
                    "three actual lab IDs use the corrected material");
                Check(labels.Select(t => t.text).OrderBy(x => x).SequenceEqual(new[] {
                    "01  /  PRACTICE LAB", "02  /  MAINTENANCE LAB", "03  /  MAINTENANCE LAB" }),
                    "neutral identification content preserved");
                foreach (string property in Outlines)
                {
                    Check(material.HasProperty(property), property + " is exposed by the expected shader");
                    Color outline = material.GetColor(property), face = material.GetColor("_FaceColor");
                    Check(Mathf.Abs(outline.r-face.r) < .00001f && Mathf.Abs(outline.g-face.g) < .00001f && Mathf.Abs(outline.b-face.b) < .00001f,
                        property + " has no independent decorative tint");
                }
                Check(material.GetFloat("_Diffuse") == 0 && material.GetFloat("_Ambient") == 1 && material.GetFloat("_SpecularPower") == 0,
                    "material-local light cannot dim or highlight neutral text");
                Check(material.GetVector("_IsoPerimeter") == Vector4.zero && material.GetVector("_Softness") == Vector4.zero,
                    "zero-width zero-softness label style retained");
                Check(labels.All(t => t.font && material.GetTexture("_MainTex") == t.font.atlasTexture),
                    "actual production atlas retained");

                // Exercise regeneration policy on poisoned inherited defaults without touching assets.
                var policy = typeof(ResearchEnvironmentSecondPass).GetMethod("ConfigurePlaqueTextMaterial",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Check(policy != null, "generator owns an explicit neutral-text policy");
                string sourceBefore = EditorJsonUtility.ToJson(material);
                clone = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                clone.SetColor("_FaceColor", new Color(.2f, .4f, .6f, .8f));
                for (int i = 0; i < Outlines.Length; i++) clone.SetColor(Outlines[i], new Color(1, 0, 1, .2f * (i + 1)));
                clone.SetFloat("_Diffuse", .9f); clone.SetFloat("_Ambient", .1f); clone.SetFloat("_SpecularPower", .7f);
                var atlas = clone.GetTexture("_MainTex"); int queue = clone.renderQueue;
                policy.Invoke(null, new object[] { clone });
                for (int i = 0; i < Outlines.Length; i++)
                {
                    var c = clone.GetColor(Outlines[i]);
                    Check(Vector3.Distance(new Vector3(c.r,c.g,c.b), new Vector3(.2f,.4f,.6f)) < .00001f
                        && Mathf.Abs(c.a - .2f * (i+1)) < .00001f, "regeneration preserves face RGB and outline alpha " + i);
                }
                Check(clone.GetColor("_FaceColor") == new Color(.2f,.4f,.6f,.8f) && clone.GetTexture("_MainTex") == atlas
                    && clone.shader == material.shader && clone.renderQueue == queue,
                    "policy preserves face, atlas, shader and depth/queue path");
                Check(clone.GetFloat("_Diffuse") == 0 && clone.GetFloat("_Ambient") == 1 && clone.GetFloat("_SpecularPower") == 0,
                    "regeneration neutralizes inherited lighting");
                string once = EditorJsonUtility.ToJson(clone);
                policy.Invoke(null, new object[] { clone });
                Check(once == EditorJsonUtility.ToJson(clone), "policy is idempotent");
                Check(EditorJsonUtility.ToJson(material) == sourceBefore, "regression fixture leaves the production asset untouched");
                passed = true;
            }
            finally
            {
                if (clone) UnityEngine.Object.DestroyImmediate(clone);
                bool scenePreserved = scene.isDirty == dirtyBefore && rootsBefore.SequenceEqual(scene.GetRootGameObjects()
                    .Select(g => g.GetInstanceID()).OrderBy(x => x));
                if (outputPath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    File.WriteAllText(outputPath, JsonConvert.SerializeObject(new { utc=DateTime.UtcNow.ToString("o"),
                        passed, checks, scenePreserved, scope="Editor material/regeneration checks, not Quest wearer clarity" }, Formatting.Indented));
                }
                if (!scenePreserved) throw new InvalidOperationException("Plaque checks changed the scene");
            }
            Debug.Log("RESEARCH_PLAQUE_MATERIAL_PASS checks=" + checks.Count + "; Editor only");
        }
    }
}
#endif
