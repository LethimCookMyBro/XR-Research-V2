#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Editor Play Mode probe, not Quest evidence. Poses the three grabbable Task A cable ends and the Training FX1 lead through
    // rest, lift, laid-on-bench, insert and remove in layouts P and Q, bakes each skin, and measures body vertices below the real
    // bench/base collider tops and the tightest bend of the baked mesh centreline. Rerun: Run("<fresh dir>", "<tag>").
    public static class ResearchCableVisualChecks
    {
        static readonly string[] SurfaceNames = { "Computer_Main", "Fan_Main", "Training_Practice_A", "COL_TRAINER_FX1_BASE_Body" };
        const float SinkEpsilon = .0001f, SlabDepth = .05f, Bin = .008f;
        public const float MinBendRadiusMm = 8; // kink gate: a centreline radius under twice the ~4 mm cable radius reads as a fold

        sealed class Surface { public string name; public Bounds bounds; }

        sealed class Probe
        {
            public string name; public SkinnedMeshRenderer skin; public bool[] body; public int[] bin, binChain; public int bins;
            public List<Transform[]> chains; public List<Vector3[]> localNext; public readonly UnityEngine.Mesh baked = new UnityEngine.Mesh();
            public Transform tubeOwner; // drawn tube replaces the skinned body: its vertices are the body, its ring centres the centreline
        }

        sealed class Result
        {
            public string id; public int bodySink, housingSink; public float bodyMm, housingMm, radiusMm = float.PositiveInfinity, misalignDeg, gapMm, nodeMinY, deepestY;
        }

        public static string Run(string evidenceDir, string tag, string shots = "")
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode required (station Awake sets the lead rest pose).");
            Directory.CreateDirectory(evidenceDir);
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            var station = UnityEngine.Object.FindFirstObjectByType<ResearchTrainingStation>(FindObjectsInactive.Include);
            var surfaces = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => !c.isTrigger && SurfaceNames.Contains(c.name) && c.bounds.size.x > .05f && c.bounds.size.z > .05f)
                .Select(c => new Surface { name = c.name, bounds = c.bounds }).ToList();
            var wanted = shots.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            // "id" shoots that pose; "prefix*" shoots the first pose whose id starts with prefix.
            bool Want(string id)
            {
                int hit = wanted.FindIndex(w => w == id || w.EndsWith("*", StringComparison.Ordinal) && id.StartsWith(w.TrimEnd('*'), StringComparison.Ordinal));
                if (hit >= 0 && wanted[hit].EndsWith("*", StringComparison.Ordinal)) wanted.RemoveAt(hit);
                return hit >= 0;
            }
            var report = new StringBuilder();
            report.AppendLine("ResearchCableVisualChecks tag=" + tag + " " + DateTime.Now.ToString("s") + " (Editor Play Mode, not Quest)");
            foreach (var s in surfaces) report.AppendLine("surface " + s.name + " top=" + s.bounds.max.y.ToString("F4") + " x[" + s.bounds.min.x.ToString("F2") + "," + s.bounds.max.x.ToString("F2") + "] z[" + s.bounds.min.z.ToString("F2") + "," + s.bounds.max.z.ToString("F2") + "]");
            report.AppendLine("gates: body vertex sink > " + SinkEpsilon * 1000 + " mm within " + SlabDepth * 1000 + " mm under a surface top; centreline radius < " + MinBendRadiusMm + " mm (" + Bin * 1000 + " mm bins, triples 2 bins apart)");

            var cableProbes = task.wiring.cables.ToDictionary(c => c, c => Build(c.assetId, c.skin, c.spans.Select(s => new[] { s.from }.Concat(s.bones).Concat(new[] { s.to }).ToArray())));
            var leadSkin = station.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name.StartsWith("VIS_TRAINER_FX1_LEAD", StringComparison.Ordinal) && r.enabled);
            Transform LeadBone(string id) => leadSkin.bones.Single(b => b.name == "B_TRAINER_FX1_LEAD_" + id);
            var lead = Build("TRAINER_FX1_LEAD", leadSkin, new[] { new[] { LeadBone("B1") }.Concat(station.leadBones).Concat(new[] { LeadBone("B2") }).ToArray() });
            var leadCable = station.GetComponent<ResearchS1Cable>();
            Action poseLead = leadCable.RefreshShape;
            lead.tubeOwner = leadCable.tubeRadius > 0 ? leadCable.transform : null;

            // Task A cameras look from outside the case, which otherwise hides the plugs at their rest pose.
            var caseCentre = task.fixedAssembly[0].GetComponentsInChildren<Renderer>().Select(x => x.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;
            var all = new List<Result>(); int blocked = 0;
            string startLayout = task.layout;
            foreach (var layout in new[] { "P", "Q" })
            {
                task.ApplyLayout(layout);
                foreach (var cable in task.wiring.cables) cable.RefreshShape();
                poseLead();
                foreach (var cable in task.wiring.cables) all.Add(Measure(cableProbes[cable], layout + "|" + cable.assetId + "|rest", surfaces));
                all.Add(Measure(lead, layout + "|FX1_LEAD|rest", surfaces));

                foreach (var end in task.wiring.ends)
                {
                    var plug = end.plug.transform; var cable = end.cable; var probe = cableProbes[cable];
                    Vector3 p0 = plug.position; Quaternion r0 = plug.rotation;
                    var mateInRoot = plug.worldToLocalMatrix * end.plug.mates[0].localToWorldMatrix;
                    var poses = new List<(string id, Matrix4x4 pose)> { ("rest", plug.localToWorldMatrix),
                        ("lift05", Matrix4x4.TRS(p0 + Vector3.up * .05f, r0, Vector3.one)), ("lift15", Matrix4x4.TRS(p0 + Vector3.up * .15f, r0, Vector3.one)) };
                    foreach (var target in task.wiring.targets.Where(t => t.socket.rules.Any(r => r.plug == end.plug.assetId && r.outcome == PairOutcome.Seat)))
                        foreach (var (where, seat) in InstalledSeats(task, target))
                            foreach (var (kind, mm) in new[] { ("insert", 0f), ("near", 10f), ("remove", 50f) })
                                poses.Add((kind + "_" + target.part.stableId + "_" + where, SocketRules.RootPose(seat, mm, 0, mateInRoot)));
                    foreach (var (rid, rot) in Rotations(r0))
                        foreach (float lift in new[] { .002f, .05f, .15f })
                            foreach (float dx in new[] { -.25f, -.12f, 0, .12f, .25f })
                                foreach (float dz in new[] { -.15f, 0, .15f })
                                    poses.Add(("laid_" + rid + "_l" + lift.ToString("F3") + "_x" + dx.ToString("F2") + "_z" + dz.ToString("F2"), OnSurface(plug, surfaces, p0 + new Vector3(dx, 0, dz), rot, lift)));
                    foreach (var (id, pose) in poses)
                    {
                        if (!cable.Allows(plug, pose) || !Clear(plug, pose, id)) { blocked++; continue; }
                        plug.SetPositionAndRotation(pose.GetColumn(3), pose.rotation); cable.RefreshShape();
                        var r = Measure(probe, layout + "|" + end.plug.stableId + "|" + id, surfaces); all.Add(r);
                        if (Want(r.id)) Shots(evidenceDir, tag, r.id, probe, plug.position, plug.position - caseCentre);
                    }
                    plug.SetPositionAndRotation(p0, r0); cable.RefreshShape();
                    if (layout == "P") Shots(evidenceDir, tag, layout + "|" + end.plug.stableId + "|rest", probe, p0, p0 - caseCentre);
                }

                var head = station.leadHead.transform; Vector3 h0 = head.position; Quaternion q0 = head.rotation;
                var headMate = head.worldToLocalMatrix * station.leadHead.mates[0].localToWorldMatrix;
                var leadPoses = new List<(string id, Matrix4x4 pose)> { ("rest", head.localToWorldMatrix),
                    ("lift05", Matrix4x4.TRS(h0 + Vector3.up * .05f, q0, Vector3.one)), ("lift10", Matrix4x4.TRS(h0 + Vector3.up * .1f, q0, Vector3.one)) };
                foreach (var (kind, mm) in new[] { ("insert", 0f), ("near", 10f), ("remove", 50f) })
                    leadPoses.Add((kind, SocketRules.RootPose(station.leadSocket.seat.localToWorldMatrix, mm, 0, headMate)));
                foreach (var (rid, rot) in Rotations(q0))
                    foreach (float lift in new[] { .002f, .05f })
                        foreach (float dx in new[] { -.12f, -.06f, 0, .06f, .12f })
                            foreach (float dz in new[] { -.1f, -.05f, 0, .05f, .1f })
                                leadPoses.Add(("laid_" + rid + "_l" + lift.ToString("F3") + "_x" + dx.ToString("F2") + "_z" + dz.ToString("F2"), OnSurface(head, surfaces, station.leadAnchor.position + new Vector3(dx, 0, dz), rot, lift)));
                foreach (var (id, pose) in leadPoses)
                {
                    if (Vector3.Distance(pose.GetColumn(3), station.leadAnchor.position) > station.leadReach || !Clear(head, pose, id)) { blocked++; continue; }
                    head.SetPositionAndRotation(pose.GetColumn(3), pose.rotation); poseLead();
                    var r = Measure(lead, layout + "|FX1_LEAD|" + id, surfaces); all.Add(r);
                    if (Want(r.id)) Shots(evidenceDir, tag, r.id, lead, (head.position + station.leadAnchor.position) / 2, Vector3.back);
                }
                head.SetPositionAndRotation(h0, q0); poseLead();
                if (layout == "P") Shots(evidenceDir, tag, layout + "|FX1_LEAD|rest", lead, (h0 + station.leadAnchor.position) / 2, Vector3.back);
            }
            task.ApplyLayout(startLayout);

            string Summary(IEnumerable<Result> rs, string label)
            {
                var list = rs.ToList(); if (list.Count == 0) return label + " poses=0";
                var sink = list.Where(r => r.bodySink > 0).ToList();
                var worstSink = list.OrderByDescending(r => r.bodyMm).First(); var worstBend = list.OrderBy(r => r.radiusMm).First();
                return label + " poses=" + list.Count + " bodySinkPoses=" + sink.Count + " bodySink>2mm=" + list.Count(r => r.bodyMm > 2) +
                    " worstBodyMm=" + worstSink.bodyMm.ToString("F2") + " (" + worstSink.id + ") housingSinkPoses=" + list.Count(r => r.housingSink > 0) +
                    " worstHousingMm=" + list.Max(r => r.housingMm).ToString("F2") + " kinkPoses(<" + MinBendRadiusMm + "mm)=" + list.Count(r => r.radiusMm < MinBendRadiusMm) +
                    " minRadiusMm=" + worstBend.radiusMm.ToString("F1") + " (" + worstBend.id + ") maxMisalignDeg=" + list.Max(r => r.misalignDeg).ToString("F1") +
                    " maxGapMm=" + list.Max(r => r.gapMm).ToString("F1");
            }
            var summary = new StringBuilder();
            foreach (var group in all.GroupBy(r => r.id.Split('|')[1]))
                foreach (var layout in new[] { "P", "Q" }) summary.AppendLine(Summary(group.Where(r => r.id.StartsWith(layout + "|", StringComparison.Ordinal)), layout + " " + group.Key));
            report.Append(summary);
            report.AppendLine("poses skipped (beyond cable length or lead reach, or held part inside a solid): " + blocked);
            report.AppendLine("pose id | bodySinkVerts bodyMm | housingSinkVerts housingMm | radiusMm | misalignDeg gapMm | nodeMinY deepestY");
            foreach (var r in all)
                report.AppendLine(r.id + " | " + r.bodySink + " " + r.bodyMm.ToString("F2") + " | " + r.housingSink + " " + r.housingMm.ToString("F2") + " | " + r.radiusMm.ToString("F1") +
                    " | " + r.misalignDeg.ToString("F1") + " " + r.gapMm.ToString("F1") + " | " + r.nodeMinY.ToString("F4") + " " + r.deepestY.ToString("F4"));
            report.Append(Cost(task, station, poseLead));
            File.WriteAllText(Path.Combine(evidenceDir, "cable_visual_" + tag + ".txt"), report.ToString());
            return summary.ToString();
        }

        // The target connector where the participant would meet it: SSD seated in either drive tray, GPU seated in either slot.
        static IEnumerable<(string, Matrix4x4)> InstalledSeats(ResearchS1TaskAMechanics task, S1CableTarget target)
        {
            var sled = task.sleds.FirstOrDefault(s => s.part == target.part);
            Transform root = sled ? sled.transform : target.part.transform, mate = sled ? sled.mate : target.part.mates[0];
            var seatInRoot = root.worldToLocalMatrix * target.socket.seat.localToWorldMatrix;
            var mateInRoot = root.worldToLocalMatrix * mate.localToWorldMatrix;
            var places = sled ? task.traySeats : task.gpuSlots.Select(s => s.seat).ToArray();
            foreach (var place in places) yield return (place.name.Split('_').Last(), SocketRules.RootPose(place.localToWorldMatrix, 0, 0, mateInRoot) * seatInRoot);
        }

        // Held parts never enter solids (ResearchS1HeldCollision, and the trainer base for the lead), so such poses are not
        // participant poses. Socket approach poses are exempt: the plug body is meant to enter the connector shell.
        static bool Clear(Transform part, Matrix4x4 pose, string id)
        {
            if (id.StartsWith("insert", StringComparison.Ordinal) || id.StartsWith("near", StringComparison.Ordinal) || id.StartsWith("remove", StringComparison.Ordinal)) return true;
            Vector3 p0 = part.position; Quaternion r0 = part.rotation;
            part.SetPositionAndRotation(pose.GetColumn(3), pose.rotation); Physics.SyncTransforms();
            bool clear = true;
            foreach (var own in part.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger))
                foreach (var other in Physics.OverlapBox(own.bounds.center, own.bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (!other.transform.IsChildOf(part) && Physics.ComputePenetration(own, own.transform.position, own.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > .001f)
                        clear = false;
            part.SetPositionAndRotation(p0, r0); Physics.SyncTransforms();
            return clear;
        }

        static IEnumerable<(string, Quaternion)> Rotations(Quaternion installed) =>
            new[] { ("inst", installed), ("yaw90", Quaternion.Euler(0, 90, 0) * installed), ("flat", Quaternion.Euler(90, 0, 0) * installed) };

        // The held part's lowest solid collider point sits `lift` above the highest surface under the requested point.
        static Matrix4x4 OnSurface(Transform part, List<Surface> surfaces, Vector3 at, Quaternion rotation, float lift)
        {
            Vector3 p0 = part.position; Quaternion r0 = part.rotation;
            part.SetPositionAndRotation(at, rotation); Physics.SyncTransforms();
            float bottom = part.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).Select(c => c.bounds.min.y).DefaultIfEmpty(at.y).Min();
            part.SetPositionAndRotation(p0, r0);
            float top = surfaces.Where(s => at.x >= s.bounds.min.x && at.x <= s.bounds.max.x && at.z >= s.bounds.min.z && at.z <= s.bounds.max.z).Select(s => s.bounds.max.y).DefaultIfEmpty(at.y).Max();
            return Matrix4x4.TRS(new Vector3(at.x, at.y - bottom + top + lift, at.z), rotation, Vector3.one);
        }

        static Probe Build(string name, SkinnedMeshRenderer skin, IEnumerable<Transform[]> chains)
        {
            var mesh = skin.sharedMesh; var binds = mesh.bindposes; var verts = mesh.vertices; float scale = skin.transform.lossyScale.x;
            var weights = mesh.GetAllBoneWeights(); var counts = mesh.GetBonesPerVertex();
            var p = new Probe { name = name, skin = skin, chains = chains.ToList(), localNext = new List<Vector3[]>(), body = new bool[verts.Length], bin = new int[verts.Length] };
            var heads = new HashSet<Transform>(p.chains.SelectMany(c => new[] { c[0], c[c.Length - 1] }));
            var bindChains = p.chains.Select(c => c.Select(t => binds[Array.IndexOf(skin.bones, t)].inverse.MultiplyPoint3x4(Vector3.zero)).ToArray()).ToList();
            var binChain = new List<int>(); var offsets = new int[bindChains.Count];
            for (int c = 0; c < bindChains.Count; c++)
            {
                var chain = p.chains[c]; var bc = bindChains[c];
                p.localNext.Add(Enumerable.Range(0, chain.Length - 1).Select(i => binds[Array.IndexOf(skin.bones, chain[i])].MultiplyPoint3x4(bc[i + 1])).ToArray());
                offsets[c] = binChain.Count;
                float length = 0; for (int i = 1; i < bc.Length; i++) length += Vector3.Distance(bc[i - 1], bc[i]) * scale;
                binChain.AddRange(Enumerable.Repeat(c, Mathf.CeilToInt(length / Bin) + 1));
            }
            p.binChain = binChain.ToArray(); p.bins = p.binChain.Length;
            for (int v = 0, w = 0; v < verts.Length; w += counts[v], v++)
            {
                p.body[v] = counts[v] > 0 && !heads.Contains(skin.bones[weights[w].boneIndex]);
                float best = float.MaxValue;
                for (int c = 0; c < bindChains.Count; c++)
                {
                    var bc = bindChains[c]; float s = 0;
                    for (int i = 1; i < bc.Length; i++)
                    {
                        Vector3 a = bc[i - 1], d = bc[i] - a; float len = d.magnitude;
                        float t = len < 1e-7f ? 0 : Mathf.Clamp01(Vector3.Dot(verts[v] - a, d) / (len * len));
                        float dist = (verts[v] - (a + d * t)).sqrMagnitude;
                        if (dist < best) { best = dist; p.bin[v] = Mathf.Min(offsets[c] + (int)((s + t * len) * scale / Bin), p.bins - 1); }
                        s += len;
                    }
                }
            }
            return p;
        }

        static Result Measure(Probe p, string id, List<Surface> surfaces)
        {
            var r = new Result { id = id, deepestY = float.PositiveInfinity, nodeMinY = float.PositiveInfinity };
            p.skin.BakeMesh(p.baked, true);
            var toWorld = Matrix4x4.TRS(p.skin.transform.position, p.skin.transform.rotation, Vector3.one);
            var verts = p.baked.vertices; var sums = new Vector3[p.bins]; var n = new int[p.bins];
            for (int i = 0; i < verts.Length; i++)
            {
                var w = toWorld.MultiplyPoint3x4(verts[i]);
                if (p.body[i]) { sums[p.bin[i]] += w; n[p.bin[i]]++; r.deepestY = Mathf.Min(r.deepestY, w.y); }
                float depth = 0;
                foreach (var s in surfaces)
                    if (w.x >= s.bounds.min.x && w.x <= s.bounds.max.x && w.z >= s.bounds.min.z && w.z <= s.bounds.max.z && w.y < s.bounds.max.y - SinkEpsilon && w.y > s.bounds.max.y - SlabDepth)
                        depth = Mathf.Max(depth, s.bounds.max.y - w.y);
                if (depth <= 0) continue;
                if (p.body[i]) { r.bodySink++; r.bodyMm = Mathf.Max(r.bodyMm, depth * 1000); }
                else { r.housingSink++; r.housingMm = Mathf.Max(r.housingMm, depth * 1000); }
            }
            var tube = p.tubeOwner ? p.tubeOwner.Find("VIS_" + p.name + "_Tube")?.GetComponent<MeshFilter>() : null;
            if (tube)
            {
                var tubeVerts = tube.sharedMesh.vertices; var tubeToWorld = tube.transform.localToWorldMatrix;
                var centres = new Vector3[tubeVerts.Length / ResearchS1Cable.TubeSides];
                for (int i = 0; i < tubeVerts.Length; i++)
                {
                    var w = tubeToWorld.MultiplyPoint3x4(tubeVerts[i]);
                    centres[i / ResearchS1Cable.TubeSides] += w / ResearchS1Cable.TubeSides; r.deepestY = Mathf.Min(r.deepestY, w.y);
                    float depth = 0;
                    foreach (var s in surfaces)
                        if (w.x >= s.bounds.min.x && w.x <= s.bounds.max.x && w.z >= s.bounds.min.z && w.z <= s.bounds.max.z && w.y < s.bounds.max.y - SinkEpsilon && w.y > s.bounds.max.y - SlabDepth)
                            depth = Mathf.Max(depth, s.bounds.max.y - w.y);
                    if (depth > 0) { r.bodySink++; r.bodyMm = Mathf.Max(r.bodyMm, depth * 1000); }
                }
                // Ring centres sit ~3.5 mm apart on the FX1 lead; 4 rings apart matches the 16 mm triples used for skins.
                for (int k = 4; k + 4 < centres.Length; k++) r.radiusMm = Mathf.Min(r.radiusMm, Circumradius(centres[k - 4], centres[k], centres[k + 4]) * 1000);
            }
            else for (int k = 2; k + 2 < p.bins; k++)
            {
                if (n[k - 2] < 6 || n[k] < 6 || n[k + 2] < 6 || p.binChain[k - 2] != p.binChain[k + 2]) continue;
                r.radiusMm = Mathf.Min(r.radiusMm, Circumradius(sums[k - 2] / n[k - 2], sums[k] / n[k], sums[k + 2] / n[k + 2]) * 1000);
            }
            for (int c = 0; c < p.chains.Count; c++)
            {
                var chain = p.chains[c];
                for (int i = 0; i < chain.Length - 1; i++)
                {
                    r.nodeMinY = Mathf.Min(r.nodeMinY, chain[i].position.y);
                    if (i == 0 || tube) continue; // the plug housing is rigid with its head bone; tube bones are folded away
                    Vector3 predicted = chain[i].TransformPoint(p.localNext[c][i]), actual = chain[i + 1].position;
                    r.misalignDeg = Mathf.Max(r.misalignDeg, Vector3.Angle(predicted - chain[i].position, actual - chain[i].position));
                    r.gapMm = Mathf.Max(r.gapMm, Vector3.Distance(predicted, actual) * 1000);
                }
            }
            return r;
        }

        static float Circumradius(Vector3 a, Vector3 b, Vector3 c)
        {
            float area = Vector3.Cross(b - a, c - a).magnitude / 2;
            return area > 1e-10f ? Vector3.Distance(a, b) * Vector3.Distance(b, c) * Vector3.Distance(a, c) / (4 * area) : float.PositiveInfinity;
        }

        // The baked mesh is rendered, not the live skin: a manual Camera.Render may show last frame's skinning.
        static void Shots(string dir, string tag, string id, Probe p, Vector3 target, Vector3 away)
        {
            var go = new GameObject("CableCheckBaked"); go.transform.SetPositionAndRotation(p.skin.transform.position, p.skin.transform.rotation);
            p.skin.BakeMesh(p.baked, true);
            go.AddComponent<MeshFilter>().sharedMesh = p.baked; go.AddComponent<MeshRenderer>().sharedMaterials = p.skin.sharedMaterials;
            bool was = p.skin.enabled; p.skin.enabled = false;
            string file = id.Replace('|', '_');
            var outward = Vector3.ProjectOnPlane(away, Vector3.up).normalized; var across = Vector3.Cross(Vector3.up, outward);
            Render(Path.Combine(dir, file + "_side_" + tag + ".png"), target + outward * .32f + Vector3.up * .03f, target);
            Render(Path.Combine(dir, file + "_persp_" + tag + ".png"), target + outward * .28f + across * .2f + Vector3.up * .22f, target);
            p.skin.enabled = was; UnityEngine.Object.DestroyImmediate(go);
        }

        static void Render(string file, Vector3 eye, Vector3 target)
        {
            const int W = 960, H = 720;
            var go = new GameObject("CableCheckCamera"); var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = 40; cam.nearClipPlane = .01f; cam.farClipPlane = 30;
            go.transform.position = eye; go.transform.LookAt(target);
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
            cam.targetTexture = rt; cam.Render();
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = previous; File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
        }

        // Per-call cost of the per-frame shape update: still ends (path cache hit) and a held end moving 1 mm every call (re-solve).
        static string Cost(ResearchS1TaskAMechanics task, ResearchTrainingStation station, Action poseLead)
        {
            const int N = 400; var sb = new StringBuilder("cost (Editor, per call, N=" + N + ")\n");
            (double ms, long bytes) Time(Action step)
            {
                step(); var watch = Stopwatch.StartNew();
                for (int i = 0; i < N; i++) step();
                double ms = watch.Elapsed.TotalMilliseconds / N;
                // GetAllocatedBytesForCurrentThread reads 0 under Unity Mono; a short heap delta is the usable proxy (min of 3 tries skips a GC).
                long bytes = long.MaxValue;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    long before = GC.GetTotalMemory(false);
                    for (int i = 0; i < 20; i++) step();
                    long delta = GC.GetTotalMemory(false) - before;
                    if (delta >= 0) bytes = Math.Min(bytes, delta / 20);
                }
                return (ms, bytes == long.MaxValue ? -1 : bytes);
            }
            foreach (var cable in task.wiring.cables)
            {
                var still = Time(cable.RefreshShape);
                sb.AppendLine(cable.assetId + " still ms=" + still.ms.ToString("F4") + " B=" + still.bytes);
            }
            foreach (var end in task.wiring.ends)
            {
                var plug = end.plug.transform; Vector3 p0 = plug.position; int k = 0;
                var moving = Time(() => { plug.position = p0 + Vector3.right * (.001f * (k++ % 2)); end.cable.RefreshShape(); });
                plug.position = p0; end.cable.RefreshShape();
                sb.AppendLine(end.cable.assetId + " moving ms=" + moving.ms.ToString("F4") + " B=" + moving.bytes);
            }
            var head = station.leadHead.transform; Vector3 h0 = head.position; int j = 0;
            var leadStill = Time(poseLead);
            var leadMoving = Time(() => { head.position = h0 + Vector3.up * (.02f + .001f * (j++ % 2)); poseLead(); });
            head.position = h0; poseLead();
            sb.AppendLine("FX1_LEAD still ms=" + leadStill.ms.ToString("F4") + " B=" + leadStill.bytes + " moving ms=" + leadMoving.ms.ToString("F4") + " B=" + leadMoving.bytes);
            return sb.ToString();
        }
    }
}
#endif
