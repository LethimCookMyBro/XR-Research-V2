#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Reader v2 footage (Tools/s1_reader_v2): renders each shot of the authored plan from the real Task A/B models through the
    // existing educational motion plan, with every printed part label blanked so no frame shows which option is installed
    // (see the 28 Oct label leak), and records where named anchors project so arrows and callouts land on real geometry.
    // Edit Mode only; refuses a dirty scene and reloads the canonical scene from disk afterwards.
    public static class ResearchS1ReaderV2Shots
    {
        const string Scene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const int W = 1280, H = 720;

        sealed class Segment { public string section; public int fact; public int frames = 48; public float from = 0, to = 1; }
        sealed class Shot
        {
            public string id, task; public string[] frame; public float[] view; public float min = .35f, fill = 1f, orbit;
            public Segment[] segments; public string[] anchors;
        }

        public static string Build(string planPath, string outputRoot, string[] only = null)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Active scene has unsaved changes; not touching it.");
            var shots = JsonConvert.DeserializeObject<Shot[]>(JObject.Parse(File.ReadAllText(planPath))["shots"].ToString());
            var scene = EditorSceneManager.GetActiveScene();
            int blanked = 0;
            var cameraGo = new GameObject("S1V2Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>(); camera.fieldOfView = 40; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI")); camera.clearFlags = CameraClearFlags.Skybox;
            var fill = cameraGo.AddComponent<Light>(); fill.type = LightType.Spot; fill.spotAngle = 70; fill.range = 4; fill.shadows = LightShadows.None;
            Directory.CreateDirectory(outputRoot);
            string manifestPath = Path.Combine(outputRoot, "shots.json");
            var manifest = only != null && File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : new JObject();
            int total = 0;
            try
            {
                foreach (var shot in shots.Where(s => only == null || only.Contains(s.id)))
                {
                    // Fresh scene per shot: a part posed by an earlier shot must never appear installed in this one.
                    scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
                    var a = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
                    var b = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
                b.KeepAuthoredGuardDetail();
                    var cables = a.GetComponentsInChildren<ResearchS1Cable>(true);
                    foreach(var cable in cables) cable.skin.forceMatrixRecalculationPerRender=true;
                    var targets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                    var station = shot.task == "T" ? UnityEngine.Object.FindObjectsByType<ResearchTrainingStation>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single() : null;
                    if (station)
                    {
                        // Training footage must not show the experiment-task models behind the station.
                        foreach (var r in a.GetComponentsInChildren<Renderer>(true).Concat(b.GetComponentsInChildren<Renderer>(true))) r.enabled = false;
                    foreach (var item in station.controller.trainingObjects) if (item) item.gameObject.SetActive(false);
                        // The turntable moves its seats, so the shot is framed on its first pose.
                        var first = shot.segments[0];
                        ResearchS1ReaderMotionBuilder.MotionT(station, first.section, first.fact, first.frames == 1 ? first.to : first.from);
                    }
                    var anchors = station ? TrainingAnchors(station, camera) : Anchors(a, b, camera);
                    blanked = BlankLabels(a.transform) + BlankLabels(b.transform);
                    if (shot.segments.Any(s => s.section == "A.ssd")) cables.Single(c => c.assetId == "CABLE_SATA_DATA_S_500").transform.localRotation *= Quaternion.Euler(0, 0, 180);
                    string dir = Path.Combine(outputRoot, shot.id);
                    if (Directory.Exists(dir)) foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);
                    Directory.CreateDirectory(dir);
                    var bounds = station ? TrainingFrame(station, shot.frame) : ResearchS1ReaderMediaBuilder.Frame(targets, shot.frame);
                    float radius = Mathf.Max(bounds.extents.magnitude, .04f);
                    if (shot.id == "A_turn")
                    {
                        var center = new Vector3(a.servicePivot.position.x, bounds.center.y, a.servicePivot.position.z);
                        radius += Vector3.Distance(bounds.center, center);
                        bounds.center = center;
                    }
                    float distance = Mathf.Clamp(radius / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f, shot.min, 2.4f);
                    // Keep the physical rim clip above the video caption band, not just its label.
                    if (shot.task == "B" && shot.anchors != null && shot.anchors.Contains("front_clip"))
                        bounds.center -= Vector3.up * (distance * .07f);
                    var direction = new Vector3(shot.view[0], shot.view[1], shot.view[2]).normalized;
                    fill.intensity = Mathf.Clamp(1.6f * distance * distance / 1.4f, .3f, 1.6f) * shot.fill;
                    var frames = new JArray(); int n = 0;
                    for (int s = 0; s < shot.segments.Length; s++)
                    {
                        var segment = shot.segments[s];
                        for (int k = 0; k < segment.frames; k++)
                        {
                            double time = segment.from + (segment.to - segment.from) * (segment.frames == 1 ? 1 : (double)k / (segment.frames - 1));
                            string state = segment.section == null ? null : shot.task == "A" ? ResearchS1ReaderMotionBuilder.MotionA(a, segment.section, segment.fact, time)
                                : station ? ResearchS1ReaderMotionBuilder.MotionT(station, segment.section, segment.fact, time)
                                : ResearchS1ReaderMotionBuilder.MotionB(b, segment.section, segment.fact, time);
                            if (segment.section == null && shot.task == "B") ResearchS1ReaderMotionBuilder.ClosedFan(b);
                            foreach (var cable in cables) cable.RefreshShape();
                            float orbit = shot.orbit * Mathf.Sin((float)(n / 48.0) * Mathf.PI);
                            camera.transform.position = bounds.center + Quaternion.AngleAxis(orbit, Vector3.up) * direction * distance;
                            camera.transform.LookAt(bounds.center);
                            string file = (n++).ToString("0000") + ".png";
                            camera.aspect = (float)W / H;
                            var points = new JObject();
                            foreach (string id in shot.anchors ?? Array.Empty<string>())
                            {
                                if (!anchors.TryGetValue(id, out var resolve)) throw new InvalidOperationException("Unknown anchor " + id);
                                Vector3 p = camera.WorldToViewportPoint(resolve());
                                points[id] = new JArray(Math.Round(p.x * W, 1), Math.Round((1 - p.y) * H, 1), p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1);
                            }
                            File.WriteAllBytes(Path.Combine(dir, file), ResearchS1ReaderMediaBuilder.Render(camera, null));
                            frames.Add(new JObject { ["file"] = file, ["segment"] = s, ["time"] = Math.Round(time, 4), ["state"] = state, ["anchors"] = points });
                        }
                    }
                    manifest[shot.id] = new JObject { ["fps"] = 12, ["task"] = shot.task, ["frames"] = frames };
                    total += n;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
                scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            }
            return "shots=" + manifest.Count + " frames=" + total + " labels_blanked=" + blanked + " reloaded_dirty=" + scene.isDirty;
        }

        // Option labels (S1_M01_label ... S1_S03_label, TMP text) name the part and its values; hide them. Base-unit silkscreens stay.
        static int BlankLabels(Transform root)
        {
            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (System.Text.RegularExpressions.Regex.IsMatch(renderer.name, @"^S1_[MSG]\d\d_label$")) { renderer.enabled = false; count++; }
            return count;
        }

        public static string LabelMaterials()
        {
            var a = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var b = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            return string.Join("\n", a.GetComponentsInChildren<Renderer>(true).Concat(b.GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials.Where(m => m).Select(m => m.name + " @ " + r.name)).Where(x => x.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0).Distinct());
        }

        static Vector3 Rim(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) return t.position;
            var bounds = renderers.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        // Guards are discs: rim point = disc centre + radius along the requested direction projected onto the disc plane.
        static Vector3 Extreme(Transform root, Vector3 direction)
        {
            var renderer = root.GetComponentsInChildren<MeshRenderer>(true).OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
            if (!renderer) return root.position;
            var t = renderer.transform; var local = renderer.localBounds; var e = Vector3.Scale(local.extents, t.lossyScale);
            int thin = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            Vector3 normal = thin == 0 ? t.right : thin == 1 ? t.up : t.forward;
            float radius = Mathf.Max(thin == 0 ? 0 : e.x, thin == 1 ? 0 : e.y, thin == 2 ? 0 : e.z);
            return t.TransformPoint(local.center) + Vector3.ProjectOnPlane(direction, normal).normalized * radius * .97f;
        }

        static Dictionary<string, Func<Vector3>> Anchors(ResearchS1TaskAMechanics a, ResearchS1TaskBMechanics b, Camera camera)
        {
            Transform Socket(QAPlug part, S1Connector connector) => a.wiring.targets.Single(t => t.part == part && t.connector == connector).socket.seat;
            var map = new Dictionary<string, Func<Vector3>>
            {
                ["ram_latch"] = () => a.ramSlots[1].latches.Single().animationRoot.transform.position,
                ["pcie_primary"] = () => a.gpuSlots[0].seat.position,
                ["pcie_secondary"] = () => a.gpuSlots[1].seat.position,
                ["gpu_card"] = () => a.gpus[1].transform.position,
                ["gpu_screw"] = () => Rim(a.gpuScrews[0].transform),
                ["gpu_8pin"] = () => Socket(a.gpus[1], S1Connector.GpuPcie8).position,
                ["ssd_tray"] = () => a.traySeats[0].position,
                ["ssd_thumb"] = () => a.sleds[0].thumb.transform.position,
                ["sata_data"] = () => Socket(a.ssds[0], S1Connector.SsdData).position,
                ["sata_power"] = () => Socket(a.ssds[0], S1Connector.SsdPower).position,
                ["service_handle"] = () => a.servicePivot.position,
                ["front_clip"] = () => Rim(b.frontClip.transform),
                ["front_guard"] = () => Rim(b.frontGuard.transform),
                ["front_marks"] = () => b.frontGuard.seat.position,
                ["blade"] = () => b.blade.transform.position,
                ["spinner"] = () => b.spinner.transform.position,
                ["rear_guard"] = () => Rim(b.rearGuard.transform),
                ["rear_lock"] = () => b.rearLock.transform.position,
                ["rear_lock_grip"] = () => Vector3.Lerp(b.rearLock.transform.position, Extreme(b.rearLock.transform, camera.transform.right), .65f),
                // Front and rear guard rims overlap from the front; point at each guard's own outermost wire on opposite screen sides.
                ["front_guard_side"] = () => Extreme(b.frontGuard.transform, camera.transform.right),
                ["rear_guard_side"] = () => Extreme(b.rearGuard.transform, -camera.transform.right),
            };
            foreach (var slot in a.ramSlots) map["slot_" + slot.seatName.Replace("DIMM_", "")] = () => slot.seat.position;
            foreach (var part in a.ram.Concat(a.ssds).Concat(a.gpus)) map["part_" + part.stableId] = () => part.transform.position;
            return map;
        }

        // Training shots frame station parts by role (shots.json "frame": station, module, slot, screw, lead).
        static Bounds TrainingFrame(ResearchTrainingStation t, string[] names)
        {
            Bounds Box(Vector3 centre, float size) => new Bounds(centre, Vector3.one * size);
            Bounds One(string name)
            {
                switch (name)
                {
                    case "station": return RendererBounds(t.turntable);
                    case "module": return RendererBounds(t.module.transform);
                    case "slot": return Box(t.moduleSocket.seat.position, .09f);
                    case "screw": return Box(t.screw.seat.position, .02f);
                    case "lead": { var b = Box(t.leadSocket.seat.position, .05f); b.Encapsulate(Box(t.leadAnchor.position, .05f)); return b; }
                    default: throw new InvalidOperationException("Unknown training frame " + name);
                }
            }
            var bounds = One(names[0]);
            foreach (var name in names.Skip(1)) bounds.Encapsulate(One(name));
            return bounds;
        }

        static Bounds RendererBounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            return renderers.Length == 0 ? new Bounds(t.position, Vector3.one * .05f) : renderers.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        }

        // The module's keyed (chamfered) corner in module space: of the four outline corners, the one farthest from every mesh vertex.
        static Vector3? KeyCorner(Transform module)
        {
            var filter = module.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled)
                .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();
            if (!filter) return null;
            var vertices = filter.sharedMesh.vertices; var b = filter.sharedMesh.bounds;
            if (vertices.Length == 0) return null;
            int up = b.size.x <= b.size.y && b.size.x <= b.size.z ? 0 : b.size.y <= b.size.z ? 1 : 2, u = (up + 1) % 3, v = (up + 2) % 3;
            Vector3 best = b.center; float bestGap = -1;
            foreach (int su in new[] { -1, 1 })
                foreach (int sv in new[] { -1, 1 })
                {
                    var corner = b.center; corner[u] += su * b.extents[u]; corner[v] += sv * b.extents[v];
                    float gap = vertices.Min(p => new Vector2(p[u] - corner[u], p[v] - corner[v]).sqrMagnitude);
                    if (gap > bestGap) { bestGap = gap; best = Vector3.Lerp(corner, b.center, .2f); }
                }
            return module.InverseTransformPoint(filter.transform.TransformPoint(best));
        }

        static Dictionary<string, Func<Vector3>> TrainingAnchors(ResearchTrainingStation t, Camera camera)
        {
            var module = t.module.transform; var mate = ResearchS1ReaderMotionBuilder.ModuleMate(t);
            var mateInRoot = module.worldToLocalMatrix * mate.localToWorldMatrix;
            float keyed = t.moduleSocket.rules.Single(r => r.outcome == PairOutcome.Seat).turnDeg;
            var corner = KeyCorner(module);
            if (corner == null) Debug.LogWarning("Reader T2: the module's cut corner was not found; corner callouts point at the module centre.");
            var grips = ResearchS1ReaderMotionBuilder.TrainingGrips(t);
            // Left/right as the camera sees them, so callout offsets in the content stay on the intended side.
            Vector3 Grip(bool left) => (left ? grips.OrderBy(g => camera.WorldToViewportPoint(g.position).x) : grips.OrderByDescending(g => camera.WorldToViewportPoint(g.position).x)).First().position;
            return new Dictionary<string, Func<Vector3>>
            {
                ["module"] = () => Rim(module),
                ["module_corner"] = () => corner.HasValue ? module.TransformPoint(corner.Value) : Rim(module),
                ["slot"] = () => t.moduleSocket.seat.position,
                ["slot_corner"] = () => corner.HasValue
                    ? SocketRules.RootPose(t.moduleSocket.seat.localToWorldMatrix, 0, keyed, mateInRoot).MultiplyPoint3x4(corner.Value) : t.moduleSocket.seat.position,
                ["latch"] = () => Rim(t.latch),
                ["screw_head"] = () => Rim(t.screw.transform),
                ["screw_seat"] = () => t.screw.seat.position,
                ["plug_head"] = () => t.leadHead.transform.position,
                ["lead_wire"] = () => t.leadBones[t.leadBones.Length / 2].position,
                ["lead_socket"] = () => t.leadSocket.seat.position,
                ["turn_handle"] = () => t.turnHandle.transform.position,
                ["turn_grip_left"] = () => Grip(true),
                ["turn_grip_right"] = () => Grip(false),
                ["turn_axis"] = () => new Vector3(t.turntable.position.x, Rim(t.turntable).y, t.turntable.position.z),
            };
        }
    }
}
#endif
