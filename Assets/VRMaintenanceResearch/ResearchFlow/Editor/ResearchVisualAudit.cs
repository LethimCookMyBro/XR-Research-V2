using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Read-only audit of world labels, lighting and key materials in the open ResearchFlow scene.
    public static class ResearchVisualAudit
    {
        public const string Output = "Logs/QuestFinalIntegration/continuation-20260928b";
        // Meta typography: 14 px minimum, 18 px comfortable; UI Set px ~ mm at 1 m.
        const float MinimumDegrees = .80f, ComfortableDegrees = 1.03f;

        static string Write(string name, object value)
        {
            Directory.CreateDirectory(Output);
            string path = Path.GetFullPath(Path.Combine(Output, name));
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }));
            return path;
        }

        // Primitives only: Newtonsoft recurses forever through Vector3.normalized / Color.linear.
        static float[] F(Vector3 v) => new[] { v.x, v.y, v.z };
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        // Standing eye points: the three stations and every neutral teleport destination.
        static Vector3[] Viewpoints() => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("ResearchFlow_Station_") || (t.parent && t.parent.name == "NeutralRoomDestinations"))
            .Select(t => t.position + Vector3.up * 1.6f).ToArray();

        public static string WorldText()
        {
            var views = Viewpoints();
            var rows = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => !t.GetComponentInParent<ResearchFlowView>(true) && !PathOf(t.transform).Contains("ResearcherSetup") && !PathOf(t.transform).Contains("Researcher Display"))
                .Select(t =>
                {
                    var canvas = t.GetComponentInParent<Canvas>(true);
                    float em = t.fontSize * (t is TextMeshProUGUI ? 1 : .1f) * Mathf.Abs(t.transform.lossyScale.y);
                    var center = t.transform.position;
                    float distance = views.Length == 0 ? float.NaN : views.Min(v => Vector3.Distance(v, center));
                    float degrees = 2 * Mathf.Atan2(em / 2, distance) * Mathf.Rad2Deg;
                    var renderer = t.GetComponent<Renderer>();
                    bool visible = t.gameObject.activeInHierarchy && t.enabled && (renderer == null || renderer.enabled) && (canvas == null || canvas.enabled);
                    return new { path = PathOf(t.transform), text = t.text.Length > 60 ? t.text.Substring(0, 60) : t.text, visible, em_metres = em, nearest_viewpoint_metres = distance,
                        em_degrees = degrees, verdict = !visible ? "hidden" : degrees >= ComfortableDegrees ? "comfortable" : degrees >= MinimumDegrees ? "minimum" : "below_minimum" };
                }).OrderBy(r => r.em_degrees).ToArray();
            return Write("world-text-audit.json", new { rule = "em angle from nearest standing eye point (station/teleport destination + 1.6 m); Meta 14 px min ~0.80 deg, 18 px comfortable ~1.03 deg",
                viewpoints = views.Length, visible = rows.Count(r => r.visible), below_minimum_visible = rows.Count(r => r.visible && r.verdict == "below_minimum"), rows });
        }

        // Baked mesh lettering (not TMP): glyph-row height ~ renderer bounds height.
        public static string MeshLabels()
        {
            var views = Viewpoints();
            var rows = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.name.Contains("Label") || r.name.Contains("Title") || r.name.Contains("Subtitle") || r.name.Contains("Sign"))
                .Where(r => !r.GetComponent<TMP_Text>())
                .Select(r =>
                {
                    float height = r.bounds.size.y, distance = views.Min(v => Vector3.Distance(v, r.bounds.center));
                    return new { path = PathOf(r.transform), enabled = r.enabled && r.gameObject.activeInHierarchy, height_metres = height, nearest_viewpoint_metres = distance,
                        degrees = 2 * Mathf.Atan2(height / 2, distance) * Mathf.Rad2Deg };
                }).OrderBy(r => r.degrees).ToArray();
            return Write("mesh-label-audit.json", rows);
        }

        // Edit-mode renders from each station eye point (1.6 m, 12 deg down, station forward) for before/after review.
        public static string CaptureStations(string label)
        {
            string folder = Path.GetFullPath(Path.Combine(Output, "views"));
            Directory.CreateDirectory(folder);
            var go = new GameObject("VisualAuditCapture") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.fieldOfView = 70; camera.nearClipPlane = .01f; camera.farClipPlane = 1000; camera.targetTexture = rt;
                var hidden = new[] { GameObject.Find("ResearchFlowUI"), GameObject.Find("HandsLab_Quest3") }.Where(g => g && g.activeSelf).ToArray();
                foreach (var g in hidden) g.SetActive(false);
                try
                {
                    foreach (var station in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name.StartsWith("ResearchFlow_Station_")))
                        foreach (var (suffix, yaw, pitch) in new[] { ("", 0f, 12f), ("_left", -55f, 8f), ("_right", 55f, 8f) })
                        {
                            camera.transform.SetPositionAndRotation(station.position + Vector3.up * 1.6f, Quaternion.LookRotation(station.forward) * Quaternion.Euler(pitch, yaw, 0));
                            camera.Render();
                            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); RenderTexture.active = null;
                            File.WriteAllBytes(Path.Combine(folder, label + "_" + station.name.Replace("ResearchFlow_Station_", "") + suffix + ".png"), image.EncodeToPNG());
                        }
                }
                finally { foreach (var g in hidden) g.SetActive(true); }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); }
            return folder;
        }

        public static string Renderers(string rootName)
        {
            var root = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == rootName);
            return Write("renderers-" + rootName + ".json", root.GetComponentsInChildren<Renderer>(true).Select(r => new { path = PathOf(r.transform), enabled = r.enabled && r.gameObject.activeInHierarchy,
                size = F(r.bounds.size), min = F(r.bounds.min), materials = r.sharedMaterials.Where(m => m).Select(m => m.name).ToArray() }).OrderByDescending(r => r.size[0] * r.size[2]).ToArray());
        }

        public static string Lighting()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(l => new
            {
                path = PathOf(l.transform), active = l.isActiveAndEnabled, type = l.type.ToString(), mode = l.lightmapBakeType.ToString(), color = "#" + ColorUtility.ToHtmlStringRGB(l.color),
                l.intensity, l.range, shadows = l.shadows.ToString(), l.shadowStrength, rotation = F(l.transform.eulerAngles)
            }).ToArray();
            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(p => new { path = PathOf(p.transform), mode = p.mode.ToString(), p.resolution, size = F(p.size), baked = p.bakedTexture != null }).ToArray();
            return Write("lighting-audit.json", new
            {
                ambient = new { mode = RenderSettings.ambientMode.ToString(), sky = Hex(RenderSettings.ambientSkyColor), equator = Hex(RenderSettings.ambientEquatorColor), ground = Hex(RenderSettings.ambientGroundColor), RenderSettings.ambientIntensity, flat = Hex(RenderSettings.ambientLight) },
                reflection = new { mode = RenderSettings.defaultReflectionMode.ToString(), RenderSettings.reflectionIntensity, RenderSettings.defaultReflectionResolution, skybox = RenderSettings.skybox ? RenderSettings.skybox.name : null },
                fog = new { RenderSettings.fog, mode = RenderSettings.fogMode.ToString(), color = Hex(RenderSettings.fogColor), RenderSettings.fogStartDistance, RenderSettings.fogEndDistance },
                lightmaps = LightmapSettings.lightmaps.Length, lights, probes,
                urp = pipeline ? new { name = pipeline.name, pipeline.shadowDistance, pipeline.shadowCascadeCount, pipeline.mainLightShadowmapResolution, pipeline.supportsSoftShadows,
                    pipeline.msaaSampleCount, pipeline.renderScale, pipeline.supportsHDR, additionalLightsRenderingMode = pipeline.additionalLightsRenderingMode.ToString(), pipeline.maxAdditionalLightsCount, pipeline.supportsAdditionalLightShadows } : null,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()]
            });
        }

        // Key surfaces the wearer reported as flat: vehicles, tables, workstations, floors, walls.
        public static string Materials()
        {
            string[] keys = { "car", "vehicle", "table", "desk", "bench", "workstation", "station", "floor", "wall", "cabinet", "pc", "tower", "fan" };
            var rows = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(r => keys.Any(k => PathOf(r.transform).ToLowerInvariant().Contains(k)))
                .SelectMany(r => r.sharedMaterials.Where(m => m).Select(m => new { renderer = PathOf(r.transform), material = m.name, asset = AssetDatabase.GetAssetPath(m), shader = m.shader.name,
                    color = m.HasProperty("_BaseColor") ? "#" + ColorUtility.ToHtmlStringRGBA(m.GetColor("_BaseColor")) : null,
                    metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1, smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1,
                    map = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : null, casts = r.shadowCastingMode.ToString(), receives = r.receiveShadows,
                    size = F(r.bounds.size), min_y = r.bounds.min.y })).ToArray();
            return Write("materials-audit.json", rows);
        }
    }
}
