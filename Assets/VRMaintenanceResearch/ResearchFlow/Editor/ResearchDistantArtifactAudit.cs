#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Read-only evidence for the APK125411 "distant blur / crawling lines" FAIL: classifies suspects, changes nothing.
    // Editor renders approximate the Quest eye buffer (same geometry, materials, MSAA); they never show headset
    // sharpness, shimmer or comfort. Primitives only in JSON (Newtonsoft recurses through Vector3/Color).
    public static class ResearchDistantArtifactAudit
    {
        public static string Output = "Logs/QuestFinalIntegration/workflow-distant-20260930/distant";
        // Quest 3 recommended eye buffer at scale 1.00 (Meta render-scale docs); the compositor resamples to the 2064x2208 panel.
        const int EyeW = 1680, EyeH = 1760, PanelW = 2064, PanelH = 2208;
        const float VerticalFov = 96f; // per-eye estimate; only pixel density matters here
        static readonly string[] Stations = { "Training", "Computer", "Fan" };
        static readonly string[] HiddenRoots = { "ResearchFlowUI", "HandsLab_Quest3" };

        static float PixelsPerRadian(float scale) => EyeH * scale / 2 / Mathf.Tan(VerticalFov / 2 * Mathf.Deg2Rad);
        static float[] F(Vector3 v) => new[] { (float)Math.Round(v.x, 3), (float)Math.Round(v.y, 3), (float)Math.Round(v.z, 3) };
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        static string Write(string name, object value)
        {
            Directory.CreateDirectory(Output);
            string path = Path.GetFullPath(Path.Combine(Output, name));
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            return path;
        }

        static Transform Station(string name) => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(t => t.name == "ResearchFlow_Station_" + name);
        static Vector3 Eye(string station) => Station(station).position + Vector3.up * 1.6f;
        static Vector3[] Eyes() => Stations.Select(Eye).ToArray();

        // Participant UI and the rig are not environment; they are hidden while rendering (as in ResearchVisualAudit).
        static bool Hidden(Transform t) { for (; t; t = t.parent) if (HiddenRoots.Contains(t.name)) return true; return false; }
        static Renderer[] SceneRenderers() => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !Hidden(r.transform)).ToArray();

        static TMP_Text PracticeSign() => UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => !Hidden(t.transform) && t.text != null && t.text.ToUpperInvariant().Contains("PRACTICE")).OrderBy(t => Vector3.Distance(t.transform.position, Eye("Training"))).FirstOrDefault();

        // ---- 1. Inventory: materials, texture filtering, line-like geometry, world text, lights ----
        public static string Inventory()
        {
            var eyes = Eyes(); float ppr = PixelsPerRadian(1);
            object Texture(Material m, string property)
            {
                if (!m.HasProperty(property) || !(m.GetTexture(property) is Texture2D t)) return null;
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                var android = importer?.GetPlatformTextureSettings("Android");
                return new { t.name, t.width, t.height, mips = t.mipmapCount, filter = t.filterMode.ToString(), aniso = t.anisoLevel, mip_bias = t.mipMapBias,
                    android_format = android != null && android.overridden ? android.format.ToString() : "default", android_max = android?.maxTextureSize };
            }
            var renderers = SceneRenderers().Select(r =>
            {
                var mesh = (r as SkinnedMeshRenderer)?.sharedMesh ?? r.GetComponent<MeshFilter>()?.sharedMesh;
                var extents = mesh ? new[] { mesh.bounds.size.x * Mathf.Abs(r.transform.lossyScale.x), mesh.bounds.size.y * Mathf.Abs(r.transform.lossyScale.y), mesh.bounds.size.z * Mathf.Abs(r.transform.lossyScale.z) }.OrderBy(x => x).ToArray() : new float[] { 0, 0, 0 };
                float distance = eyes.Min(e => Mathf.Sqrt(r.bounds.SqrDistance(e)));
                // A "line" has two small extents (rail, strip, mullion). Projected at the nearest standing eye, scale 1.00.
                float secondPx = extents[1] / Mathf.Max(distance, .01f) * ppr;
                bool lineLike = extents[2] > 8 * Mathf.Max(extents[1], .001f);
                return new
                {
                    path = PathOf(r.transform), mesh = mesh ? mesh.name : null, mesh_asset = mesh ? AssetDatabase.GetAssetPath(mesh) : null, triangles = mesh ? (int)(mesh.GetIndexCount(0) / 3) : 0,
                    obb_extents = extents.Select(x => (float)Math.Round(x, 4)).ToArray(), nearest_eye_m = (float)Math.Round(distance, 2),
                    line_like = lineLike, line_width_px_at_1_00 = (float)Math.Round(secondPx, 2), sub_pixel_line = lineLike && secondPx < 1.5f,
                    casts_shadow = r.shadowCastingMode.ToString(), static_batching = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic),
                    materials = r.sharedMaterials.Where(m => m).Select(m => new
                    {
                        m.name, asset = AssetDatabase.GetAssetPath(m), shader = m.shader.name,
                        transparent = m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f, alpha_clip = m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f,
                        double_sided = m.HasProperty("_Cull") && m.GetFloat("_Cull") < .5f,
                        smoothness = m.HasProperty("_Smoothness") ? (float?)m.GetFloat("_Smoothness") : null,
                        normal_scale = m.IsKeywordEnabled("_NORMALMAP") && m.HasProperty("_BumpScale") ? (float?)m.GetFloat("_BumpScale") : null,
                        tiling = m.HasProperty("_BaseMap") ? new[] { m.GetTextureScale("_BaseMap").x, m.GetTextureScale("_BaseMap").y } : null,
                        base_map = Texture(m, "_BaseMap"), normal_map = Texture(m, "_BumpMap"), mask_map = Texture(m, "_MetallicGlossMap")
                    }).ToArray()
                };
            }).ToArray();
            var text = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(t => !Hidden(t.transform) && t.enabled).Select(t =>
            {
                float em = t.fontSize * (t is TextMeshProUGUI ? 1 : .1f) * Mathf.Abs(t.transform.lossyScale.y);
                float distance = eyes.Min(e => Vector3.Distance(e, t.transform.position));
                // Stroke ~ 0.1 em: below ~1 px the glyphs break into dashes regardless of MSAA (text is not MSAA-resolved geometry).
                return new { path = PathOf(t.transform), text = t.text.Length > 40 ? t.text.Substring(0, 40) : t.text, position = F(t.transform.position), nearest_eye_m = (float)Math.Round(distance, 2),
                    em_px_at_1_00 = (float)Math.Round(em / distance * ppr, 1), stroke_px_at_1_00 = (float)Math.Round(em * .1f / distance * ppr, 2), ugui = t is TextMeshProUGUI };
            }).OrderBy(t => t.em_px_at_1_00).ToArray();
            var sign = PracticeSign();
            var urp = QualitySettings.GetRenderPipelineAssetAt(1) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            return Write("inventory.json", new
            {
                rule = "nearest standing eye = station + 1.6 m; px at scale 1.00 = " + ppr.ToString("F0") + " px/rad (" + (ppr * Mathf.Deg2Rad).ToString("F1") + " px/deg, 1680x1760 eye, 96 deg vertical estimate)",
                stations = Stations.Select(s => new { station = s, eye = F(Eye(s)), forward = F(Station(s).forward) }).ToArray(),
                practice_sign = sign ? new { path = PathOf(sign.transform), position = F(sign.transform.position), distance_from_training_eye = Vector3.Distance(sign.transform.position, Eye("Training")) } : null,
                urp_android = urp ? new { urp.name, urp.renderScale, urp.msaaSampleCount, urp.supportsHDR, urp.shadowDistance, urp.shadowCascadeCount, urp.mainLightShadowmapResolution, urp.supportsSoftShadows } : null,
                lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l.enabled).Select(l => new { path = PathOf(l.transform), type = l.type.ToString(), mode = l.lightmapBakeType.ToString(), shadows = l.shadows.ToString(), l.shadowStrength, l.shadowBias, l.shadowNormalBias }).ToArray(),
                lightmaps = LightmapSettings.lightmaps.Length,
                counts = new { renderers = renderers.Length, sub_pixel_lines = renderers.Count(r => r.sub_pixel_line), transparent = renderers.Count(r => r.materials.Any(m => m.transparent)), alpha_clip = renderers.Count(r => r.materials.Any(m => m.alpha_clip)),
                    normal_mapped = renderers.Count(r => r.materials.Any(m => m.normal_scale != null)), world_text = text.Length },
                sub_pixel_lines = renderers.Where(r => r.sub_pixel_line).OrderBy(r => r.line_width_px_at_1_00).ToArray(),
                world_text = text, renderers
            });
        }

        // ---- 2. Coplanar / duplicate faces (z-fighting candidates) ----
        struct Tri { public int r; public Vector3 a, b, c, n; public float d; public bool twoSided; }

        public static string Coplanar(float maxSeparationMm = 6)
        {
            var eyes = Eyes();
            var renderers = SceneRenderers().Where(r => r is MeshRenderer && r.GetComponent<MeshFilter>()?.sharedMesh).ToArray();
            var tris = new List<Tri>();
            var buckets = new Dictionary<(int, int, int, int), List<int>>();
            var duplicates = new List<object>();
            var seenMeshes = new HashSet<Mesh>();
            for (int ri = 0; ri < renderers.Length; ri++)
            {
                var r = renderers[ri]; var mesh = r.GetComponent<MeshFilter>().sharedMesh; var m = r.localToWorldMatrix;
                var v = mesh.vertices; var world = v.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                bool firstUse = seenMeshes.Add(mesh);
                var faces = new Dictionary<((int, int, int), (int, int, int), (int, int, int)), List<Vector3>>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                    var material = s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null;
                    bool twoSided = material && material.HasProperty("_Cull") && material.GetFloat("_Cull") < .5f;
                    var idx = mesh.GetTriangles(s);
                    for (int i = 0; i < idx.Length; i += 3)
                    {
                        if (firstUse)
                        {
                            (int, int, int) K(Vector3 p) => (Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f)); // 0.1 mm, exact tuple
                            var keys = new[] { K(v[idx[i]]), K(v[idx[i + 1]]), K(v[idx[i + 2]]) }.OrderBy(k => k).ToArray();
                            var ln = Vector3.Cross(v[idx[i + 1]] - v[idx[i]], v[idx[i + 2]] - v[idx[i]]);
                            if (ln.sqrMagnitude > 1e-14f) { var fk = (keys[0], keys[1], keys[2]); if (!faces.TryGetValue(fk, out var list)) faces[fk] = list = new List<Vector3>(); list.Add(ln.normalized); }
                        }
                        Vector3 a = world[idx[i]], b = world[idx[i + 1]], c = world[idx[i + 2]];
                        var n = Vector3.Cross(b - a, c - a); float area = n.magnitude / 2;
                        if (area < 1e-4f) continue; // < 1 cm2
                        n /= area * 2;
                        var t = new Tri { r = ri, a = a, b = b, c = c, n = n, d = Vector3.Dot(n, a), twoSided = twoSided };
                        var key = (Mathf.RoundToInt(n.x * 100), Mathf.RoundToInt(n.y * 100), Mathf.RoundToInt(n.z * 100), Mathf.RoundToInt(t.d * 1000));
                        if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new List<int>();
                        bucket.Add(tris.Count); tris.Add(t);
                    }
                }
                if (firstUse)
                {
                    int same = faces.Values.Sum(l => l.Count - l.Select(x => new Vector3(Mathf.Round(x.x * 20), Mathf.Round(x.y * 20), Mathf.Round(x.z * 20))).Distinct().Count());
                    int backToBack = faces.Values.Count(l => l.Count > 1 && l.Any(x => l.Any(y => Vector3.Dot(x, y) < -.99f)));
                    if (same > 0 || backToBack > 0) duplicates.Add(new { mesh = mesh.name, asset = AssetDatabase.GetAssetPath(mesh), first_renderer = PathOf(r.transform), duplicate_same_facing = same, back_to_back = backToBack });
                }
            }
            var pairs = new Dictionary<(int, int), (float area, float sep, bool same, float dist, int count)>();
            int maxSep = Mathf.CeilToInt(maxSeparationMm);
            foreach (var kv in buckets)
            {
                var (nx, ny, nz, dmm) = kv.Key;
                for (int dd = 0; dd <= maxSep; dd++)
                    foreach (bool opposite in new[] { false, true })
                    {
                        var key = opposite ? (-nx, -ny, -nz, -(dmm + dd)) : (nx, ny, nz, dmm + dd);
                        if (!buckets.TryGetValue(key, out var other)) continue;
                        foreach (int i in kv.Value)
                            foreach (int j in other)
                            {
                                var A = tris[i]; var B = tris[j];
                                if (A.r == B.r || (!opposite && dd == 0 && j <= i)) continue;
                                if (opposite && !A.twoSided && !B.twoSided) continue; // back faces are culled: cannot fight
                                if (!BoxesOverlap(A, B)) continue;
                                float overlap = OverlapArea(A, B);
                                if (overlap < 1e-4f) continue;
                                var pk = A.r < B.r ? (A.r, B.r) : (B.r, A.r);
                                float sep = opposite ? Mathf.Abs(A.d + B.d) : Mathf.Abs(A.d - B.d);
                                float dist = eyes.Min(e => Vector3.Distance(e, (A.a + A.b + A.c) / 3));
                                pairs.TryGetValue(pk, out var acc);
                                pairs[pk] = (acc.area + overlap, acc.count == 0 ? sep : Mathf.Min(acc.sep, sep), !opposite, acc.count == 0 ? dist : Mathf.Min(acc.dist, dist), acc.count + 1);
                            }
                    }
            }
            var rows = pairs.Where(p => p.Value.area >= 1e-4f).OrderBy(p => p.Value.sep).ThenByDescending(p => p.Value.area).Select(p => new
            {
                a = PathOf(renderers[p.Key.Item1].transform), b = PathOf(renderers[p.Key.Item2].transform),
                material_a = renderers[p.Key.Item1].sharedMaterial ? renderers[p.Key.Item1].sharedMaterial.name : null,
                material_b = renderers[p.Key.Item2].sharedMaterial ? renderers[p.Key.Item2].sharedMaterial.name : null,
                mesh_a = renderers[p.Key.Item1].GetComponent<MeshFilter>().sharedMesh.name, mesh_b = renderers[p.Key.Item2].GetComponent<MeshFilter>().sharedMesh.name,
                separation_mm = (float)Math.Round(p.Value.sep * 1000, 2), overlap_cm2 = (float)Math.Round(p.Value.area * 1e4, 1), same_facing = p.Value.same,
                nearest_eye_m = (float)Math.Round(p.Value.dist, 2), triangle_pairs = p.Value.count,
                verdict = p.Value.sep < .0005f ? "coplanar: z-fights at any depth precision" : "near-coplanar offset (decal/overlay)"
            }).ToArray();
            return Write("coplanar.json", new
            {
                rule = "active MeshRenderers, triangles >= 1 cm2, plane normal within ~0.6 deg, separation <= " + maxSeparationMm + " mm, polygon overlap >= 1 cm2; same renderer excluded (see duplicates); opposite-facing pairs only when a material is double-sided",
                renderers = renderers.Length, triangles = tris.Count, coplanar_pairs = rows.Count(r => r.separation_mm < .5f), near_pairs = rows.Count(r => r.separation_mm >= .5f),
                pairs = rows, duplicate_faces = duplicates
            });
        }

        static bool BoxesOverlap(Tri A, Tri B)
        {
            var a = new Bounds(A.a, Vector3.zero); a.Encapsulate(A.b); a.Encapsulate(A.c); a.Expand(.008f);
            var b = new Bounds(B.a, Vector3.zero); b.Encapsulate(B.b); b.Encapsulate(B.c);
            return a.Intersects(b);
        }

        // Area of triangle A ∩ projection of triangle B onto A's plane (convex clipping).
        static float OverlapArea(Tri A, Tri B)
        {
            var u = (A.b - A.a).normalized; var w = Vector3.Cross(A.n, u);
            Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p - A.a, u), Vector3.Dot(p - A.a, w));
            var poly = new List<Vector2> { P(A.a), P(A.b), P(A.c) };
            var clip = new[] { P(B.a), P(B.b), P(B.c) };
            float Cross(Vector2 o, Vector2 x, Vector2 y) => (x.x - o.x) * (y.y - o.y) - (x.y - o.y) * (y.x - o.x);
            if (Cross(clip[0], clip[1], clip[2]) < 0) Array.Reverse(clip);
            if (Cross(poly[0], poly[1], poly[2]) < 0) poly.Reverse();
            for (int e = 0; e < 3 && poly.Count > 0; e++)
            {
                Vector2 c0 = clip[e], c1 = clip[(e + 1) % 3]; var next = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                    bool pin = Cross(c0, c1, p) >= 0, qin = Cross(c0, c1, q) >= 0;
                    if (pin) next.Add(p);
                    if (pin != qin) { float t = Cross(c0, c1, p) / (Cross(c0, c1, p) - Cross(c0, c1, q)); next.Add(p + (q - p) * t); }
                }
                poly = next;
            }
            float area = 0;
            for (int i = 0; i < poly.Count; i++) area += poly[i].x * poly[(i + 1) % poly.Count].y - poly[(i + 1) % poly.Count].x * poly[i].y;
            return Mathf.Abs(area) / 2;
        }

        // ---- 3. Temporal instability under sub-pixel head motion (per renderer) ----
        // view: "<Station>_fwd|left|right" or "Training_sign". 8 frames jittered by +/-0.5 px (scale 1.00 angle) and +/-1 mm.
        // Colour flicker = luminance std > 0.06. A flicker pixel whose object ID also changes = geometry (edge/sub-pixel/z-fight);
        // with a stable ID = in-surface (texture, normal/specular, shadow). Scale is emulated by rendering the eye buffer larger
        // and resampling to the panel (compositor); a prediction for choosing what to show the wearer, never a result.
        public static string Capture(string view, float scale = 1, int msaa = 2, bool shadows = true, bool images = false, bool reloadDirtyScene = true, bool temporalImages = false)
        {
            var scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity"), "ResearchFlow V2 in Edit Mode required.");
            Require(!scene.isDirty, "Scene must be clean: the audit reloads it from disk afterwards.");
            string station = view.Split('_')[0], dir = view.Split('_')[1];
            var eye = Eye(station); var forward = Station(station).forward;
            Quaternion look = dir == "sign" ? Quaternion.LookRotation(PracticeSign().transform.position - eye)
                : Quaternion.LookRotation(forward) * Quaternion.Euler(5, dir == "left" ? -55 : dir == "right" ? 55 : 0, 0);
            int w = Mathf.RoundToInt(EyeW * scale), h = Mathf.RoundToInt(EyeH * scale), n = w * h, frames = 8;
            float jitter = .5f / PixelsPerRadian(1) * Mathf.Rad2Deg; // same physical head motion at every scale
            var hidden = HiddenRoots.Select(GameObject.Find).Where(g => g && g.activeSelf).ToArray();
            var renderers = SceneRenderers();
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var lightShadows = lights.Select(l => l.shadows).ToArray();
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.enabled && !Hidden(c.transform)).ToArray();
            var others = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled && !(r is MeshRenderer) && !(r is SkinnedMeshRenderer) && !Hidden(r.transform)).ToArray();
            var originalMaterials = renderers.Select(r => r.sharedMaterials).ToArray();
            var glass = renderers.Where(r => r.sharedMaterials.All(m => m && m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f)).ToArray();
            int quality = QualitySettings.GetQualityLevel();
            var go = new GameObject("DistantArtifactAudit") { hideFlags = HideFlags.HideAndDontSave };
            var colorRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = msaa };
            var idRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var image = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var idMaterials = new List<Material>();
            var mean = new float[n]; var m2 = new float[n]; var id0 = new int[n]; var flipped = new bool[n]; float[] lum0 = null; int repeatDiff = 0;
            var partner = new Dictionary<(int, int), int>();
            var cov0 = new int[renderers.Length + 1]; var iouSum = new double[renderers.Length + 1];
            try
            {
                QualitySettings.SetQualityLevel(1, false); // Android default = Medium (UniversalRP-MediumQuality)
                foreach (var g in hidden) g.SetActive(false);
                if (!shadows) foreach (var l in lights) l.shadows = LightShadows.None;
                var camera = go.AddComponent<Camera>();
                camera.fieldOfView = VerticalFov; camera.aspect = (float)EyeW / EyeH; camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
                camera.allowMSAA = true; camera.allowHDR = false;
                var rng = new System.Random(20260930);
                var poses = Enumerable.Range(0, frames).Select(i => i == 0 ? (Vector3.zero, Vector2.zero) :
                    (new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * .001f,
                     new Vector2(((float)rng.NextDouble() * 2 - 1) * jitter, ((float)rng.NextDouble() * 2 - 1) * jitter))).ToArray();
                var warm = new HashSet<RenderTexture>();
                void Pose(int i) => camera.transform.SetPositionAndRotation(eye + poses[i].Item1, look * Quaternion.Euler(poses[i].Item2.y, poses[i].Item2.x, 0));
                // A new camera's first renders can miss scene geometry (observed: frame 0 = skybox + text only), so warm up every pass.
                Color32[] Read(RenderTexture rt) { camera.targetTexture = rt; if (warm.Add(rt)) for (int i = 0; i < 3; i++) camera.Render(); camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, w, h), 0, 0); RenderTexture.active = null; return image.GetPixels32(); }

                // Colour frames (Welford per pixel).
                camera.clearFlags = CameraClearFlags.Skybox;
                for (int f = 0; f < frames; f++)
                {
                    Pose(f); var px = Read(colorRt);
                    if (f == 0) lum0 = new float[n];
                    for (int p = 0; p < n; p++)
                    {
                        float y = (.2126f * px[p].r + .7152f * px[p].g + .0722f * px[p].b) / 255f;
                        if (f == 0) lum0[p] = y;
                        float delta = y - mean[p]; mean[p] += delta / (f + 1); m2[p] += delta * (y - mean[p]);
                    }
                    if (f == frames - 1)
                    {   // Control: the unjittered pose rendered again must match frame 0, otherwise "flicker" is renderer noise.
                        Pose(0); var again = Read(colorRt);
                        for (int p = 0; p < n; p++) if (Mathf.Abs((.2126f * again[p].r + .7152f * again[p].g + .0722f * again[p].b) / 255f - lum0[p]) > .02f) repeatDiff++;
                        if (images)
                        {
                            var control = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            try
                            {
                                control.SetPixels32(again);
                                File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Output).FullName, view + "_repeat_control.png"), control.EncodeToPNG());
                            }
                            finally { UnityEngine.Object.DestroyImmediate(control); }
                        }
                    }
                    if (images && (f == 0 || temporalImages))
                    {
                        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false); copy.SetPixels32(px);
                        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Output).FullName,
                            view + (temporalImages ? "_frame" + f.ToString("00") : "_color") + ".png"), copy.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(copy);
                    }
                }

                // Object-ID frames: unlit per-renderer colour (6 bits per channel), MSAA off, canvases/particles off.
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                for (int i = 0; i < renderers.Length; i++)
                {
                    int id = i + 1; var material = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetColor("_BaseColor", new Color((id & 63) / 63f, ((id >> 6) & 63) / 63f, ((id >> 12) & 63) / 63f).gamma);
                    idMaterials.Add(material);
                    renderers[i].sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, originalMaterials[i].Length)).ToArray();
                }
                foreach (var c in canvases) c.enabled = false;
                foreach (var r in others) r.enabled = false;
                // Glass would occlude the factory hall in the ID pass; hide it so flicker behind windows is attributed to what is seen.
                foreach (var r in glass) r.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                var ids = new int[n];
                for (int f = 0; f < frames; f++)
                {
                    Pose(f); var px = Read(idRt);
                    var cov = new int[renderers.Length + 1]; var inter = new int[renderers.Length + 1];
                    for (int p = 0; p < n; p++)
                    {
                        int id = Mathf.RoundToInt(px[p].r * 63 / 255f) | (Mathf.RoundToInt(px[p].g * 63 / 255f) << 6) | (Mathf.RoundToInt(px[p].b * 63 / 255f) << 12);
                        if (id > renderers.Length) id = 0;
                        ids[p] = id; cov[id]++;
                        if (f == 0) { id0[p] = id; continue; }
                        if (id == id0[p]) inter[id]++;
                        else { flipped[p] = true; var k = id0[p] < id ? (id0[p], id) : (id, id0[p]); partner.TryGetValue(k, out int c); partner[k] = c + 1; }
                    }
                    if (f == 0) { Array.Copy(cov, cov0, cov.Length); continue; }
                    for (int id = 1; id <= renderers.Length; id++)
                        if (cov0[id] + cov[id] > 0) iouSum[id] += (double)inter[id] / (cov0[id] + cov[id] - inter[id]);
                }
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].sharedMaterials = originalMaterials[i];
                foreach (var c in canvases) if (c) c.enabled = true;
                foreach (var r in others) if (r) r.enabled = true;
                foreach (var r in glass) if (r) r.enabled = true;
                for (int i = 0; i < lights.Length; i++) lights[i].shadows = lightShadows[i];
                foreach (var g in hidden) g.SetActive(true);
                QualitySettings.SetQualityLevel(quality, false);
                UnityEngine.Object.DestroyImmediate(go); colorRt.Release(); idRt.Release();
                UnityEngine.Object.DestroyImmediate(colorRt); UnityEngine.Object.DestroyImmediate(idRt); UnityEngine.Object.DestroyImmediate(image);
                foreach (var m in idMaterials) UnityEngine.Object.DestroyImmediate(m);
            }
            if (scene.isDirty && !reloadDirtyScene)
                throw new InvalidOperationException("Capture dirtied the scene; reload refused to preserve caller state.");
            string reload = scene.isDirty ? ResearchVisualQualityPass.ReloadActiveSceneFromDisk() : "scene stayed clean";

            if (repeatDiff != 0)
            {
                string invalid = Write(view + "_invalid-repeat-control.json", new
                {
                    status = "INVALID_REPEAT_CONTROL", view, scale, msaa, shadows, frames,
                    eye_size = new[] { w, h }, repeat_render_diff_px = repeatDiff, repeat_control_luma_threshold = .02f,
                    reload, hardware_verified = false,
                    reason = "Same unjittered pose differs from frame00; capture startup/render instability cannot be attributed to head-motion flicker.",
                    repeat_control_image = images ? Path.GetFullPath(Path.Combine(Output, view + "_repeat_control.png")) : null,
                    metrics_emitted = false
                });
                throw new InvalidOperationException("DISTANT_CAPTURE_INVALID_REPEAT_CONTROL " + invalid);
            }

            // Per-pixel classification and per-renderer attribution.
            const float Threshold = .06f;
            var geometry = new int[renderers.Length + 1]; var surface = new int[renderers.Length + 1]; var thin = new int[renderers.Length + 1];
            var distance = new float[renderers.Length + 1]; distance[0] = float.PositiveInfinity;
            for (int i = 0; i < renderers.Length; i++) distance[i + 1] = Mathf.Sqrt(renderers[i].bounds.SqrDistance(eye));
            int geometryTotal = 0, surfaceTotal = 0, farPixels = 0, farGeometry = 0, farSurface = 0;
            var heat = images ? new Color32[n] : null;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x, id = id0[p];
                    int same = (x > 0 && id0[p - 1] == id ? 1 : 0) + (x < w - 1 && id0[p + 1] == id ? 1 : 0) + (y > 0 && id0[p - w] == id ? 1 : 0) + (y < h - 1 && id0[p + w] == id ? 1 : 0);
                    if (id > 0 && same <= 1) thin[id]++;
                    bool flicker = Mathf.Sqrt(m2[p] / (frames - 1)) > Threshold;
                    bool far = distance[id] > 5; if (far) farPixels++;
                    if (flicker && flipped[p]) { geometry[id]++; geometryTotal++; if (far) farGeometry++; }
                    else if (flicker) { surface[id]++; surfaceTotal++; if (far) farSurface++; }
                    if (heat != null) { byte g = (byte)(lum0[p] * 110); heat[p] = flicker ? (flipped[p] ? new Color32(255, 40, 40, 255) : new Color32(40, 170, 255, 255)) : flipped[p] ? new Color32(g, (byte)Mathf.Min(255, g + 70), g, 255) : new Color32(g, g, g, 255); }
                }
            if (heat != null)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels32(heat);
                File.WriteAllBytes(Path.Combine(Output, view + "_s" + scale.ToString("F2") + "_msaa" + msaa + (shadows ? "" : "_noshadow") + "_heat.png"), t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
            }
            // Far-field sharpness in panel space (bilinear resample = compositor): mean gradient magnitude over pixels of objects > 5 m.
            double gradient = 0; int gradientCount = 0;
            float Sample(float u, float v)
            {
                float fx = Mathf.Clamp(u * w - .5f, 0, w - 1.001f), fy = Mathf.Clamp(v * h - .5f, 0, h - 1.001f); int x0 = (int)fx, y0 = (int)fy; float ax = fx - x0, ay = fy - y0;
                return Mathf.Lerp(Mathf.Lerp(lum0[y0 * w + x0], lum0[y0 * w + x0 + 1], ax), Mathf.Lerp(lum0[(y0 + 1) * w + x0], lum0[(y0 + 1) * w + x0 + 1], ax), ay);
            }
            for (int y = 1; y < PanelH - 1; y += 2)
                for (int x = 1; x < PanelW - 1; x += 2)
                {
                    float u = (x + .5f) / PanelW, v = (y + .5f) / PanelH;
                    if (distance[id0[Mathf.Min(h - 1, (int)(v * h)) * w + Mathf.Min(w - 1, (int)(u * w))]] <= 5) continue;
                    float gx = Sample(u + 1f / PanelW, v) - Sample(u - 1f / PanelW, v), gy = Sample(u, v + 1f / PanelH) - Sample(u, v - 1f / PanelH);
                    gradient += Mathf.Sqrt(gx * gx + gy * gy); gradientCount++;
                }
            object Row(int id) => new
            {
                path = PathOf(renderers[id - 1].transform), material = renderers[id - 1].sharedMaterial ? renderers[id - 1].sharedMaterial.name : null,
                mesh = renderers[id - 1] is SkinnedMeshRenderer skinned ? skinned.sharedMesh?.name : renderers[id - 1].GetComponent<MeshFilter>().sharedMesh?.name, distance_m = (float)Math.Round(distance[id], 1), pixels = cov0[id],
                geometry_flicker_px = geometry[id], surface_flicker_px = surface[id], one_px_wide_fraction = cov0[id] == 0 ? 0 : (float)Math.Round((double)thin[id] / cov0[id], 3),
                mask_iou = (float)Math.Round(iouSum[id] / (frames - 1), 3)
            };
            var visible = Enumerable.Range(1, renderers.Length).Where(id => cov0[id] > 0).ToArray();
            return Write(view + "_s" + scale.ToString("F2") + "_msaa" + msaa + (shadows ? "" : "_noshadow") + ".json", new
            {
                status = "VALID_REPEAT_CONTROL", repeat_control_luma_threshold = .02f,
                view, eye = F(eye), look = F(look * Vector3.forward), scale, eye_size = new[] { w, h }, msaa, shadows, frames, jitter_deg = jitter, jitter_mm = 1, threshold = Threshold,
                quality = "Medium (Android default)", reload, hardware_verified = false,
                totals = new { pixels = n, repeat_render_diff_px = repeatDiff, geometry_flicker_px = geometryTotal, surface_flicker_px = surfaceTotal, far_pixels = farPixels, far_geometry_flicker_px = farGeometry, far_surface_flicker_px = farSurface,
                    far_panel_gradient = gradientCount == 0 ? 0 : (float)Math.Round(gradient / gradientCount, 5) },
                top_geometry = visible.OrderByDescending(id => geometry[id]).Take(30).Select(Row).ToArray(),
                top_surface = visible.OrderByDescending(id => surface[id]).Take(20).Select(Row).ToArray(),
                unstable_small = visible.Where(id => cov0[id] >= 20 && iouSum[id] / (frames - 1) < .6).OrderBy(id => iouSum[id]).Take(30).Select(Row).ToArray(),
                top_flip_pairs = partner.OrderByDescending(p => p.Value).Take(25).Select(p => new { a = p.Key.Item1 == 0 ? "(background)" : PathOf(renderers[p.Key.Item1 - 1].transform), b = PathOf(renderers[p.Key.Item2 - 1].transform), flips = p.Value }).ToArray(),
                // Full lists so coplanar.json pairs can be checked for on-screen visibility and flipping.
                all_flip_pairs = partner.Where(p => p.Value >= 3 && p.Key.Item1 > 0).Select(p => new { a = PathOf(renderers[p.Key.Item1 - 1].transform), b = PathOf(renderers[p.Key.Item2 - 1].transform), flips = p.Value }).ToArray(),
                coverage = visible.ToDictionary(id => PathOf(renderers[id - 1].transform) + "#" + id, id => cov0[id])
            });
        }

        // ---- 4. Error against a supersampled reference, in panel space ----
        // Each jittered frame is compared with a 2x-per-axis MSAA 8x render of the same pose, both resampled to the 2064x2208
        // panel. Mean |error| = softness + aliasing; per-pixel std of the error over frames = crawl that follows head motion
        // (edge motion that the reference also shows cancels out). Editor prediction only.
        public static string CaptureError(string view, float scale = 1, int msaa = 2)
        {
            var scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity") && !scene.isDirty, "Clean ResearchFlow V2 in Edit Mode required.");
            string station = view.Split('_')[0], dir = view.Split('_')[1];
            var eye = Eye(station);
            Quaternion look = dir == "sign" ? Quaternion.LookRotation(PracticeSign().transform.position - eye)
                : Quaternion.LookRotation(Station(station).forward) * Quaternion.Euler(5, dir == "left" ? -55 : dir == "right" ? 55 : 0, 0);
            int w = Mathf.RoundToInt(EyeW * scale), h = Mathf.RoundToInt(EyeH * scale), rw = EyeW * 2, rh = EyeH * 2, pn = PanelW * PanelH, frames = 8;
            float jitter = .5f / PixelsPerRadian(1) * Mathf.Rad2Deg;
            var hidden = HiddenRoots.Select(GameObject.Find).Where(g => g && g.activeSelf).ToArray();
            var renderers = SceneRenderers();
            int quality = QualitySettings.GetQualityLevel();
            var go = new GameObject("DistantArtifactError") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = msaa };
            var refRt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            var errMean = new float[pn]; var errM2 = new float[pn]; double absError = 0; int farCount = 0; int repeatDiff = 0;
            var panelId = new int[pn]; var distance = new float[renderers.Length + 1]; distance[0] = float.PositiveInfinity;
            for (int i = 0; i < renderers.Length; i++) distance[i + 1] = Mathf.Sqrt(renderers[i].bounds.SqrDistance(eye));
            float[] first = null;
            try
            {
                QualitySettings.SetQualityLevel(1, false);
                foreach (var g in hidden) g.SetActive(false);
                var camera = go.AddComponent<Camera>();
                camera.fieldOfView = VerticalFov; camera.aspect = (float)EyeW / EyeH; camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
                camera.allowMSAA = true; camera.allowHDR = false; camera.clearFlags = CameraClearFlags.Skybox;
                var rng = new System.Random(20260930);
                var poses = Enumerable.Range(0, frames).Select(i => i == 0 ? (Vector3.zero, Vector2.zero) :
                    (new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * .001f,
                     new Vector2(((float)rng.NextDouble() * 2 - 1) * jitter, ((float)rng.NextDouble() * 2 - 1) * jitter))).ToArray();
                var warm = new HashSet<RenderTexture>();
                float[] Luma(RenderTexture target, int tw, int th)
                {
                    camera.targetTexture = target; if (warm.Add(target)) for (int i = 0; i < 3; i++) camera.Render(); camera.Render();
                    var image = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, tw, th), 0, 0); RenderTexture.active = null;
                    var px = image.GetPixels32(); UnityEngine.Object.DestroyImmediate(image);
                    var y = new float[px.Length]; for (int p = 0; p < px.Length; p++) y[p] = (.2126f * px[p].r + .7152f * px[p].g + .0722f * px[p].b) / 255f; return y;
                }
                float Bilinear(float[] img, int iw, int ih, float u, float v)
                {
                    float fx = Mathf.Clamp(u * iw - .5f, 0, iw - 1.001f), fy = Mathf.Clamp(v * ih - .5f, 0, ih - 1.001f); int x0 = (int)fx, y0 = (int)fy; float ax = fx - x0, ay = fy - y0;
                    return Mathf.Lerp(Mathf.Lerp(img[y0 * iw + x0], img[y0 * iw + x0 + 1], ax), Mathf.Lerp(img[(y0 + 1) * iw + x0], img[(y0 + 1) * iw + x0 + 1], ax), ay);
                }
                for (int f = 0; f < frames; f++)
                {
                    camera.transform.SetPositionAndRotation(eye + poses[f].Item1, look * Quaternion.Euler(poses[f].Item2.y, poses[f].Item2.x, 0));
                    var render = Luma(rt, w, h); var reference = Luma(refRt, rw, rh);
                    if (f == 0) first = render;
                    for (int y = 0; y < PanelH; y++)
                        for (int x = 0; x < PanelW; x++)
                        {
                            float u = (x + .5f) / PanelW, v = (y + .5f) / PanelH, du = .25f / PanelW, dv = .25f / PanelH;
                            // Compositor: bilinear from the eye buffer. Reference: 2x2 taps over the panel pixel footprint.
                            float r = Bilinear(render, w, h, u, v);
                            float g = (Bilinear(reference, rw, rh, u - du, v - dv) + Bilinear(reference, rw, rh, u + du, v - dv) + Bilinear(reference, rw, rh, u - du, v + dv) + Bilinear(reference, rw, rh, u + du, v + dv)) / 4;
                            int p = y * PanelW + x; float e = r - g;
                            float delta = e - errMean[p]; errMean[p] += delta / (f + 1); errM2[p] += delta * (e - errMean[p]);
                            absError += Mathf.Abs(e);
                        }
                    if (f == frames - 1)
                    {
                        camera.transform.SetPositionAndRotation(eye, look);
                        var again = Luma(rt, w, h); for (int p = 0; p < again.Length; p++) if (Mathf.Abs(again[p] - first[p]) > .02f) repeatDiff++;
                    }
                }
                // Attribution: one unjittered ID frame at eye resolution (unlit per-renderer colour, glass hidden).
                var materials = renderers.Select(r => r.sharedMaterials).ToArray();
                var glass = renderers.Where(r => r.sharedMaterials.All(m => m && m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f)).ToArray();
                var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.enabled && !Hidden(c.transform)).ToArray();
                var unlit = Shader.Find("Universal Render Pipeline/Unlit"); var temp = new List<Material>();
                var idRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                try
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        int id = i + 1; var m = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave }; temp.Add(m);
                        m.SetColor("_BaseColor", new Color((id & 63) / 63f, ((id >> 6) & 63) / 63f, ((id >> 12) & 63) / 63f).gamma);
                        renderers[i].sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, materials[i].Length)).ToArray();
                    }
                    foreach (var r in glass) r.enabled = false; foreach (var c in canvases) c.enabled = false;
                    camera.transform.SetPositionAndRotation(eye, look); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                    camera.targetTexture = idRt; for (int i = 0; i < 4; i++) camera.Render();
                    var image = new Texture2D(w, h, TextureFormat.RGBA32, false); RenderTexture.active = idRt; image.ReadPixels(new Rect(0, 0, w, h), 0, 0); RenderTexture.active = null;
                    var px = image.GetPixels32(); UnityEngine.Object.DestroyImmediate(image);
                    for (int y = 0; y < PanelH; y++)
                        for (int x = 0; x < PanelW; x++)
                        {
                            var c = px[Mathf.Min(h - 1, (int)((y + .5f) / PanelH * h)) * w + Mathf.Min(w - 1, (int)((x + .5f) / PanelW * w))];
                            int id = Mathf.RoundToInt(c.r * 63 / 255f) | (Mathf.RoundToInt(c.g * 63 / 255f) << 6) | (Mathf.RoundToInt(c.b * 63 / 255f) << 12);
                            panelId[y * PanelW + x] = id > renderers.Length ? 0 : id;
                        }
                }
                finally
                {
                    for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].sharedMaterials = materials[i];
                    foreach (var r in glass) if (r) r.enabled = true; foreach (var c in canvases) if (c) c.enabled = true;
                    foreach (var m in temp) UnityEngine.Object.DestroyImmediate(m); idRt.Release(); UnityEngine.Object.DestroyImmediate(idRt);
                }
            }
            finally
            {
                foreach (var g in hidden) g.SetActive(true);
                QualitySettings.SetQualityLevel(quality, false);
                UnityEngine.Object.DestroyImmediate(go); rt.Release(); refRt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(refRt);
            }
            string reload = scene.isDirty ? ResearchVisualQualityPass.ReloadActiveSceneFromDisk() : "scene stayed clean";
            const float CrawlThreshold = .03f;
            var crawl = new int[renderers.Length + 1]; var cover = new int[renderers.Length + 1]; int crawlTotal = 0, crawlFar = 0;
            for (int p = 0; p < pn; p++)
            {
                int id = panelId[p]; cover[id]++;
                bool far = distance[id] > 5; if (far) farCount++;
                if (Mathf.Sqrt(errM2[p] / (frames - 1)) > CrawlThreshold) { crawl[id]++; crawlTotal++; if (far) crawlFar++; }
            }
            var byMaterial = Enumerable.Range(1, renderers.Length).Where(id => crawl[id] > 0).GroupBy(id => renderers[id - 1].sharedMaterial ? renderers[id - 1].sharedMaterial.name : "(none)")
                .Select(g => new { material = g.Key, crawl_px = g.Sum(id => crawl[id]), far_crawl_px = g.Where(id => distance[id] > 5).Sum(id => crawl[id]), pixels = g.Sum(id => cover[id]) })
                .OrderByDescending(r => r.crawl_px).Take(12).ToArray();
            return Write("error_" + view + "_s" + scale.ToString("F2") + "_msaa" + msaa + ".json", new
            {
                view, scale, msaa, eye_size = new[] { w, h }, reference = new { size = new[] { rw, rh }, msaa = 8 }, panel = new[] { PanelW, PanelH }, frames, crawl_threshold = CrawlThreshold,
                repeat_render_diff_px = repeatDiff, reload, hardware_verified = false,
                mean_abs_error = (float)Math.Round(absError / (pn * (double)frames), 5), crawl_px = crawlTotal, far_pixels = farCount, far_crawl_px = crawlFar,
                by_material = byMaterial,
                top = Enumerable.Range(1, renderers.Length).Where(id => crawl[id] > 0).OrderByDescending(id => crawl[id]).Take(20).Select(id => new
                { path = PathOf(renderers[id - 1].transform), material = renderers[id - 1].sharedMaterial ? renderers[id - 1].sharedMaterial.name : null, distance_m = (float)Math.Round(distance[id], 1), crawl_px = crawl[id], pixels = cover[id] }).ToArray()
            });
        }

        public static string CaptureErrorView(string view) => string.Join("\n", new[] { CaptureError(view, 1, 2), CaptureError(view, 1, 4), CaptureError(view, 1.10f, 2), CaptureError(view, 1.15f, 2) });

        // Runs every station view at the baseline plus the one-variable editor predictions.
        public static string CaptureView(string view)
        {
            var results = new List<string>
            {
                Capture(view, 1, 2, true, true), Capture(view, 1, 2, false), Capture(view, 1, 4, true),
                Capture(view, 1.10f, 2, true), Capture(view, 1.15f, 2, true)
            };
            return string.Join("\n", results);
        }
    }
}
#endif
