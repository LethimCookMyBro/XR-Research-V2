using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Visual-quality pass tooling for the open ResearchFlow scene. Primitives only in JSON
    // (Newtonsoft recurses forever through Vector3.normalized / Color.linear).
    public static class ResearchVisualQualityPass
    {
        public const string Output = "Logs/QuestFinalIntegration/visual-quality-20260929";

        static string Write(string name, object value)
        {
            Directory.CreateDirectory(Output);
            string path = Path.GetFullPath(Path.Combine(Output, name));
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            return path;
        }

        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        // World metres covered by one UV unit (sqrt of world area / uv area), over all triangles.
        public static float MetresPerUv(Mesh mesh, Transform transform)
        {
            if (!mesh || !mesh.isReadable) return float.NaN;
            var uv = mesh.uv; var vertices = mesh.vertices; var triangles = mesh.triangles;
            if (uv.Length != vertices.Length || triangles.Length == 0) return float.NaN;
            double world = 0, texture = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var pa = transform.TransformPoint(vertices[a]); var pb = transform.TransformPoint(vertices[b]); var pc = transform.TransformPoint(vertices[c]);
                world += Vector3.Cross(pb - pa, pc - pa).magnitude * .5;
                texture += Mathf.Abs((uv[b].x - uv[a].x) * (uv[c].y - uv[a].y) - (uv[c].x - uv[a].x) * (uv[b].y - uv[a].y)) * .5;
            }
            return texture <= 1e-9 ? float.PositiveInfinity : (float)Math.Sqrt(world / texture);
        }

        public static readonly string[] PbrMaterials = { "UXS_Floor", "UXS_Wall", "SP_Frame", "SP_Paint", "SP_Steel", "SP_Blue", "SP_Bin", "SP_Rubber", "SP_Timber" };

        // Widest rendered tutorial line per language (coach font sizes), to size the text column so no line wraps.
        public static string TutorialLineWidths()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include);
            var probe = new GameObject("LineProbe").AddComponent<TMPro.TextMeshPro>();
            try
            {
                var rows = new List<string>();
                foreach (var (language, font) in new[] { ("English", view.englishFont), ("Thai", view.thaiFont), ("Japanese", view.japaneseFont) })
                {
                    probe.font = font; probe.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                    float widest = 0; string which = "";
                    foreach (var pair in ResearchStrings.All(language).Where(p => p.Key.StartsWith("tutorial.try.") || p.Key.StartsWith("tutorial.control.") || p.Key == "tutorial.complete.body"))
                    {
                        var lines = pair.Value.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            probe.fontSize = pair.Key.StartsWith("tutorial.control.") ? 20 : i == 0 ? 30 : 27;
                            probe.fontStyle = i == 0 && !pair.Key.StartsWith("tutorial.control.") ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
                            float width = probe.GetPreferredValues(lines[i]).x * (pair.Key.StartsWith("tutorial.control.") ? 1 : 1);
                            rows.Add(language + " " + pair.Key + "[" + i + "] " + width.ToString("0"));
                            if (!pair.Key.StartsWith("tutorial.control.") && width > widest) { widest = width; which = pair.Key + "[" + i + "]"; }
                        }
                    }
                    rows.Insert(0, "WIDEST " + language + " " + widest.ToString("0") + " " + which);
                }
                return string.Join("\n", rows.Where(r => r.StartsWith("WIDEST") || float.Parse(r.Substring(r.LastIndexOf(' ') + 1)) > 50 || (r.Contains("tutorial.control.") && float.Parse(r.Substring(r.LastIndexOf(' ') + 1)) > 17)));
            }
            finally { UnityEngine.Object.DestroyImmediate(probe.gameObject); }
        }

        public static string DirtyProbe()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include);
            var names = new[] { "TutorialDemo", "TutorialSuccess", "LineProbe", "PracticeHighlight_0", "SecondPass_EvidenceCamera" };
            var found = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => names.Any(n => t.name.StartsWith(n))).Select(t => PathOf(t)).ToArray();
            bool onDisk = File.ReadAllText(scene.path).Contains("TutorialDemo");
            return "dirty=" + scene.isDirty + " inMemory=[" + string.Join(";", found) + "] onDiskHasTutorialDemo=" + onDisk;
        }

        public static string SaveInMemoryCopy()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            const string copy = "Temp/ResearchFlow_InMemory_Copy.unity";
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, copy, true);
            return "copy=" + ok + " dirtyAfter=" + scene.isDirty + " path=" + scene.path;
        }

        public static string ReloadActiveSceneFromDisk()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var reopened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return "reloaded " + reopened.path + " dirty=" + reopened.isDirty;
        }

        public static string MeshBudget()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var filters = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.sharedMaterials.Any(m => m && PbrMaterials.Contains(m.name))).Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh).ToArray();
            var unique = filters.GroupBy(f => (f.sharedMesh, Key(f.transform.lossyScale))).ToArray();
            return "scene_dirty=" + scene.isDirty + " renderers=" + filters.Length + " unique_mesh_scale=" + unique.Length +
                " vertices=" + unique.Sum(g => g.Key.sharedMesh.vertexCount) + " meshes=" + filters.Select(f => f.sharedMesh).Distinct().Count() +
                " submesh_other_materials=" + filters.Count(f => f.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m && !PbrMaterials.Contains(m.name)));
        }

        static string Key(Vector3 v) => v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###");

        public static string Audit()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(r => r.enabled && !r.GetComponentInParent<ResearchFlowView>(true) && !r.GetComponentInParent<Canvas>(true)).ToArray();
            var lods = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(l => new { path = PathOf(l.transform), lods = l.lodCount, enabled = l.enabled && l.gameObject.activeInHierarchy }).ToArray();
            var materials = new Dictionary<Material, List<MeshRenderer>>();
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    if (material) { if (!materials.TryGetValue(material, out var list)) materials[material] = list = new List<MeshRenderer>(); list.Add(renderer); }
            var rows = materials.Select(pair =>
            {
                var material = pair.Key;
                var textures = material.GetTexturePropertyNames().Where(p => material.GetTexture(p)).Select(p => p + "=" + material.GetTexture(p).name + "(" + material.GetTexture(p).width + ")").ToArray();
                float area = pair.Value.Sum(r => { var s = r.bounds.size; return 2 * (s.x * s.y + s.y * s.z + s.x * s.z); });
                var largest = pair.Value.OrderByDescending(r => r.bounds.size.sqrMagnitude).First();
                var filter = largest.GetComponent<MeshFilter>();
                return new
                {
                    material = material.name, asset = AssetDatabase.GetAssetPath(material), shader = material.shader.name,
                    base_color = "#" + ColorUtility.ToHtmlStringRGB(material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color),
                    metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : -1,
                    smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : -1,
                    tiling = material.HasProperty("_BaseMap") ? new[] { material.GetTextureScale("_BaseMap").x, material.GetTextureScale("_BaseMap").y } : null,
                    textures, renderers = pair.Value.Count, bounds_area_m2 = area,
                    largest = PathOf(largest.transform), largest_size = new[] { largest.bounds.size.x, largest.bounds.size.y, largest.bounds.size.z },
                    largest_metres_per_uv = filter ? MetresPerUv(filter.sharedMesh, largest.transform) : float.NaN,
                    readable = filter && filter.sharedMesh && filter.sharedMesh.isReadable
                };
            }).OrderByDescending(r => r.bounds_area_m2).ToArray();
            var projectMats = AssetDatabase.FindAssets("t:Material", new[] { "Assets/VRMaintenanceResearch" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(p => AssetDatabase.LoadAssetAtPath<Material>(p))
                .Select(m => new { path = AssetDatabase.GetAssetPath(m), textured = m.GetTexturePropertyNames().Any(p => m.GetTexture(p)) }).ToArray();
            return Write("material-audit.json", new
            {
                scene = scene.path, lod_groups = lods, lod_group_count = lods.Length,
                active_renderers = renderers.Length, scene_materials = rows.Length,
                scene_untextured = rows.Count(r => r.textures.Length == 0),
                project_materials = projectMats.Length, project_untextured = projectMats.Count(m => !m.textured),
                rows, hardware_verified = false
            });
        }
            const string PbrRoot = "Assets/VRMaintenanceResearch/Assets/V2/Environment/textures/PBR/";
        const string MeshRoot = "Assets/VRMaintenanceResearch/ResearchFlow/Meshes/VisualQuality";

        // material -> texture set, metres per repeat, tint (sRGB), smoothness scale, normal strength, occlusion strength
        static readonly (string material, string set, float metres, Color tint, float smooth, float normal, float occlusion)[] PbrPlan =
        {
            ("UXS_Floor", "PBR_ConcreteFloor", 3f, new Color(.93f, .94f, .96f), .9f, .8f, 1f),
            ("UXS_Wall", "PBR_PaintedPlaster", 2f, new Color(.86f, .9f, .92f), .6f, .6f, .8f),
            ("SP_Frame", "PBR_PaintedMetal", 1f, new Color(.15f, .2f, .24f), .9f, 1f, 1f),
            ("SP_Paint", "PBR_PaintedMetal", 1.2f, new Color(.22f, .31f, .37f), .85f, 1f, 1f),
            ("SP_Steel", "PBR_BrushedSteel", .8f, new Color(.86f, .89f, .92f), .95f, .7f, 1f),
            ("SP_Blue", "PBR_PaintedMetal", 1f, new Color(.16f, .36f, .58f), .9f, 1f, 1f),
            ("SP_Bin", "PBR_PaintedMetal", .8f, new Color(.13f, .34f, .62f), .7f, .6f, 1f),
            ("SP_Rubber", "PBR_RubberMat", .8f, Color.white, .7f, 1f, 1f),
            ("SP_Timber", "PBR_Plywood", 1.2f, new Color(1f, .96f, .9f), .8f, .8f, 1f),
        };

        // Idempotent; the bridge can call it repeatedly (one reimport per call keeps each Editor tick short).
        public static string ConfigureTextures(int limit = int.MaxValue)
        {
            int changed = 0, pending = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PbrRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var android = importer.GetPlatformTextureSettings("Android");
                if (android.overridden && (android.format == TextureImporterFormat.ASTC_5x5 || android.format == TextureImporterFormat.ASTC_6x6) && importer.mipmapEnabled && importer.filterMode == FilterMode.Trilinear) continue;
                if (changed >= limit) { pending++; continue; }
                bool normal = path.EndsWith("_Normal.png"), mask = path.EndsWith("_Mask.png"), floor = path.Contains("ConcreteFloor");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !mask;
                importer.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true; importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = floor ? 8 : 4; importer.mipMapBias = 0; importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = floor ? 2048 : 1024;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = importer.maxTextureSize,
                    format = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6,
                    compressionQuality = 50, textureCompression = TextureImporterCompression.Compressed
                });
                importer.SaveAndReimport(); changed++;
            }
            return "PBR_TEXTURES_CONFIGURED " + changed + " pending=" + pending;
        }

        // Dominant-axis box projection in the renderer's scaled local space: 1 UV unit = 1 m.
        static Vector2[] BoxUv(Mesh mesh, Vector3 scale, HashSet<int> vertices, Vector2[] original)
        {
            var positions = mesh.vertices; var normals = mesh.normals;
            var uv = original.Length == positions.Length ? (Vector2[])original.Clone() : new Vector2[positions.Length];
            var inverse = new Vector3(1 / Mathf.Max(1e-6f, Mathf.Abs(scale.x)), 1 / Mathf.Max(1e-6f, Mathf.Abs(scale.y)), 1 / Mathf.Max(1e-6f, Mathf.Abs(scale.z)));
            foreach (int i in vertices)
            {
                var q = Vector3.Scale(positions[i], scale);
                var n = normals.Length == positions.Length ? Vector3.Scale(normals[i], inverse) : Vector3.up;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                uv[i] = ax >= ay && ax >= az ? new Vector2(-q.z * Mathf.Sign(n.x), q.y)
                    : az >= ay ? new Vector2(q.x * Mathf.Sign(n.z), q.y) : new Vector2(q.x, q.z * Mathf.Sign(n.y));
            }
            return uv;
        }

        public static string ApplyPbr()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity")) throw new InvalidOperationException("Open the ResearchFlow V2 scene first.");
            if (scene.isDirty) throw new InvalidOperationException("Scene already has unsaved changes; refusing to mix them into the PBR pass.");
            if (!ConfigureTextures(0).EndsWith("pending=0")) throw new InvalidOperationException("Configure PBR textures first (ConfigureTextures).");
            Directory.CreateDirectory(MeshRoot);
            var before = new List<object>();
            var materials = new Dictionary<string, Material>();
            var inScene = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SelectMany(r => r.sharedMaterials).Where(m => m).ToArray();
            foreach (var plan in PbrPlan)
            {
                var used = inScene.Where(m => m.name == plan.material).Distinct().ToArray();
                if (used.Length != 1 || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(used[0])))
                    throw new InvalidOperationException(plan.material + ": expected exactly one scene-referenced material asset, found " + used.Length);
                var material = used[0];
                if (material.shader.name != "Universal Render Pipeline/Lit") throw new InvalidOperationException(plan.material + " is not URP Lit");
                before.Add(new { material = plan.material, base_color = "#" + ColorUtility.ToHtmlStringRGB(material.GetColor("_BaseColor")),
                    base_map = material.GetTexture("_BaseMap") ? material.GetTexture("_BaseMap").name : null, metallic = material.GetFloat("_Metallic"), smoothness = material.GetFloat("_Smoothness") });
                Texture2D Load(string suffix) => AssetDatabase.LoadAssetAtPath<Texture2D>(PbrRoot + plan.set + suffix) ?? throw new FileNotFoundException(plan.set + suffix);
                var maskMap = Load("_Mask.png");
                material.SetTexture("_BaseMap", Load("_BaseColor.png"));
                material.SetTexture("_BumpMap", Load("_Normal.png")); material.SetFloat("_BumpScale", plan.normal);
                material.SetTexture("_MetallicGlossMap", maskMap); material.SetFloat("_SmoothnessTextureChannel", 0);
                material.SetTexture("_OcclusionMap", maskMap); material.SetFloat("_OcclusionStrength", plan.occlusion);
                material.SetColor("_BaseColor", plan.tint); material.SetFloat("_Smoothness", plan.smooth); material.SetFloat("_Metallic", 1);
                material.SetTextureScale("_BaseMap", Vector2.one / plan.metres); material.SetTextureOffset("_BaseMap", Vector2.zero);
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP");
                material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                EditorUtility.SetDirty(material);
                materials[plan.material] = material;
            }
            // One UV copy per (mesh, scale); vertices of other-material submeshes keep their UVs.
            var copies = new Dictionary<string, Mesh>();
            int assigned = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var shared = renderer.sharedMaterials;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || !shared.Any(m => m && materials.ContainsKey(m.name))) continue;
                var source = filter.sharedMesh;
                if (AssetDatabase.GetAssetPath(source).StartsWith(MeshRoot)) continue; // already converted
                if (!source.isReadable) throw new InvalidOperationException("Mesh not readable: " + source.name);
                var scale = renderer.transform.lossyScale;
                string key = AssetDatabase.GetAssetPath(source) + "|" + source.name + "|" + Key(scale);
                if (!copies.TryGetValue(key, out var copy))
                {
                    var vertices = new HashSet<int>();
                    for (int sub = 0; sub < source.subMeshCount && sub < shared.Length; sub++)
                        if (shared[sub] && materials.ContainsKey(shared[sub].name)) vertices.UnionWith(source.GetTriangles(sub));
                    copy = UnityEngine.Object.Instantiate(source);
                    copy.name = source.name + "_BoxUV";
                    copy.uv = BoxUv(source, scale, vertices, source.uv);
                    copy.RecalculateTangents();
                    string file = MeshRoot + "/" + string.Concat((source.name + "_" + copies.Count).Split(Path.GetInvalidFileNameChars())) + ".asset";
                    AssetDatabase.CreateAsset(copy, file);
                    copies[key] = copy;
                }
                Undo.RecordObject(filter, "PBR box UV");
                filter.sharedMesh = copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                assigned++;
            }
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed");
            return Write("pbr-apply.json", new
            {
                scene = scene.path, textures = PbrRoot, license = "CC0 (see PBR/CREDITS.md)", before,
                after = PbrPlan.Select(p => new { p.material, p.set, metres_per_repeat = p.metres, tint = "#" + ColorUtility.ToHtmlStringRGB(p.tint), p.smooth, p.normal, p.occlusion }).ToArray(),
                mesh_copies = copies.Count, renderers_assigned = assigned,
                import = "mipmaps, trilinear, aniso 8 floor / 4 others, Android ASTC 6x6 (normal 5x5), max 2048 floor / 1024 others",
                not_changed = "glTF-shader vehicles, CNC machines, robots, luminaires; lighting; post-processing",
                hardware_verified = false
            });
        }
    }
}
