using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Opt-in, technical-only Android ENV diagnostics. Never moves an XR camera or starts a session.
    // Mono render requests use the shipped meshes/materials/lights. They are not wearer or app-FPS evidence.
    public sealed class ResearchEnvironmentNativeCapture : MonoBehaviour
    {
        public const string CommandFile = "environment-capture-command.json";
        public const string ResponseFile = "environment-capture-response.json";
        const int CaptureLayer = 31;
        const string CanonicalScene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        static readonly string[] EnvironmentRoots = { "Environment_V2", "ResearchFlow_EnvironmentSecondPass", "ResearchFlow_EnvironmentDepthPass" };
        ResearchSessionFlowController controller;
        float nextPoll;
        bool busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Application.identifier.EndsWith(".technical", StringComparison.Ordinal) || Application.platform != RuntimePlatform.Android) return;
            var owner = FindFirstObjectByType<ResearchSessionFlowController>();
            if (!owner) return;
            var host = new GameObject("ResearchEnvironmentNativeCapture") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<ResearchEnvironmentNativeCapture>().controller = owner;
        }

        public static string RequestRefusal(string identity, string command, string id, double ageSeconds, bool idle)
        {
            if (identity == null || !identity.EndsWith(".technical", StringComparison.Ordinal)) return "Technical package required";
            if (command != "capture") return "Only capture is supported";
            if (id == null || !Regex.IsMatch(id, "\\A[a-z0-9-]{1,80}\\z", RegexOptions.CultureInvariant)) return "Invalid request id";
            if (double.IsNaN(ageSeconds) || double.IsInfinity(ageSeconds) || ageSeconds < -5 || ageSeconds > 30) return "Request expired or future-dated";
            if (!idle) return "Idle ResearcherSetup without a participant/session is required";
            return null;
        }

        bool Idle => controller && controller.Flow == null && controller.TechnicalSelfSetup
            && controller.Record == null && !controller.TechnicalSelfTest && !controller.Transitioning
            && string.IsNullOrEmpty(controller.Error) && !(controller.RenderComparison && controller.RenderComparison.Running)
            && !(controller.GetComponent<ResearchTechnicalScenario>()?.Running ?? false);

        void Update()
        {
            if (busy || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .5f;
            string path = Path.Combine(Application.persistentDataPath, CommandFile);
            if (!File.Exists(path)) return;
            string raw;
            try { raw = File.ReadAllText(path); File.Delete(path); }
            catch (IOException) { return; }
            string id = null;
            busy = true;
            try
            {
                if (raw.Length > 1024) throw new InvalidOperationException("Request too large");
                var request = JObject.Parse(raw);
                id = (string)request["request_id"];
                if (!DateTimeOffset.TryParse((string)request["issued_utc"], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var issued)) throw new InvalidOperationException("issued_utc required");
                string refusal = RequestRefusal(Application.identifier, (string)request["command"], id, (DateTimeOffset.UtcNow - issued).TotalSeconds, Idle);
                if (refusal != null) throw new InvalidOperationException(refusal);
                string profile = (string)request["profile"] ?? "baseline";
                if (request["page"] != null && request["page"].Type != JTokenType.Integer) throw new InvalidOperationException("page must be an integer");
                int page = (int?)request["page"] ?? 0;
                string report = Capture(id, profile, page);
                Respond(id, true, null, report);
            }
            catch (Exception error) { Respond(id, false, error.Message, null); }
            finally { busy = false; }
        }

        void Respond(string id, bool ok, string error, string report)
        {
            File.WriteAllText(Path.Combine(Application.persistentDataPath, ResponseFile), JsonConvert.SerializeObject(new
            { request_id = id, response_created_utc = DateTime.UtcNow.ToString("O"), ok, error, report, hardware_verified = false }, Formatting.Indented));
        }

        sealed class Pose
        {
            public string name; public Vector3 position, lookAt;
            public Pose(string n, Vector3 p, Vector3 t) { name = n; position = p; lookAt = t; }
        }
        static Pose[] Poses() => new[] {
            new Pose("training", new Vector3(9.7f,1.65f,1.1f), new Vector3(9.7f,1.1f,6.5f)),
            new Pose("computer", new Vector3(0,1.65f,1.1f), new Vector3(0,1.1f,7.5f)),
            new Pose("corridor", new Vector3(12,1.65f,-1.9f), new Vector3(-9,1.65f,-1.9f)),
            new Pose("factory", new Vector3(8,1.65f,-4.5f), new Vector3(-3,1.1f,-16)),
            new Pose("training-far", new Vector3(9.7f,1.65f,0), new Vector3(9.7f,1.65f,7.5f)),
            new Pose("computer-far", new Vector3(0,1.65f,0), new Vector3(0,1.65f,8.5f)),
            new Pose("floor-oblique", new Vector3(2,1.65f,-1.9f), new Vector3(-8,.05f,-1.9f)),
            new Pose("factory-detail", new Vector3(0,1.65f,-8), new Vector3(0,1,-15)) };

        public static string PageRefusal(string profile, int page, int total)
        {
            if (profile != "baseline" && profile != "full") return "Unknown capture profile";
            if (total < 1 || total > 512 || (profile == "baseline" && total != 8)) return "Invalid bounded pose plan";
            if (page < 0 || page >= (total + 7) / 8) return "Capture page outside reviewed plan";
            return null;
        }
        static Pose[] SelectPoses(string profile, int page, out int total, out string planHash)
        {
            if (profile != "baseline" && profile != "full") throw new InvalidOperationException("Unknown capture profile");
            Pose[] poses;
            planHash = null;
            if (profile == "baseline") poses = Poses();
            else
            {
                var asset = Resources.Load<TextAsset>("ResearchEnvironmentFullCapturePlan");
                if (!asset) throw new InvalidOperationException("Reviewed full ENV capture plan required");
                var plan = JObject.Parse(asset.text);
                if ((int?)plan["version"] != 1 || (string)plan["scene"] != CanonicalScene) throw new InvalidOperationException("Wrong full ENV plan");
                var items = plan["poses"] as JArray;
                if (items == null || items.Count < 1 || items.Count > 512) throw new InvalidOperationException("Invalid bounded full plan");
                var names = new HashSet<string>(StringComparer.Ordinal);
                poses = items.Select(item =>
                {
                    string name = (string)item["name"];
                    if (name == null || !Regex.IsMatch(name,"\\A[a-z0-9-]{1,80}\\z",RegexOptions.CultureInvariant) || !names.Add(name)) throw new InvalidOperationException("Invalid or duplicate full pose name");
                    Vector3 Read(string key)
                    {
                        var values = item[key] as JArray;
                        if (values == null || values.Count != 3) throw new InvalidOperationException("Three pose coordinates required");
                        var v = values.Select(x => (float)x).ToArray();
                        if (v.Any(x => float.IsNaN(x) || float.IsInfinity(x) || Math.Abs(x) > 100)) throw new InvalidOperationException("Unreviewed pose coordinate");
                        return new Vector3(v[0],v[1],v[2]);
                    }
                    var position = Read("position"); var target = Read("look_at");
                    if ((target-position).sqrMagnitude < .01f || position.y < .8f || position.y > 2.2f) throw new InvalidOperationException("Invalid diagnostic head position/direction");
                    return new Pose(name,position,target);
                }).ToArray();
                planHash = Sha(System.Text.Encoding.UTF8.GetBytes(asset.text));
            }
            total = poses.Length;
            string refusal = PageRefusal(profile,page,total);
            if (refusal != null) throw new InvalidOperationException(refusal);
            return poses.Skip(page*8).Take(8).ToArray();
        }

        static float[] XYZ(Vector3 v) => new[] { v.x, v.y, v.z };
        public static int[] SubmeshIndices(int total, int start, int originalCount, int materialCount, bool batched)
        {
            if (originalCount <= 0 || materialCount != originalCount || start < 0 || (long)start + originalCount > total
                || (!batched && (start != 0 || total != originalCount))) throw new InvalidOperationException("Unreviewed mesh/material slice mapping");
            return Enumerable.Range(start, originalCount).ToArray();
        }
        public static bool ValidScenePixels(Color32[] samples)
        {
            return samples != null && samples.Length > 1 && samples.Any(p=>p.a>0)
                && samples.Any(p=>p.r!=samples[0].r || p.g!=samples[0].g || p.b!=samples[0].b || p.a!=samples[0].a);
        }
        public static bool ValidScenePixels(Texture2D image)
        {
            if (!image || !image.isReadable || image.format != TextureFormat.RGBA32 || image.width < 2 || image.height < 2) return false;
            // Inspect the complete existing CPU readback without allocating a second full-size array.
            // Sparse sampling rejected a real ceiling strip that fell between every sample row.
            var values = image.GetRawTextureData<Color32>();
            if (values.Length != (long)image.width * image.height) return false;
            var first = values[0]; bool opaque = false, varied = false;
            for (int i=0;i<values.Length;i++)
            {
                var p=values[i]; opaque |= p.a>0;
                varied |= p.r!=first.r || p.g!=first.g || p.b!=first.b || p.a!=first.a;
                if (opaque && varied) return true;
            }
            return false;
        }
        public static bool ValidScenePixels(Texture2D image, Color32 verifiedBackground)
        {
            if (!image || !image.isReadable || image.format != TextureFormat.RGBA32 || image.width < 2 || image.height < 2 || verifiedBackground.a != 255 || IsMarker(verifiedBackground)) return false;
            var values=image.GetRawTextureData<Color32>();
            if (values.Length != (long)image.width*image.height) return false;
            bool changed=false;
            for(int i=0;i<values.Length;i++)
            {
                var p=values[i];
                if(IsMarker(p)) return false; // Scan the entire image, including any partially unwritten marker.
                changed |= p.a>0 && (p.r!=verifiedBackground.r || p.g!=verifiedBackground.g || p.b!=verifiedBackground.b);
            }
            return changed;
        }
        static bool IsMarker(Color32 p) => p.r==255 && p.g==0 && p.b==255 && p.a==255;
        public static bool ValidBackgroundPixels(Texture2D image, Color32 expected)
        {
            if (!image || !image.isReadable || image.format != TextureFormat.RGBA32 || image.width < 2 || image.height < 2 || expected.a!=255 || IsMarker(expected)) return false;
            var values=image.GetRawTextureData<Color32>();
            if(values.Length!=(long)image.width*image.height) return false;
            for(int i=0;i<values.Length;i++)
            {
                var p=values[i];
                if(p.r!=expected.r || p.g!=expected.g || p.b!=expected.b || p.a!=expected.a) return false;
            }
            return true;
        }
        static string RendererPath(Transform t)
        {
            var parts = new List<string>();
            while (t.parent) { parts.Add(t.GetSiblingIndex() + ":" + t.name); t = t.parent; }
            parts.Add(t.name); parts.Reverse(); return string.Join("/", parts);
        }
        static bool Lightmapped(int index) => index >= 0 && index < 65534;
#if UNITY_EDITOR
        public static string OriginalMeshLayout(IEnumerable<MeshRenderer> sources)
        {
            if (sources.Any(r => r.isPartOfStaticBatch)) throw new InvalidOperationException("Original unbatched sources required");
            return JsonConvert.SerializeObject(new {version=1,scene=CanonicalScene,renderers=sources.Select(r=>new {path=RendererPath(r.transform),submesh_count=r.GetComponent<MeshFilter>().sharedMesh.subMeshCount,materials=r.sharedMaterials.Select(m=>m?m.name:null).ToArray()}).OrderBy(x=>x.path).ToArray()},Formatting.Indented);
        }
#endif
        static Transform[] LoadedTransforms() => Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded).ToArray();
        static string Sha(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string OriginalState(IEnumerable<GameObject> roots)
        {
            return JsonConvert.SerializeObject(roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => new
            { id=t.GetInstanceID(), parent=t.parent?t.parent.GetInstanceID():0, position=XYZ(t.localPosition), rotation=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w}, scale=XYZ(t.localScale), layer=t.gameObject.layer, active=t.gameObject.activeSelf,
              renderers=t.GetComponents<Renderer>().Select(r=>new {id=r.GetInstanceID(),r.enabled,r.forceRenderingOff,shadow_casting=r.shadowCastingMode.ToString(),r.receiveShadows,materials=r.sharedMaterials.Select(m=>m?m.GetInstanceID():0).ToArray(),r.lightmapIndex,lightmapScaleOffset=new[]{r.lightmapScaleOffset.x,r.lightmapScaleOffset.y,r.lightmapScaleOffset.z,r.lightmapScaleOffset.w}}),
              lights=t.GetComponents<Light>().Select(l=>new {id=l.GetInstanceID(),l.enabled,l.intensity,l.range,l.cullingMask,color=new[]{l.color.r,l.color.g,l.color.b,l.color.a}}) }));
        }

        string Capture(string id, string profile, int page)
        {
            var poses = SelectPoses(profile,page,out int totalPoses,out string planHash);
            var scene = SceneManager.GetActiveScene();
            if (!Idle || scene.path != CanonicalScene) throw new InvalidOperationException("Canonical idle scene required");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline || (pipeline.name != "UniversalRP-HighQuality" && pipeline.name != "UniversalRP-MediumQuality")) throw new InvalidOperationException("Reviewed existing URP pipeline required");
            // Include inactive and DontSave/DDOL scene objects; exclude prefab/assets by valid loaded scene.
            var allTransforms = LoadedTransforms();
            var loadedRoots = allTransforms.Where(t => !t.parent).Select(t => t.gameObject).ToArray();
            if (allTransforms.Any(t => t.gameObject.layer == CaptureLayer)) throw new InvalidOperationException("Capture layer is occupied; no layer mutation permitted");
            var envRoots = scene.GetRootGameObjects().Where(g => EnvironmentRoots.Contains(g.name)).ToArray();
            if (envRoots.Length != 3) throw new InvalidOperationException("Expected three exact ENV roots");
            var sources = envRoots.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(false)).Where(r => r.enabled && !r.forceRenderingOff && r.GetComponent<MeshFilter>()?.sharedMesh).ToArray();
            if (sources.Length != 338 || sources.Any(r => r.GetComponentInParent<Canvas>())) throw new InvalidOperationException("Expected 338 reviewed ENV mesh renderers without Canvas ancestors");
            var layoutAsset = Resources.Load<TextAsset>("ResearchEnvironmentCaptureLayout");
            if (!layoutAsset) throw new InvalidOperationException("Reviewed original ENV layout required");
            var layout = JObject.Parse(layoutAsset.text);
            if ((int)layout["version"] != 1 || (string)layout["scene"] != CanonicalScene) throw new InvalidOperationException("Wrong original ENV layout");
            var expected = ((JArray)layout["renderers"]).ToDictionary(x => (string)x["path"]);
            if (expected.Count != sources.Length) throw new InvalidOperationException("Original ENV layout count mismatch");
            var slices = new Dictionary<MeshRenderer, int[]>();
            foreach (var source in sources)
            {
                if (!expected.TryGetValue(RendererPath(source.transform), out var item)
                    || !source.sharedMaterials.Select(m => m ? m.name : null).SequenceEqual(((JArray)item["materials"]).Select(m => (string)m))) throw new InvalidOperationException("ENV identity/material layout mismatch: " + source.name);
                if (source.probeAnchor || source.additionalVertexStreams || Lightmapped(source.lightmapIndex) || Lightmapped(source.realtimeLightmapIndex)
                    || source.lightProbeUsage == LightProbeUsage.UseProxyVolume || source.sharedMaterials.Any(m => !m)) throw new InvalidOperationException("Unsupported original lighting/vertex stream: " + source.name);
                var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                slices.Add(source, SubmeshIndices(mesh.subMeshCount, source.subMeshStartIndex, (int)item["submesh_count"], source.sharedMaterials.Length, source.isPartOfStaticBatch));
            }
            if (allTransforms.SelectMany(t=>t.GetComponents<LODGroup>()).SelectMany(l=>l.GetLODs()).SelectMany(l=>l.renderers).Any(r=>r && sources.Contains(r))) throw new InvalidOperationException("LOD-assigned ENV requires a separate reviewed capture implementation");
            var lights = allTransforms.SelectMany(t => t.GetComponents<Light>()).Where(l => l.isActiveAndEnabled).ToArray();
            if (lights.Length != 4 || lights.Any(l => (l.cullingMask & (1 << CaptureLayer)) == 0)) throw new InvalidOperationException("Original lighting must include the capture layer; no light mutation permitted");
            // A cold, unworn Quest can update the app without creating an XR swapchain. This independent
            // mono resource diagnostic uses a fixed, reviewed raster and must never imply live XR sampling.
            int liveXRWidth = XRSettings.eyeTextureWidth, liveXRHeight = XRSettings.eyeTextureHeight;
            const int width = 1848, height = 1936;
            string original = OriginalState(loadedRoots);
            var scale = pipeline.renderScale; var msaa = pipeline.msaaSampleCount;
            var previousRT = RenderTexture.active;
            var sun = RenderSettings.sun;
            var ambient = RenderSettings.ambientProbe;
            GameObject owned = null; Camera camera = null; RenderTexture rt = null, readback = null; Texture2D pixels = null;
            int cameraBegins=0, cameraEnds=0;
            bool reentrant = false, restored = false;
            Exception captureFailure = null;
            var cleanupErrors = new List<string>();
            var captures = new List<object>();
            var renderEvidence = new List<object>();
            string output = Path.Combine(Application.persistentDataPath, "EnvironmentNativeCaptures", id);
            if (Directory.Exists(output)) throw new InvalidOperationException("Preserve existing capture directory");
            Directory.CreateDirectory(output);
            Action<ScriptableRenderContext, Camera> guard = (context, other) => { if (other == camera) cameraBegins++; else { reentrant = true; if (owned) owned.SetActive(false); } };
            Action<ScriptableRenderContext, Camera> ended = (context, other) => { if (other == camera) cameraEnds++; };
            var scopedLogs=new System.Collections.Concurrent.ConcurrentQueue<string>();
            Application.LogCallback logged=(message,stack,type)=>{if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert || type==LogType.Warning)scopedLogs.Enqueue(type+": "+message);};
            try
            {
                Application.logMessageReceivedThreaded+=logged;
                owned = new GameObject("OwnedEnvironmentCapture") { hideFlags = HideFlags.HideAndDontSave, layer = CaptureLayer };
                owned.SetActive(false);
                rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { name="OwnedEnvironmentRT", antiAliasing=msaa };
                if (!rt.Create()) throw new InvalidOperationException("Native render target creation failed");
                readback=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) {name="OwnedResolvedEnvironmentRT",antiAliasing=1};
                if (!readback.Create()) throw new InvalidOperationException("Native resolved target creation failed");
                pixels=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
                void ReadTarget(bool explicitResolve)
                {
                    if(explicitResolve && msaa>1) rt.ResolveAntiAliasedSurface(readback); else Graphics.Blit(rt,readback);
                    RenderTexture.active=readback; pixels.ReadPixels(new Rect(0,0,width,height),0,0,false); pixels.Apply(false,false);
                }
                // Independent owned-target control: diagnose GPU write/readback before judging camera rendering.
                RenderTexture.active=rt; GL.Clear(true,true,Color.magenta); ReadTarget(true);
                var witness=(Color32)pixels.GetPixel(width/2,height/2);
                File.WriteAllBytes(Path.Combine(output,"gpu-readback-witness.png"),pixels.EncodeToPNG());
                renderEvidence.Add(new {phase="owned_magenta_readback_control",rgba=new[]{(int)witness.r,witness.g,witness.b,witness.a},msaa,explicit_resolve=msaa>1});
                if(witness.r<250 || witness.g>5 || witness.b<250 || witness.a<250)throw new InvalidOperationException("Owned GPU write/readback control failed; no ENV image acceptance");
                var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
                RenderPipelineManager.beginCameraRendering+=guard;
                RenderPipelineManager.endCameraRendering+=ended;
                owned.SetActive(true);
                foreach(var pose in poses)
                {
                    if (!Idle || reentrant) throw new InvalidOperationException("Capture lost idle or exclusive camera boundary");
                    // A distinct camera owns each GPU submission. No queued draw can target the live XR camera.
                    var cameraHost=new GameObject("OwnedEnvironmentCamera-"+pose.name) { hideFlags=HideFlags.HideAndDontSave, layer=CaptureLayer };
                    cameraHost.transform.SetParent(owned.transform,false); camera=cameraHost.AddComponent<Camera>();
                    camera.enabled=false; camera.cullingMask=1<<CaptureLayer;
                    camera.targetTexture=rt;
                    camera.fieldOfView=60; camera.nearClipPlane=.03f; camera.farClipPlane=100; camera.allowHDR=false; camera.allowMSAA=true;
                    camera.aspect=width/(float)height; camera.rect=new Rect(0,0,1,1); camera.pixelRect=new Rect(0,0,width,height);
                    camera.projectionMatrix=Matrix4x4.Perspective(60,camera.aspect,.03f,100);
                    camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.035f,.035f,1); camera.useOcclusionCulling=false;
                    var data=camera.GetUniversalAdditionalCameraData(); data.allowXRRendering=false; data.renderPostProcessing=false; data.antialiasing=AntialiasingMode.None;
                    data.volumeLayerMask=0; data.renderShadows=true; data.renderType=CameraRenderType.Base;
                    camera.transform.SetPositionAndRotation(pose.position,Quaternion.LookRotation(pose.lookAt-pose.position));
                    bool cullingReady=camera.TryGetCullingParameters(false,out var culling);
                    renderEvidence.Add(new {phase="before_request",pose=pose.name,camera.pixelWidth,camera.pixelHeight,stereo=camera.stereoEnabled,target_samples=rt.antiAliasing,culling_ready=cullingReady});
                    if(camera.pixelWidth!=width || camera.pixelHeight!=height || data.allowXRRendering || !cullingReady)throw new InvalidOperationException("Owned mono viewport/culling control failed: "+pose.name);
                    if (!RenderPipeline.SupportsRenderRequest(camera,request)) throw new InvalidOperationException("URP single-camera request unsupported");
                    // Render the same empty camera before queuing its geometry. A genuinely uniform ceiling
                    // is distinguishable from this calibrated background without relying on spatial variation.
                    RenderTexture.active=rt; GL.Clear(true,true,Color.magenta);
                    int backgroundBegins=cameraBegins,backgroundEnds=cameraEnds;
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    if(reentrant) throw new InvalidOperationException("Concurrent camera during empty background calibration");
                    var expectedBackground=(Color32)camera.backgroundColor;
                    bool backgroundSrgbBefore=GL.sRGBWrite;
                    ReadTarget(false); bool autoBackgroundValid=ValidBackgroundPixels(pixels,expectedBackground);
                    if(!autoBackgroundValid) ReadTarget(true);
                    bool backgroundSrgbAfter=GL.sRGBWrite;
                    bool backgroundValid=ValidBackgroundPixels(pixels,expectedBackground);
                    string backgroundFile=pose.name+"-background.png";byte[] backgroundPng=pixels.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(output,backgroundFile),backgroundPng);
                    renderEvidence.Add(new {phase="empty_camera_background_control",pose=pose.name,begins=cameraBegins-backgroundBegins,ends=cameraEnds-backgroundEnds,
                        valid_pixels=backgroundValid,auto_resolved_samples_valid=autoBackgroundValid,explicit_resolve_fallback_used=!autoBackgroundValid,
                        rgba=new[]{(int)expectedBackground.r,expectedBackground.g,expectedBackground.b,expectedBackground.a},pixel_count=width*height,
                        active_color_space=QualitySettings.activeColorSpace.ToString(),target_srgb=rt.sRGB,readback_srgb=readback.sRGB,
                        target_graphics_format=rt.graphicsFormat.ToString(),readback_graphics_format=readback.graphicsFormat.ToString(),
                        gl_srgb_write_before=backgroundSrgbBefore,gl_srgb_write_after=backgroundSrgbAfter});
                    if(cameraBegins-backgroundBegins!=1 || cameraEnds-backgroundEnds!=1 || !backgroundValid)
                        throw new InvalidOperationException("Empty camera background calibration failed: "+pose.name);
                    // Every pose starts with a uniform marker, so a silent early return cannot reuse a prior view.
                    RenderTexture.active=rt; GL.Clear(true,true,Color.magenta);
                    foreach (var source in sources)
                    {
                        var mesh=source.GetComponent<MeshFilter>().sharedMesh;
                        var globalBlock=new MaterialPropertyBlock(); source.GetPropertyBlock(globalBlock);
                        for (int slot=0;slot<slices[source].Length;slot++)
                        {
                            var materialBlock=new MaterialPropertyBlock(); source.GetPropertyBlock(materialBlock,slot);
                            var draw=new RenderParams(source.sharedMaterials[slot]) {camera=camera,layer=CaptureLayer,worldBounds=source.bounds,
                                renderingLayerMask=source.renderingLayerMask,rendererPriority=source.rendererPriority,
                                shadowCastingMode=source.shadowCastingMode,receiveShadows=source.receiveShadows,
                                lightProbeUsage=source.lightProbeUsage,reflectionProbeUsage=source.reflectionProbeUsage,
                                matProps=materialBlock.isEmpty?globalBlock:materialBlock};
                            Graphics.RenderMesh(draw,mesh,slices[source][slot],source.localToWorldMatrix);
                        }
                    }
                    int beforeBegins=cameraBegins,beforeEnds=cameraEnds;
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    if(reentrant) throw new InvalidOperationException("Unexpected concurrent camera render; owned geometry disabled");
                    // Use precisely the readback path whose empty-camera output was calibrated above.
                    // A scene-dependent switch to an uncalibrated path could silently change its color encoding.
                    bool sceneSrgbBefore=GL.sRGBWrite;
                    ReadTarget(!autoBackgroundValid);
                    bool sceneSrgbAfter=GL.sRGBWrite;
                    byte[] png=pixels.EncodeToPNG(); string file=pose.name+".png";
                    File.WriteAllBytes(Path.Combine(output,file),png);
                    captures.Add(new {name=pose.name,file,position=XYZ(pose.position),look_at=XYZ(pose.lookAt),width,height,sha256=Sha(png),bytes=png.Length,frame=Time.frameCount,
                        background_file=backgroundFile,background_sha256=Sha(backgroundPng),background_bytes=backgroundPng.Length,background_rgba=new[]{(int)expectedBackground.r,expectedBackground.g,expectedBackground.b,expectedBackground.a}});
                    bool validPixels=ValidScenePixels(pixels,expectedBackground);
                    renderEvidence.Add(new {phase="after_request",pose=pose.name,begins=cameraBegins-beforeBegins,ends=cameraEnds-beforeEnds,auto_resolved_samples_valid=autoBackgroundValid && validPixels,explicit_resolve_fallback_used=!autoBackgroundValid,
                        calibrated_readback=autoBackgroundValid?"automatic_blit":"explicit_msaa_resolve",valid_pixels=validPixels,pixel_control="full_calibrated_rgba32",pixel_count=width*height,
                        gl_srgb_write_before=sceneSrgbBefore,gl_srgb_write_after=sceneSrgbAfter});
                    if(cameraBegins-beforeBegins!=1 || cameraEnds-beforeEnds!=1 || !validPixels)throw new InvalidOperationException("ENV camera render/pixel control failed: "+pose.name);
                    if(scopedLogs.Any(x=>!x.StartsWith("Warning:",StringComparison.Ordinal)))throw new InvalidOperationException("Engine error during owned render request");
                    cameraHost.SetActive(false);
                }
            }
            catch(Exception error) {captureFailure=error;}
            finally
            {
                // Disable synchronously before returning Update; runtime Destroy is deferred, so never rely on it for isolation.
                void Clean(Action action) {try {action();} catch(Exception error) {cleanupErrors.Add(error.Message);}}
                Clean(()=>{if(owned) owned.SetActive(false);});
                Clean(()=>RenderPipelineManager.beginCameraRendering-=guard);
                Clean(()=>RenderPipelineManager.endCameraRendering-=ended);
                Clean(()=>Application.logMessageReceivedThreaded-=logged);
                Clean(()=>RenderTexture.active=previousRT);
                Clean(()=>{if(rt) rt.Release();});
                Clean(()=>{if(rt) Destroy(rt);});
                Clean(()=>{if(readback) readback.Release();});
                Clean(()=>{if(readback) Destroy(readback);});
                Clean(()=>{if(pixels) Destroy(pixels);});
                Clean(()=>{if(owned) Destroy(owned);});
                bool ambientSame=true;
                for(int channel=0;channel<3;channel++)for(int coefficient=0;coefficient<9;coefficient++)ambientSame &= ambient[channel,coefficient]==RenderSettings.ambientProbe[channel,coefficient];
                var remainingOriginals=LoadedTransforms().Where(t=>!owned || !t.IsChildOf(owned.transform)).Select(t=>t.GetInstanceID()).OrderBy(x=>x).ToArray();
                var originalIds=allTransforms.Select(t=>t.GetInstanceID()).OrderBy(x=>x).ToArray();
                restored=cleanupErrors.Count==0 && remainingOriginals.SequenceEqual(originalIds) && OriginalState(loadedRoots)==original && pipeline.renderScale==scale && pipeline.msaaSampleCount==msaa && RenderSettings.sun==sun && ambientSame && RenderTexture.active==previousRT && Idle;
                if(!restored) Debug.LogError("ENV_NATIVE_CAPTURE_ORIGINAL_GUARD_FAILED");
                File.WriteAllText(Path.Combine(output,"capture-status.json"),JsonConvert.SerializeObject(new {utc=DateTime.UtcNow.ToString("O"),request_id=id,profile,page,total_poses=totalPoses,selected_poses=poses.Length,plan_sha256=planHash,completed=captureFailure==null && restored && !reentrant && captures.Count==poses.Length,captured_count=captures.Count,original_transform_renderer_light_guard_passed=restored,reentrant,cleanup_errors=cleanupErrors,error=captureFailure?.Message,camera_begins=cameraBegins,camera_ends=cameraEnds,render_evidence=renderEvidence,scoped_engine_logs=scopedLogs.ToArray(),hardware_verified=false},Formatting.Indented));
            }
            if(captureFailure!=null) throw new InvalidOperationException(captureFailure.Message,captureFailure);
            if(!restored || reentrant) throw new InvalidOperationException("Original scene/render-state guard failed");
            var textures=sources.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().SelectMany(m=>m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t=>t).Distinct().OfType<Texture2D>().Select(t=>new {t.name,t.width,t.height,t.mipmapCount,format=t.format.ToString(),t.anisoLevel,t.filterMode,t.isReadable,t.streamingMipmaps,id=t.GetInstanceID(),graphics_format=t.graphicsFormat.ToString(),srgb=UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat),mip_bias=t.mipMapBias,wrap_u=t.wrapModeU.ToString(),wrap_v=t.wrapModeV.ToString(),wrap_w=t.wrapModeW.ToString()}).ToArray();
            string report=Path.Combine(output,"report.json");
            var geometry=sources.Select(r=>new {path=RendererPath(r.transform),batched=r.isPartOfStaticBatch,shadow_casting=r.shadowCastingMode.ToString(),receive_shadows=r.receiveShadows,mesh=r.GetComponent<MeshFilter>().sharedMesh.name,total_submeshes=r.GetComponent<MeshFilter>().sharedMesh.subMeshCount,start=r.subMeshStartIndex,slices=slices[r],materials=r.sharedMaterials.Select(m=>m.name).ToArray(),matrix=Enumerable.Range(0,16).Select(i=>r.localToWorldMatrix[i]).ToArray()}).ToArray();
            // Read the runtime finish actually used by the draw requests, including owned material copies.
            var finishNames=new[]{"Training_Storage","Computer_ToolPanel","Computer_ComponentTray","Computer_TaskCase","Fan_DiagnosticTray","Fan_InspectionTray","Fan_ReturnTray","Fan_SparesRack","Fan_ToolPanel"};
            var surface_materials=sources.Where(r=>finishNames.Contains(r.name)).Select(r=>new {path=RendererPath(r.transform),slots=r.sharedMaterials.Select((m,slot)=>new {
                slot,name=m.name,id=m.GetInstanceID(),shader=m.shader?m.shader.name:null,base_map=m.HasProperty("_BaseMap")&&m.GetTexture("_BaseMap")?m.GetTexture("_BaseMap").name:null,
                base_color=m.HasProperty("_BaseColor")?new[]{m.GetColor("_BaseColor").r,m.GetColor("_BaseColor").g,m.GetColor("_BaseColor").b,m.GetColor("_BaseColor").a}:null,
                legacy_color=m.HasProperty("_Color")?new[]{m.GetColor("_Color").r,m.GetColor("_Color").g,m.GetColor("_Color").b,m.GetColor("_Color").a}:null,
                bump_scale=m.HasProperty("_BumpScale")?(float?)m.GetFloat("_BumpScale"):null,smoothness=m.HasProperty("_Smoothness")?(float?)m.GetFloat("_Smoothness"):null,
                metallic=m.HasProperty("_Metallic")?(float?)m.GetFloat("_Metallic"):null,
                metallic_map_id=m.HasProperty("_MetallicGlossMap")&&m.GetTexture("_MetallicGlossMap")?(int?)m.GetTexture("_MetallicGlossMap").GetInstanceID():null,
                metallic_map=m.HasProperty("_MetallicGlossMap")&&m.GetTexture("_MetallicGlossMap")?m.GetTexture("_MetallicGlossMap").name:null,
                normal_map=m.HasProperty("_BumpMap")&&m.GetTexture("_BumpMap")?m.GetTexture("_BumpMap").name:null,
                occlusion_map=m.HasProperty("_OcclusionMap")&&m.GetTexture("_OcclusionMap")?m.GetTexture("_OcclusionMap").name:null,
                keywords=m.shaderKeywords.OrderBy(k=>k,StringComparer.Ordinal).ToArray()}).ToArray()}).ToArray();
            // Same-process getter control from unselected original Blue consumers; report only, no material setters.
            var original_blue_materials=sources.SelectMany(r=>r.sharedMaterials).Where(m=>m && m.name=="SP_Blue" && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")).Distinct().Select(m=>new {
                name=m.name,id=m.GetInstanceID(),shader=m.shader?m.shader.name:null,base_map=m.GetTexture("_BaseMap").name,
                base_color=m.HasProperty("_BaseColor")?new[]{m.GetColor("_BaseColor").r,m.GetColor("_BaseColor").g,m.GetColor("_BaseColor").b,m.GetColor("_BaseColor").a}:null,
                legacy_color=m.HasProperty("_Color")?new[]{m.GetColor("_Color").r,m.GetColor("_Color").g,m.GetColor("_Color").b,m.GetColor("_Color").a}:null,
                consumers=sources.Where(r=>r.sharedMaterials.Contains(m)).Select(r=>RendererPath(r.transform)).ToArray()}).ToArray();
            File.WriteAllText(report,JsonConvert.SerializeObject(new {utc=DateTime.UtcNow.ToString("O"),request_id=id,profile,page,total_poses=totalPoses,selected_poses=poses.Length,plan_sha256=planHash,scope="Native Android mono ENV GPU render requests at a fixed diagnostic raster, independent of XR swapchain availability. Shipped resources with reviewed original submesh mapping and Renderer matrices. No XR head movement or UI/session change. Finite planned viewpoints, not all continuous positions. Not live XR sampling, stereo, wearer optics, sampled GPU mip, or performance acceptance; capture workload must be excluded from ordinary frame/thermal measurements; host must verify serialized batch ranges in this exact APK.",original_state_restored=restored,hardware_verified=false,renderer_count=sources.Length,field_of_view=60,diagnostic_raster=new[]{width,height},live_xr_target=new[]{liveXRWidth,liveXRHeight},pipeline=pipeline.name,quality=QualitySettings.names[QualitySettings.GetQualityLevel()],render_settings=ResearchRenderExperiment.CurrentSettings(),textures,geometry,surface_materials,original_blue_materials,captures},Formatting.Indented));
            return report;
        }
    }
}
