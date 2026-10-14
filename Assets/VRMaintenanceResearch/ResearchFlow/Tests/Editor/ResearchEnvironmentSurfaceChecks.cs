#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Actual asset and owned-copy policy regressions. Editor evidence, not Quest optical acceptance.
    public static class ResearchEnvironmentSurfaceChecks
    {
        static readonly string[] Roles = { "UXS_Floor", "UXS_Wall", "SP_Paint", "SP_Timber" };
        static readonly string[] Paths = {
            "Assets/VRMaintenanceResearch/ResearchFlow/Materials/Simplification/UXS_Floor.mat",
            "Assets/VRMaintenanceResearch/ResearchFlow/Materials/Simplification/UXS_Wall.mat",
            "Assets/VRMaintenanceResearch/ResearchFlow/Materials/SecondPass/SP_Paint.mat",
            "Assets/VRMaintenanceResearch/ResearchFlow/Materials/SecondPass/SP_Timber.mat" };
        static readonly string[] SurfaceProperties = { "_Surface", "_Blend", "_AlphaClip", "_Cutoff", "_Cull", "_ZWrite",
            "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_AlphaToMask" };

        [MenuItem("VR Research/Research Flow/Check Environment Surfaces")]
        public static void RunMenu() => Run();

        public static void Run(string outputPath = null, bool verifyProduction = true)
        {
            var checks = new List<object>();
            void Check(bool pass, string name)
            {
                checks.Add(new { pass, name });
                if (!pass) throw new InvalidOperationException("Environment surface check failed: " + name);
            }
            bool Near(float a, float b) => Mathf.Abs(a - b) < .00001f;
            void Rgb(Material m, Vector3 expected, string label)
            {
                var c = m.GetColor("_BaseColor");
                Check(Vector3.Distance(new Vector3(c.r, c.g, c.b), expected) < .00001f, label);
            }
            void Surface(Material m, string role)
            {
                Check(m && m.shader && m.shader.name == "Universal Render Pipeline/Lit", role + " uses installed URP Lit");
                if (role == "UXS_Floor")
                {
                    Check(m.GetTexture("_BaseMap") == null, "floor avoids full-strength aggregate albedo");
                    Check(m.GetTexture("_DetailAlbedoMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(ResearchEnvironmentSurfaceProfile.FloorDetailPath),
                        "floor uses the attributed detail asset");
                    Check(m.GetTextureScale("_BaseMap") == Vector2.one / 2.5f && m.GetTextureOffset("_BaseMap") == Vector2.zero
                        && m.GetTextureScale("_DetailAlbedoMap") == Vector2.one && m.GetTextureOffset("_DetailAlbedoMap") == Vector2.zero,
                        "combined URP base/detail tiling is 2.5 metres per repeat on one-metre authored UVs");
                    Check(Near(m.GetFloat("_DetailAlbedoMapScale"), .12f) && m.IsKeywordEnabled("_DETAIL_SCALED")
                        && !m.IsKeywordEnabled("_DETAIL_MULX2"), "detail is scaled rather than uncontrolled multiply-by-two");
                    Check(m.GetTexture("_DetailMask") == null && m.GetTexture("_DetailNormalMap") == null
                        && m.GetFloat("_DetailNormalMapScale") == 0, "no inherited mask or detail-normal perturbation");
                    Check(m.GetTexture("_BumpMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(ResearchEnvironmentSurfaceProfile.FloorNormalPath)
                        && m.IsKeywordEnabled("_NORMALMAP") && Near(m.GetFloat("_BumpScale"), .18f), "floor uses restrained new tangent normal");
                    Check(Near(m.GetFloat("_Smoothness"), .28f) && m.GetFloat("_Metallic") == 0
                        && m.GetTexture("_MetallicGlossMap") == null && m.GetTexture("_OcclusionMap") == null
                        && !m.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && !m.IsKeywordEnabled("_OCCLUSIONMAP"),
                        "floor gloss is independent of old material masks");
                    Rgb(m, new Vector3(.59f, .61f, .61f), "floor neutral epoxy tint");
                }
                else if (role == "UXS_Wall")
                {
                    Check(m.GetTexture("_BaseMap") == null && Near(m.GetFloat("_BumpScale"), .15f), "wall removes coarse albedo and reduces surface relief");
                    Rgb(m, new Vector3(.92f, .92f, .90f), "wall neutral pale tint");
                }
                else if (role == "SP_Paint")
                {
                    Check(m.GetTexture("_BaseMap") == null && Near(m.GetFloat("_BumpScale"), .12f)
                        && Near(m.GetFloat("_Smoothness"), .35f), "paint removes imported panel seams with restrained relief and gloss");
                    Rgb(m, new Vector3(.18f, .28f, .30f), "paint dark teal tint");
                }
                else Check(Near(m.GetFloat("_BumpScale"), .28f), "timber surface relief is restrained");
            }

            var scene = SceneManager.GetActiveScene();
            bool dirty = scene.isDirty, passed = false;
            var roots = scene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(x => x).ToArray();
            var sources = Paths.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            var sourceStates = sources.Where(m => m).ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
            var owned = new List<Material>();
            try
            {
                Check(scene.path == "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity" && !Application.isPlaying,
                    "canonical scene in Edit mode");
                Check(sources.All(m => m), "four exact production material assets exist");
                if (verifyProduction)
                    for (int i = 0; i < Roles.Length; i++) Surface(sources[i], Roles[i]);
                foreach (string path in new[] { ResearchEnvironmentSurfaceProfile.FloorDetailPath, ResearchEnvironmentSurfaceProfile.FloorNormalPath })
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    Check(importer != null && !importer.sRGBTexture && importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear
                        && importer.wrapMode == TextureWrapMode.Repeat, "floor detail/normal import is linear, mipmapped and repeating: " + path);
                }
                var env = scene.GetRootGameObjects().Single(g => g.name == "Environment_V2").transform;
                foreach (string floor in new[] { "Training/Training_Floor", "Computer/Computer_Floor", "Fan/Fan_Floor", "Corridor/Corridor_Floor", "Factory_Static/Factory_Floor" })
                {
                    var transform = env.Find(floor);
                    var filter = transform ? transform.GetComponent<MeshFilter>() : null;
                    Check(filter && Near(ResearchVisualQualityPass.MetresPerUv(filter.sharedMesh, transform), 1),
                        floor + " actual mesh retains one world metre per UV unit");
                }

                for (int i = 0; i < Roles.Length; i++)
                {
                    var copy = new Material(sources[i]) { hideFlags = HideFlags.HideAndDontSave };
                    owned.Add(copy);
                    var color = copy.GetColor("_BaseColor"); color.a = .37f; copy.SetColor("_BaseColor", color);
                    copy.SetFloat("_BumpScale", .97f); copy.renderQueue = 2147; copy.enableInstancing = i % 2 == 0;
                    copy.SetFloat("_Surface", 1); copy.SetFloat("_AlphaClip", 1);
                    copy.SetColor("_EmissionColor", new Color(.02f, .04f, .06f, .8f)); copy.EnableKeyword("_EMISSION");
                    var floats = SurfaceProperties.ToDictionary(p => p, copy.GetFloat);
                    var shader = copy.shader; var emission = copy.GetColor("_EmissionColor");
                    bool instancing = copy.enableInstancing;
                    var timberMap = copy.GetTexture("_BaseMap"); var timberColor = copy.GetColor("_BaseColor");
                    ResearchEnvironmentSurfaceProfile.Configure(copy, Roles[i]);
                    Surface(copy, Roles[i]);
                    Check(copy.shader == shader && Near(copy.GetColor("_BaseColor").a, .37f) && copy.renderQueue == 2147
                        && copy.enableInstancing == instancing && copy.GetColor("_EmissionColor") == emission && copy.IsKeywordEnabled("_EMISSION")
                        && floats.All(pair => copy.GetFloat(pair.Key) == pair.Value), Roles[i] + " preserves shader, alpha, surface state, queue, emission and instancing");
                    if (Roles[i] == "SP_Timber") Check(copy.GetTexture("_BaseMap") == timberMap && copy.GetColor("_BaseColor") == timberColor,
                        "timber keeps its existing grain texture and tint");
                    string once = EditorJsonUtility.ToJson(copy);
                    ResearchEnvironmentSurfaceProfile.Configure(copy, Roles[i]);
                    Check(once == EditorJsonUtility.ToJson(copy), Roles[i] + " configuration is idempotent");
                }

                void Reject(Material material, string role, string label)
                {
                    string before = EditorJsonUtility.ToJson(material); bool rejected = false;
                    try { ResearchEnvironmentSurfaceProfile.Configure(material, role); }
                    catch (ArgumentException) { rejected = true; }
                    catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && EditorJsonUtility.ToJson(material) == before, label + " rejected without partial material mutation");
                }
                Reject(owned[0], "UXS_Ceiling", "out-of-scope ceiling role");
                Reject(owned[0], null, "missing role");
                Reject(sources[0], "UXS_Floor", "persistent production asset");
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                Check(unlit != null, "installed unlit shader available for rejection fixture");
                var wrong = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(wrong);
                Reject(wrong, "UXS_Floor", "unsupported shader");
                passed = true;
            }
            finally
            {
                foreach (var material in owned) if (material) UnityEngine.Object.DestroyImmediate(material);
                bool assetsPreserved = sourceStates.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value);
                bool scenePreserved = SceneManager.GetActiveScene() == scene && scene.isDirty == dirty
                    && roots.SequenceEqual(scene.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(x => x));
                if (outputPath != null)
                {
                    string directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(outputPath, JsonConvert.SerializeObject(new { utc = DateTime.UtcNow.ToString("o"), passed, verifyProduction, checks,
                        assetsPreserved, scenePreserved, scope = "Editor surface/import/UV policy checks, not native or worn Quest blur acceptance" }, Formatting.Indented));
                }
                if (!assetsPreserved || !scenePreserved) throw new InvalidOperationException("Environment surface checks changed production assets or the scene.");
            }
            Debug.Log("RESEARCH_ENVIRONMENT_SURFACE_PASS checks=" + checks.Count + "; Editor only");
        }
    }
}
#endif
