#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Temporary trials only. Existing Capture owns rendering/temporal metrics; this class owns bindings/restoration.
    // It never creates project assets, saves/reloads a scene, retains a candidate, or claims optical/wearer acceptance.
    public static class ResearchDistantReadabilityChecks
    {
        const string Root = "Assets/VRMaintenanceResearch/ResearchFlow/";
        const string SignFont = Root + "UI/Fonts/ResearchFlow_English.asset";
        const string SignMaterial = Root + "Materials/SecondPass/SP_PlaqueText.mat";
        const string SteelMaterial = Root + "Materials/SecondPass/SP_Steel.mat";
        const string TimberMaterial = Root + "Materials/SecondPass/SP_Timber.mat";
        const string TimberMesh = Root + "Meshes/VisualQuality/Grounded_Training_Practice_A_145.asset";
        const string TimberMeshBaselineHash = "c2df1475e16fcf70f2678d4516512f357b99566328ff441546f7382fba11cd06";
        static bool running;

        sealed class SignState
        {
            public TextMeshPro text;
            public TMP_FontAsset font;
            public Material material;
            public bool auto;
            public float size;
            public string value;
        }

        static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static string Write(string directory, string name, object value)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            return path;
        }

        // Root supplies a new evidence directory explicitly; every call gets an immutable unique child.
        public static string TrialSignSdf16(string outputRoot, string view = "Training_fwd") => Run(outputRoot, view, "sign_sdf16");
        public static string TrialSteelNormal(string outputRoot, float normal = 0f, string view = "Training_fwd")
        {
            Require(normal == 0f || normal == .15f, "Only the isolated normal 0/.15 candidates are supported.");
            return Run(outputRoot, view, normal == 0 ? "steel_normal0" : "steel_normal015");
        }
        public static string TrialTimberBevelUv(string outputRoot) => Run(outputRoot, "Training_fwd", "timber_bevel_uv");

        public static string Run(string outputRoot, string view = "Training_fwd", string trial = "baseline")
        {
            Require(!running && !EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode only, one trial at a time.");
            Require(new[] { "Training_fwd", "Computer_fwd", "Training_sign" }.Contains(view), "Unsupported matched view.");
            Require(new[] { "baseline", "sign_sdf16", "steel_normal0", "steel_normal015", "timber_bevel_uv" }.Contains(trial), "Unsupported trial.");
            Require(trial != "timber_bevel_uv" || view == "Training_fwd", "The bounded Timber mesh trial uses Training_fwd only.");
            var scene = SceneManager.GetActiveScene();
            Require(scene.path == "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity" && !scene.isDirty,
                "Canonical saved ResearchFlow scene required; dirty scenes are never discarded.");
            Require(!string.IsNullOrWhiteSpace(outputRoot), "Supply an explicit evidence output directory.");
            string baseDirectory = Path.GetFullPath(outputRoot);
            string assetsDirectory = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            Require(!baseDirectory.StartsWith(assetsDirectory, StringComparison.OrdinalIgnoreCase), "Evidence cannot be written inside Assets.");
            string directory = Path.Combine(baseDirectory, trial + "_" + view + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            Require(directory.Length < 210, "Use a shorter evidence root for Windows output paths.");
            Directory.CreateDirectory(directory);

            var labels = UnityEngine.Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name == "Permanent neutral lab identification" && t.gameObject.scene == scene).ToArray();
            Require(labels.Length == 3 && labels.All(t => t.font && AssetDatabase.GetAssetPath(t.font) == SignFont), "Exactly the three existing English lab signs are required.");
            foreach (var t in labels) t.ForceMeshUpdate(true, true);
            var signs = labels.Select(t => new SignState { text = t, font = t.font, material = t.fontSharedMaterial,
                auto = t.enableAutoSizing, size = t.fontSize, value = t.text }).ToArray();
            Require(signs.All(s => s.material && AssetDatabase.GetAssetPath(s.material) == SignMaterial), "Unexpected signage material; refuse to replace unrelated work.");
            var steel = AssetDatabase.LoadAssetAtPath<Material>(SteelMaterial);
            Require(steel && Mathf.Abs(steel.GetFloat("_BumpScale") - .35f) < .0001f, "Expected retained steel baseline normal .35.");
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.sharedMaterials.Contains(steel)).ToArray();
            Require(renderers.Length > 0, "No steel bindings found.");
            var oldMaterials = renderers.Select(r => r.sharedMaterials).ToArray();
            MeshFilter timberFilter = null;
            Mesh oldTimberMesh = null;
            MeshCollider timberCollider = null;
            Mesh oldColliderMesh = null;
            bool hadTimberCollider = false;
            if (trial == "timber_bevel_uv")
            {
                timberFilter = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true))
                    .Single(f => PathOf(f.transform) == "Environment_V2/Training/Training_Practice_A");
                oldTimberMesh = timberFilter.sharedMesh;
                timberCollider = timberFilter.GetComponent<MeshCollider>();
                hadTimberCollider = timberCollider;
                oldColliderMesh = timberCollider ? timberCollider.sharedMesh : null;
                Require(oldTimberMesh && AssetDatabase.GetAssetPath(oldTimberMesh) == TimberMesh && Hash(TimberMesh) == TimberMeshBaselineHash,
                    "Timber mesh differs from the reviewed source; refuse a stale candidate.");
                var slots = timberFilter.GetComponent<Renderer>().sharedMaterials;
                Require(slots.Length == 3 && AssetDatabase.GetAssetPath(slots[2]) == TimberMaterial,
                    "Reviewed Timber slot2 binding is required.");
            }
            string[] files = { scene.path, SignFont, SignMaterial, SteelMaterial, "Assets/XRI_Examples/Global/RendererData/UniversalRP-MediumQuality.asset",
                "Assets/TextMesh Pro/Fonts/LiberationSans.ttf", "Assets/TextMesh Pro/Shaders/TMP_SDF-URP Unlit.shadergraph",
                "Assets/TextMesh Pro/Shaders/SDFFunctions.hlsl", Root + "Editor/ResearchDistantReadabilityChecks.cs", Root + "Editor/ResearchDistantArtifactAudit.cs" };
            if (trial == "timber_bevel_uv") files = files.Concat(new[] { TimberMesh, TimberMesh + ".meta", TimberMaterial, TimberMaterial + ".meta" }).ToArray();
            var hashes = files.ToDictionary(p => p, Hash);
            string oldOutput = ResearchDistantArtifactAudit.Output;
            int oldQuality = QualitySettings.GetQualityLevel();
            var temporaries = new List<UnityEngine.Object>();
            Exception failure = null;
            JObject actual = null;
            string actualPath = null;
            object[] frameEvidence = null;
            object meshEvidence = null;
            bool restored = false;
            var restoreErrors = new List<string>();
            running = true;
            try
            {
                // Freeze each existing effective font size; font generation is the only sign trial variable.
                foreach (var s in signs) { s.text.enableAutoSizing = false; s.text.fontSize = s.size; }
                if (trial == "sign_sdf16")
                {
                    var source = signs[0].font.sourceFontFile;
                    Require(source, "Original English source font is unavailable.");
                    var font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDF16, 2048, 2048, AtlasPopulationMode.Dynamic, false);
                    Require(font, "Installed FontEngine could not create the temporary SDF16 font.");
                    temporaries.Add(font); temporaries.Add(font.material); font.material.hideFlags = HideFlags.HideAndDontSave;
                    font.name = "Temporary neutral signage SDF16"; font.hideFlags = HideFlags.HideAndDontSave;
                    string chars = new string(string.Concat(signs.Select(s => s.value)).Distinct().ToArray());
                    bool added = font.TryAddCharacters(chars, out string missing);
                    foreach (var atlas in font.atlasTextures) { atlas.hideFlags = HideFlags.HideAndDontSave; temporaries.Add(atlas); }
                    Require(added && string.IsNullOrEmpty(missing), "Missing SDF16 glyphs: " + missing);
                    font.atlasPopulationMode = AtlasPopulationMode.Static;
                    var material = new Material(signs[0].material) { name = "Temporary plaque SDF16", hideFlags = HideFlags.HideAndDontSave };
                    temporaries.Add(material); material.SetTexture("_MainTex", font.atlasTexture);
                    Require(material.HasProperty("_GradientScale"), "Existing URP plaque graph has no SDF scale property.");
                    material.SetFloat("_GradientScale", font.atlasPadding + 1); font.material = material;
                    foreach (var s in signs) { s.text.font = font; s.text.fontSharedMaterial = material; s.text.fontSize = s.size; s.text.ForceMeshUpdate(true, true); }
                }
                if (trial.StartsWith("steel_normal", StringComparison.Ordinal))
                {
                    var material = new Material(steel) { name = "Temporary steel " + trial, hideFlags = HideFlags.HideAndDontSave };
                    temporaries.Add(material); material.SetFloat("_BumpScale", trial == "steel_normal0" ? 0 : .15f);
                    for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = oldMaterials[i].Select(m => m == steel ? material : m).ToArray();
                }
                if (trial == "timber_bevel_uv")
                {
                    var candidate = BuildTimberBevelMesh(oldTimberMesh, timberFilter.transform, temporaries, out meshEvidence);
                    Write(directory, "mesh-trial.json", meshEvidence);
                    timberFilter.sharedMesh = candidate;
                    Require(!hadTimberCollider || (timberCollider && timberCollider.sharedMesh == oldColliderMesh), "Trial changed the collision mesh.");
                }
                Require(!scene.isDirty, "Trial assignment dirtied the scene; refuse capture/reload and restore references.");
                WriteGlyphEvidence(directory, signs);
                // Reuse the exact existing temporal renderer and seed. Never allow its historical auto-reload path.
                ResearchDistantArtifactAudit.Output = Path.Combine(directory, "warm");
                ResearchDistantArtifactAudit.Capture(view, 1.10f, 2, true, false, false, false);
                ResearchDistantArtifactAudit.Output = Path.Combine(directory, "capture");
                actualPath = ResearchDistantArtifactAudit.Capture(view, 1.10f, 2, true, true, false, true);
                actual = JObject.Parse(File.ReadAllText(actualPath));
                Require((int)actual["totals"]["repeat_render_diff_px"] == 0, "Unjittered repeat control failed; temporal comparison is invalid.");
                Require((int)actual["frames"] == 8 && (int)actual["eye_size"][0] == 1848 && (int)actual["eye_size"][1] == 1936,
                    "Unexpected temporal capture dimensions or frame count.");
                Require((string)actual["reload"] == "scene stayed clean", "Capture unexpectedly reloaded the scene.");
                frameEvidence = Enumerable.Range(0, 8).Select(f => PngEvidence(Path.Combine(directory, "capture", view + "_frame" + f.ToString("00") + ".png"))).ToArray();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                // Attempt every restore even if a different reference fails. Never mark a scene clean to hide mutation.
                foreach (var s in signs)
                {
                    try
                    {
                        if (!s.text) throw new InvalidOperationException("Sign disappeared: possible external scene reload.");
                        s.text.font = s.font; s.text.fontSharedMaterial = s.material; s.text.fontSize = s.size;
                        s.text.enableAutoSizing = s.auto; s.text.ForceMeshUpdate(true, true);
                    }
                    catch (Exception ex) { restoreErrors.Add(ex.Message); }
                }
                for (int i = 0; i < renderers.Length; i++)
                    try { if (renderers[i]) renderers[i].sharedMaterials = oldMaterials[i]; else restoreErrors.Add("Steel renderer disappeared."); }
                    catch (Exception ex) { restoreErrors.Add(ex.Message); }
                if (oldTimberMesh)
                    try { if (timberFilter) timberFilter.sharedMesh = oldTimberMesh; else restoreErrors.Add("Timber MeshFilter disappeared."); }
                    catch (Exception ex) { restoreErrors.Add(ex.Message); }
                ResearchDistantArtifactAudit.Output = oldOutput;
                try { QualitySettings.SetQualityLevel(oldQuality, false); } catch (Exception ex) { restoreErrors.Add(ex.Message); }
                foreach (var item in temporaries.Distinct())
                    try { if (item && !AssetDatabase.Contains(item)) UnityEngine.Object.DestroyImmediate(item); }
                    catch (Exception ex) { restoreErrors.Add(ex.Message); }
                restored = restoreErrors.Count == 0 && scene.IsValid() && !scene.isDirty && QualitySettings.GetQualityLevel() == oldQuality
                    && signs.All(s => s.text && s.text.font == s.font && s.text.fontSharedMaterial == s.material && s.text.text == s.value
                        && s.text.enableAutoSizing == s.auto && Mathf.Abs(s.text.fontSize - s.size) < .0001f)
                    && renderers.Select((r, i) => r && r.sharedMaterials.SequenceEqual(oldMaterials[i])).All(v => v)
                    && (!oldTimberMesh || (timberFilter && timberFilter.sharedMesh == oldTimberMesh
                        && (!hadTimberCollider || (timberCollider && timberCollider.sharedMesh == oldColliderMesh))))
                    && hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                running = false;
            }
            string report = Write(directory, "trial.json", new
            {
                schema = "research.distant-readability-trial.1", utc = DateTime.UtcNow.ToString("o"), trial, view,
                status = failure == null && restored ? "captured_editor_trial" : "failed",
                hardware_verified = false, candidate_retained = false, decision = "pending actual image inspection and native cost comparison",
                scope = "matched Editor temporal proxy and font-atlas threshold masks; no optical/comfort/performance score",
                capture = actualPath, totals = actual?["totals"], top_surface = actual?["top_surface"], restored,
                temporal_frames = frameEvidence,
                mesh_trial = meshEvidence,
                restore_errors = restoreErrors, scene_dirty = scene.IsValid() ? (bool?)scene.isDirty : null,
                source_sha256 = hashes, error = failure?.ToString(), glyph_evidence = "glyphs.json"
            });
            if (failure != null || !restored) throw new InvalidOperationException("Readability trial failed; inspect " + report, failure);
            return report;
        }

        // One reviewed mesh only: split bevel seams, keep every original vertex/channel and triangle position.
        // UV0 changes require a matching tangent basis on the new bevel vertices; existing tangents stay exact.
        static Mesh BuildTimberBevelMesh(Mesh source, Transform transform, List<UnityEngine.Object> temporaries, out object evidence)
        {
            Require(source.isReadable && source.vertexCount == 1744 && source.subMeshCount == 3 && source.blendShapeCount == 0
                && source.bindposes.Length == 0 && source.GetVertexAttributes().Length == 5
                && source.GetVertexAttributes().All(a => a.format == VertexAttributeFormat.Float32), "Unexpected reviewed mesh shape/channels.");
            Require(source.GetVertexAttributeDimension(VertexAttribute.Position) == 3
                && source.GetVertexAttributeDimension(VertexAttribute.Normal) == 3
                && source.GetVertexAttributeDimension(VertexAttribute.Tangent) == 4
                && source.GetVertexAttributeDimension(VertexAttribute.TexCoord0) == 2
                && source.GetVertexAttributeDimension(VertexAttribute.TexCoord1) == 2, "Unsupported vertex-channel shape; refuse lossy cloning.");
            Require(transform.lossyScale.Equals(Vector3.one) && Quaternion.Angle(transform.rotation, Quaternion.identity) < .0001f,
                "Reviewed unit-scale, unrotated bench required for metre-based face charts.");
            var originalVertices = source.vertices; var originalNormals = source.normals; var originalTangents = source.tangents;
            var originalUv = source.uv; var originalUv1 = source.uv2;
            Require(originalNormals.Length == 1744 && originalTangents.Length == 1744 && originalUv.Length == 1744 && originalUv1.Length == 1744,
                "Missing reviewed mesh channel.");
            var triangles = Enumerable.Range(0, 3).Select(s => source.GetTriangles(s)).ToArray();
            Require(triangles.Select(t => t.Length).SequenceEqual(new[] { 2268, 1296, 324 }), "Reviewed triangle counts changed.");
            var vertices = originalVertices.ToList(); var normals = originalNormals.ToList(); var tangents = originalTangents.ToList();
            var uv = originalUv.ToList(); var uv1 = originalUv1.ToList();
            var sourceIndex = Enumerable.Range(0, 1744).ToList();
            var timber = (int[])triangles[2].Clone();
            var beforeDensity = new List<float>(); var afterDensity = new List<float>();
            float beforeWorld = 0, beforeArea = 0, afterArea = 0, maxTangentNormalDot = 0;
            for (int i = 0; i < timber.Length; i += 3)
            {
                int ia = triangles[2][i], ib = triangles[2][i + 1], ic = triangles[2][i + 2];
                var face = Vector3.Cross(originalVertices[ib] - originalVertices[ia], originalVertices[ic] - originalVertices[ia]);
                float worldArea = face.magnitude * .5f;
                Require(worldArea > 1e-10f, "Degenerate reviewed triangle.");
                face.Normalize();
                // Exactly the 80 reviewed bevel/other triangles. Good flat top/bottom and vertical faces keep UV0.
                float vertical = Mathf.Abs(face.y);
                if (vertical < .01f || vertical > .99f) continue;
                int[] ids = { ia, ib, ic };
                Vector2[] chart = ids.Select(id => FaceUv(originalVertices[id], face)).ToArray();
                float oldArea = UvArea(originalUv[ia], originalUv[ib], originalUv[ic]);
                float newArea = UvArea(chart[0], chart[1], chart[2]);
                Require(oldArea > 1e-10f && newArea > 1e-10f, "Degenerate bevel UV chart.");
                beforeWorld += worldArea; beforeArea += oldArea; afterArea += newArea;
                beforeDensity.Add(Mathf.Sqrt(worldArea / oldArea)); afterDensity.Add(Mathf.Sqrt(worldArea / newArea));
                var edge1 = originalVertices[ib] - originalVertices[ia]; var edge2 = originalVertices[ic] - originalVertices[ia];
                var du1 = chart[1] - chart[0]; var du2 = chart[2] - chart[0];
                float determinant = du1.x * du2.y - du1.y * du2.x;
                var direction = (edge1 * du2.y - edge2 * du1.y) / determinant;
                var bitangent = (edge2 * du1.x - edge1 * du2.x) / determinant;
                for (int c = 0; c < 3; c++)
                {
                    int id = ids[c]; var n = originalNormals[id].normalized;
                    var t = direction - n * Vector3.Dot(n, direction);
                    Require(t.sqrMagnitude > 1e-10f, "Undefined new tangent basis."); t.Normalize();
                    float handedness = Vector3.Dot(Vector3.Cross(n, t), bitangent) < 0 ? -1 : 1;
                    maxTangentNormalDot = Mathf.Max(maxTangentNormalDot, Mathf.Abs(Vector3.Dot(n, t)));
                    timber[i + c] = vertices.Count; vertices.Add(originalVertices[id]); normals.Add(originalNormals[id]);
                    tangents.Add(new Vector4(t.x, t.y, t.z, handedness)); uv.Add(chart[c]); uv1.Add(originalUv1[id]); sourceIndex.Add(id);
                }
            }
            Require(beforeDensity.Count == 80 && beforeDensity.Min() < .02f, "Reviewed failing UV-density check no longer reproduces.");
            Require(afterDensity.All(d => d >= .95f && d <= 1.5f), "Corrected bevel chart density is outside the bounded metre-based range.");
            var candidate = UnityEngine.Object.Instantiate(source); temporaries.Add(candidate);
            candidate.name = "Temporary Training Timber bevel face UV"; candidate.hideFlags = HideFlags.HideAndDontSave;
            candidate.SetVertices(vertices); candidate.SetNormals(normals); candidate.SetTangents(tangents);
            candidate.SetUVs(0, uv); candidate.SetUVs(1, uv1); candidate.SetTriangles(timber, 2, false); candidate.bounds = source.bounds;
            var candidateVertices = candidate.vertices; var candidateNormals = candidate.normals;
            var candidateUv = candidate.uv; var candidateUv1 = candidate.uv2; var candidateTangents = candidate.tangents;
            bool positionExact = sourceIndex.Select((id, i) => candidateVertices[i].Equals(originalVertices[id])).All(x => x);
            bool normalExact = sourceIndex.Select((id, i) => candidateNormals[i].Equals(originalNormals[id])).All(x => x);
            bool uv1Exact = sourceIndex.Select((id, i) => candidateUv1[i].Equals(originalUv1[id])).All(x => x);
            bool originalChannelsExact = Enumerable.Range(0, 1744).All(i => candidateUv[i].Equals(originalUv[i]) && candidateTangents[i].Equals(originalTangents[i]));
            bool geometryExact = Enumerable.Range(0, 3).All(s => candidate.GetTriangles(s).Select(id => sourceIndex[id]).SequenceEqual(triangles[s]));
            bool boundsExact = candidate.bounds.Equals(source.bounds);
            bool beforeRangePass = beforeDensity.All(d => d >= .95f && d <= 1.5f);
            evidence = new { schema = "research.timber-bevel-uv-trial.1", hardware_verified = false, source_asset = TimberMesh,
                source_sha256 = TimberMeshBaselineHash, material_slot = 2, scope = "Timber bevel UV0 plus required matching tangent basis; not diffuse-only attribution",
                original_vertices = 1744, candidate_vertices = candidate.vertexCount, split_triangles = beforeDensity.Count,
                original_triangle_counts = triangles.Select(t => t.Length / 3).ToArray(),
                candidate_triangle_counts = Enumerable.Range(0, 3).Select(s => candidate.GetTriangles(s).Length / 3).ToArray(),
                original_vertex_uv0_and_tangents_exact = originalChannelsExact,
                mapped_positions_exact = positionExact, mapped_normals_exact = normalExact, mapped_uv1_exact = uv1Exact,
                mapped_triangle_geometry_exact = geometryExact, world_bounds_exact = boundsExact, new_tangent_normal_abs_dot_max = maxTangentNormalDot,
                preserved_good_flat_top_uv = originalChannelsExact, before = new { world_area = beforeWorld, uv_area = beforeArea,
                    metres_per_uv_min = beforeDensity.Min(), metres_per_uv_max = beforeDensity.Max(), density_range_check_pass = beforeRangePass },
                after = new { world_area = beforeWorld, uv_area = afterArea, metres_per_uv_min = afterDensity.Min(),
                    metres_per_uv_max = afterDensity.Max(), density_range_check_pass = afterDensity.All(d => d >= .95f && d <= 1.5f) },
                candidate_retained = false };
            Require(!beforeRangePass && candidate.vertexCount == 1984 && positionExact && normalExact && uv1Exact && originalChannelsExact && geometryExact && boundsExact
                && maxTangentNormalDot < .0001f, "Temporary candidate failed geometry/channel preservation.");
            return candidate;
        }

        static Vector2 FaceUv(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            return ax >= ay && ax >= az ? new Vector2(-p.z * Mathf.Sign(n.x), p.y)
                : az >= ay ? new Vector2(p.x * Mathf.Sign(n.z), p.y) : new Vector2(p.x, p.z * Mathf.Sign(n.y));
        }
        static float UvArea(Vector2 a, Vector2 b, Vector2 c) => Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * .5f;

        static object PngEvidence(string path)
        {
            Require(File.Exists(path), "Missing actual temporal PNG: " + path);
            byte[] b = File.ReadAllBytes(path);
            Require(b.Length >= 24 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71 && b[12] == 73 && b[13] == 72,
                "Invalid actual PNG header: " + path);
            int Read(int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
            int width = Read(16), height = Read(20);
            Require(width == 1848 && height == 1936, "Actual temporal PNG dimensions differ: " + path);
            return new { path, bytes = b.Length, width, height, sha256 = Hash(path) };
        }

        static void WriteGlyphEvidence(string directory, SignState[] signs)
        {
            var glyphs = new List<object>();
            var masks = new Dictionary<(TMP_FontAsset, uint), string>();
            foreach (var s in signs)
            {
                s.text.ForceMeshUpdate(true, true);
                foreach (var c in s.text.textInfo.characterInfo.Take(s.text.textInfo.characterCount).Where(c => c.isVisible))
                {
                    uint unicode = c.textElement.unicode;
                    var key = (c.fontAsset, unicode);
                    if (!masks.TryGetValue(key, out string mask))
                    {
                        var glyph = c.textElement.glyph; var rect = glyph.glyphRect;
                        var atlas = c.fontAsset.atlasTextures[glyph.atlasIndex];
                        mask = null;
                        if (atlas.isReadable && rect.width > 0 && rect.height > 0)
                        {
                            Color[] px = atlas.GetPixels(rect.x, rect.y, rect.width, rect.height);
                            var t = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
                            try
                            {
                                t.SetPixels(px.Select(p => p.a >= .5f ? Color.white : Color.black).ToArray()); t.Apply();
                                mask = "glyph_" + masks.Count + "_u" + unicode.ToString("X4") + ".png";
                                File.WriteAllBytes(Path.Combine(directory, mask), t.EncodeToPNG());
                            }
                            finally { UnityEngine.Object.DestroyImmediate(t); }
                        }
                        masks[key] = mask;
                    }
                    glyphs.Add(new { sign = PathOf(s.text.transform), text = s.text.text, unicode, font = c.fontAsset.name,
                        render_mode = c.fontAsset.atlasRenderMode.ToString(), point_size = c.fontAsset.faceInfo.pointSize,
                        effective_font_size = s.text.fontSize, atlas = c.textElement.glyph.atlasIndex,
                        font_atlas_contour_mask = mask, mask_threshold = .5f, mask_scope = "intrinsic font atlas contour; not rendered eye pixels",
                        world_quad = new[] { V(s.text.transform.TransformPoint(c.bottomLeft)), V(s.text.transform.TransformPoint(c.topLeft)),
                            V(s.text.transform.TransformPoint(c.topRight)), V(s.text.transform.TransformPoint(c.bottomRight)) } });
                }
            }
            Write(directory, "glyphs.json", new { schema = "research.font-glyph-mask.1", hardware_verified = false,
                glyph_count = glyphs.Count, unique_masks = masks.Count, missing_unreadable_masks = masks.Count(p => p.Value == null), glyphs });
        }
    }
}
#endif
