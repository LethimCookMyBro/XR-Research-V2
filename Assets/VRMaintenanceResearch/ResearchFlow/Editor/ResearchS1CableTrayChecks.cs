#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // S1_LAYOUT_CABLE_TRAY_20261110: stopped-Editor check of the authored Task A cable rest state, with stills for a human to look at.
    // Not Quest input or wearer proof.
    public static class ResearchS1CableTrayChecks
    {
        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor and a fresh evidence directory required.");
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            if (!task || !task.wiring) throw new InvalidOperationException("Authored Task A required.");
            Directory.CreateDirectory(directory);
            var checks = new List<object>(); var failures = new List<string>();
            void Check(bool pass, string name, object observed) { checks.Add(new { name, pass, observed }); if (!pass) failures.Add(name); }
            var pivot = task.servicePivot;
            Physics.SyncTransforms();
            var trays = pivot.Cast<Transform>().Where(t => t.name.StartsWith("S1_CableTray_", StringComparison.Ordinal)).ToArray();
            Check(trays.Length == 1, "one cable tray turns with the case", trays.Select(t => t.name).ToArray());
            foreach (var end in task.wiring.ends)
            {
                string id = end.plug.stableId; var cable = end.cable;
                foreach (var plug in new[] { end.sourcePlug, end.plug })
                {
                    var head = plug.transform; var park = cable.ParkOf(head);
                    var start = park == null ? default : cable.SlotPose(park.slots[0]);
                    Check(park != null && !plug.Socket && Vector3.Distance(head.position, start.position) < .0001f && Quaternion.Angle(head.rotation, start.rotation) < .01f,
                        plug.stableId + "/head lies loose in its start slot", park != null ? Vector3.Distance(head.position, start.position) : -1);
                    Check(!head.IsChildOf(task.fixedAssembly[1]) && !head.IsChildOf(task.fixedAssembly[4]) && head.GetComponent<ResearchS1TaskAInput>(), plug.stableId + "/head is a free input, not fixed to the board or PSU", head.parent.name);
                    var solid = head.GetComponentsInChildren<BoxCollider>(true).Where(b => !b.isTrigger).Select(b => b.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                    var under = trays.SelectMany(t => t.GetComponentsInChildren<BoxCollider>()).Select(b => b.bounds)
                        .Where(b => solid.center.x > b.min.x && solid.center.x < b.max.x && solid.center.z > b.min.z && solid.center.z < b.max.z).ToArray();
                    float gap = under.Length == 1 ? solid.min.y - under[0].max.y : float.NaN;
                    Check(under.Length == 1 && gap > 0 && gap < .003f, plug.stableId + "/head rests just above one tray", gap);
                    // No solid of the case, board, PSU or another head overlaps it: nothing of a task cable is inside the case.
                    var inside = Physics.OverlapBox(solid.center, solid.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                        .Where(c => !c.transform.IsChildOf(head) && !trays.Any(t => c.transform.IsChildOf(t))).Select(c => c.name).ToArray();
                    Check(inside.Length == 0, plug.stableId + "/head touches no other solid", inside);
                }
                Check(end.sourceSockets.All(s => !s.Occupant), id + "/source ports start empty", end.sourceSockets.Length);
                var span = cable.spans[0]; // head to head; a second span only carries the PCIe +2 block
                cable.RefreshShape();
                Check(cable.fold != null && cable.Loose && Vector3.Distance(span.to.position, cable.FoldedPose(span.to).position) < .0005f,
                    id + "/cable lies folded and loose at both ends", cable.fold != null);
                // Each lead bone against every solid that is not this cable's own heads or the tray.
                var hits = new HashSet<string>();
                foreach (var bone in span.bones.Skip(1))
                    foreach (var c in Physics.OverlapSphere(bone.position, .002f, ~0, QueryTriggerInteraction.Ignore))
                        if (!c.transform.IsChildOf(end.plug.transform) && !c.transform.IsChildOf(end.sourcePlug.transform) && !trays.Any(t => c.transform.IsChildOf(t))) hits.Add(c.name);
                Check(hits.Count == 0, id + "/cable passes through no solid", hits.ToArray());
                var local = span.bones.Select(b => cable.transform.InverseTransformPoint(b.position)).ToArray();
                Check(local.All(p => p.x < -.02f && p.x > -.44f && Mathf.Abs(p.z) < .21f), id + "/whole cable lies on the tray beside the rear end of the case", local.Min(p => p.x));
                Check(cable.Allows(end.plug.transform, Matrix4x4.TRS(end.plug.transform.position + Vector3.one, Quaternion.Euler(40, 70, 10), Vector3.one)),
                    id + "/a cable loose at both ends never limits the hand", true);
            }
            var spare = task.wiring.cables.SelectMany(c => c.skin.bones).Where(b => b.name.EndsWith("_SATA3_C") || b.name.EndsWith("_SATA3_D") || b.name.EndsWith("_PCIE8X2_C1") || b.name.EndsWith("_PCIE8X2_C2")).ToArray();
            Check(spare.Length == 4 && spare.All(b => b.lossyScale == Vector3.zero), "four spare daisy-chain connectors are folded away", spare.Length);
            Check(task.AuthoredBindingsValid() && task.wiring.contacts.Count(c => c.Connected) == 13, "Task A bindings valid and thirteen fixed base contacts connected", task.wiring.contacts.Count(c => c.Connected));
            var state = task.Capture();
            Check(state.baseState.powerIsolated && state.cables.All(c => !c.sourceConnected && !c.targetConnected), "base isolated, three task cables loose at both ends", state.baseState.powerIsolated);
            Check(ResearchS1TaskEvaluator.Evaluate(state).Category == S1InspectionCategory.AssemblyIncomplete, "untouched start state inspects as assembly incomplete", ResearchS1TaskEvaluator.Evaluate(state).Category.ToString());
            var centre = pivot.position + Vector3.up * .17f;
            Still(Path.Combine(directory, "participant.png"), pivot.TransformPoint(new Vector3(.2f, .62f, -.85f)), centre, 60);
            Still(Path.Combine(directory, "open_side.png"), pivot.TransformPoint(new Vector3(.25f, .32f, -.5f)), pivot.TransformPoint(new Vector3(.25f, .08f, 0)), 50);
            Still(Path.Combine(directory, "rear_side.png"), pivot.TransformPoint(new Vector3(.25f, .36f, .6f)), pivot.TransformPoint(new Vector3(.25f, .11f, 0)), 50);
            Still(Path.Combine(directory, "top.png"), pivot.TransformPoint(new Vector3(.05f, 1.5f, -.02f)), pivot.TransformPoint(new Vector3(.05f, 0, 0)), 50);
            Still(Path.Combine(directory, "start_tray.png"), pivot.TransformPoint(new Vector3(-.2f, .5f, -.55f)), pivot.TransformPoint(new Vector3(-.2f, 0, 0)), 50);
            string status = failures.Count == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(directory, "cable-tray.json"), JsonConvert.SerializeObject(new { status, layout_version = ResearchUIPolicy.LayoutVersion,
                scope = "Stopped Editor authored rest state and stills; not Quest input or wearer proof", checks, failures }, Formatting.Indented));
            return "CABLE_TRAY_" + status + " checks=" + checks.Count + " failures=" + string.Join(",", failures);
        }

        // The states a participant leaves a task cable in: one head in a port with the other waiting on the tray, and both heads
        // in ports, for every port of the family and both SSD and card positions. Each lead must touch no solid, and with both
        // heads in ports it must take its authored route. routesPath tries a route file without saving it. The posed scene is discarded.
        public static string Plugged(string directory, string routesPath = null)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || Directory.Exists(directory))
                throw new InvalidOperationException("Stopped Editor, a saved scene and a fresh evidence directory required.");
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            Directory.CreateDirectory(directory);
            var checks = new List<object>(); var failures = new List<string>();
            void Check(bool pass, string name, object observed) { checks.Add(new { name, pass, observed }); if (!pass) failures.Add(name); }
            string V(Vector3 v) => v.x.ToString("F3") + " " + v.y.ToString("F3") + " " + v.z.ToString("F3");
            try
            {
                if (!string.IsNullOrEmpty(routesPath)) ResearchS1TaskABuilder.RouteTaskCables(task, routesPath);
                var pivot = task.servicePivot; var a = task;
                void Seat(Transform root, Transform mate, Transform seat, float turn = 0)
                {
                    var pose = VRMaintenanceResearch.AssetLibraryQA.SocketRules.RootPose(seat.localToWorldMatrix, 0, turn, root.worldToLocalMatrix * mate.localToWorldMatrix);
                    root.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
                }
                for (int index = 0; index < 3; index++)
                {
                    var end = a.wiring.ends[index]; var cable = end.cable; var span = cable.spans[0];
                    void Park(VRMaintenanceResearch.AssetLibraryQA.QAPlug plug, int slot)
                    { var pose = cable.SlotPose(cable.ParkOf(plug.transform).slots[slot]); plug.transform.SetPositionAndRotation(pose.position, pose.rotation); }
                    void Measure(string state, bool routed, string still)
                    {
                        Physics.SyncTransforms(); span.cached = false; cable.RefreshShape();
                        var path = span.cachedPath; var hits = new List<string>(); var local = cable.transform.worldToLocalMatrix;
                        if (path != null)
                            for (int p = 0; p + 1 < path.Length; p++)
                            {
                                if (Vector3.Distance(path[p], path[0]) < .03f || Vector3.Distance(path[p + 1], path[path.Length - 1]) < .03f) continue;
                                foreach (var c in Physics.OverlapCapsule(path[p], path[p + 1], .003f, ~0, QueryTriggerInteraction.Ignore))
                                    if (!c.transform.IsChildOf(span.from) && !c.transform.IsChildOf(span.to)) hits.Add(c.name + "@" + V(local.MultiplyPoint3x4(path[p])));
                            }
                        string id = end.plug.stableId + "/" + state;
                        // Why an authored route was not taken: its straight-line length against the cable, and whether it solves with no radius limit.
                        string routeNote = null;
                        {
                            Vector3 la = local.MultiplyPoint3x4(span.from.TransformPoint(span.fromExit)), lb = local.MultiplyPoint3x4(span.to.TransformPoint(span.toExit));
                            var route = cable.routes == null ? null : Array.Find(cable.routes, r => r.from.Contains(la) && r.to.Contains(lb));
                            if (route != null)
                            {
                                var knots = new[] { la }.Concat(route.knots.Select(k => k.anchor == 1 ? la + k.offset : k.anchor == 2 ? lb + k.offset : k.offset)).Append(lb).ToArray();
                                float poly = 0; for (int k = 1; k < knots.Length; k++) poly += Vector3.Distance(knots[k - 1], knots[k]);
                                var world = knots.Skip(1).Take(knots.Length - 2).Select(k => cable.transform.TransformPoint(k)).ToArray();
                                Vector3 wa = span.from.TransformPoint(span.fromExit), wb = span.to.TransformPoint(span.toExit), wda = span.from.TransformVector(span.fromDirection), wdb = span.to.TransformVector(span.toDirection);
                                bool free = ResearchS1CablePath.SolveGuided(wa, wb, wda, wdb, span.length, world, cable.transform.TransformVector(route.slack), 0, route.weights) != null;
                                bool limited = ResearchS1CablePath.SolveGuided(wa, wb, wda, wdb, span.length, world, cable.transform.TransformVector(route.slack), ResearchS1Cable.RouteRadius, route.weights) != null;
                                routeNote = "poly " + poly.ToString("F3") + " of " + span.length.ToString("F3") + " solves_r0=" + free + " solves_radius=" + limited;
                            }
                            else if (routed) routeNote = "no route box holds these exits";
                        }
                        Check(path != null && hits.Count == 0 && (!routed || span.cachedGuided), id, new { guided = span.cachedGuided, solved = path != null,
                            hits = hits.GroupBy(h => h.Split('@')[0]).Select(g => g.Key + " x" + g.Count() + " first " + g.First().Split('@')[1]).ToArray(),
                            a = V(local.MultiplyPoint3x4(span.from.TransformPoint(span.fromExit))), da = V(local.MultiplyVector(span.from.TransformVector(span.fromDirection)).normalized),
                            b = V(local.MultiplyPoint3x4(span.to.TransformPoint(span.toExit))), db = V(local.MultiplyVector(span.to.TransformVector(span.toDirection)).normalized),
                            length = span.length, route = routeNote, low = path == null ? 0 : path.Min(q => local.MultiplyPoint3x4(q).y) });
                        if (still == null) return;
                        var centre = pivot.TransformPoint(new Vector3(.2f, .17f, 0));
                        Still(Path.Combine(directory, still + "_open.png"), pivot.TransformPoint(new Vector3(.12f, .42f, -.75f)), centre, 50);
                        Still(Path.Combine(directory, still + "_rear.png"), pivot.TransformPoint(new Vector3(.12f, .42f, .75f)), centre, 50);
                        Still(Path.Combine(directory, still + "_top.png"), pivot.TransformPoint(new Vector3(.12f, 1.25f, -.02f)), pivot.TransformPoint(new Vector3(.12f, 0, 0)), 50);
                    }
                    for (int device = 0; device < 2; device++)
                    {
                        QASocketSeat(index, device, out var port);
                        void QASocketSeat(int cableIndex, int variant, out Transform seat)
                        {
                            if (cableIndex == 2)
                            {
                                var card = a.gpus[1]; Seat(card.transform, card.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal)), a.gpuSlots[variant].seat);
                                seat = a.wiring.targets.Single(t => t.part == card && t.connector == S1Connector.GpuPcie8).socket.seat;
                            }
                            else
                            {
                                var sled = a.sleds[0]; Seat(sled.transform, sled.mate, a.traySeats[variant]);
                                seat = a.wiring.targets.Single(t => t.part == a.ssds[0] && t.connector == (cableIndex == 0 ? S1Connector.SsdData : S1Connector.SsdPower)).socket.seat;
                            }
                        }
                        // Device end plugged first: the board/PSU head waits on the side of the tray the plugged end is on.
                        Seat(end.plug.transform, end.plug.mates.Single(), port); Park(end.sourcePlug, index == 2 ? 1 : 2);
                        Measure("device" + device + "_source_waiting", false, device == 0 ? "c" + index + "_device_only" : null);
                        for (int source = 0; source < end.sourceSockets.Length; source++)
                        {
                            Seat(end.sourcePlug.transform, end.sourcePlug.mates.Single(), end.sourceSockets[source].seat);
                            if (device == 0) { Park(end.plug, index == 0 ? 2 : 1); // the SATA data head waits at the rear corner (S1CablePark.waitSlot)
                             Measure("source" + source + "_device_waiting", false, source == 0 ? "c" + index + "_source_only" : null); }
                            Seat(end.plug.transform, end.plug.mates.Single(), port);
                            Measure("source" + source + "_device" + device, true, source == 0 && device == 0 ? "c" + index + "_both" : null);
                        }
                    }
                    // Leave this cable plugged at its first ports for the stills of the next one.
                    Seat(end.sourcePlug.transform, end.sourcePlug.mates.Single(), end.sourceSockets[0].seat);
                }
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path, UnityEditor.SceneManagement.OpenSceneMode.Single); }
            string status = failures.Count == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(directory, "cable-plugged.json"), JsonConvert.SerializeObject(new { status, layout_version = ResearchUIPolicy.LayoutVersion,
                scope = "Stopped Editor, heads posed in their ports and tray slots; not Quest input or wearer proof", checks, failures }, Formatting.Indented));
            return "CABLE_PLUGGED_" + status + " checks=" + checks.Count + " failures=" + failures.Count;
        }

        // Discards unsaved scene state (ExecuteAlways components dirty the scene after a script reload) and reopens the scene from disk.
        public static string Reload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Editor required.");
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return "reloaded " + scene.path + " was_dirty=" + dirty;
        }

        public static void Still(string file, Vector3 position, Vector3 target, float fieldOfView)
        {
            var go = new GameObject("S1StillCamera") { hideFlags = HideFlags.HideAndDontSave };
            var texture = new RenderTexture(1280, 800, 24); var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            var skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToDictionary(s => s, s => s.forceMatrixRecalculationPerRender);
            try
            {
                foreach (var skin in skins.Keys) skin.forceMatrixRecalculationPerRender = true;
                var camera = go.AddComponent<Camera>(); camera.fieldOfView = fieldOfView; camera.nearClipPlane = .03f; camera.farClipPlane = 60;
                var fill = go.AddComponent<Light>(); fill.type = LightType.Point; fill.range = 3; fill.intensity = 1; fill.shadows = LightShadows.None;
                go.transform.position = position; go.transform.LookAt(target);
                camera.targetTexture = texture; camera.Render();
                RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
                File.WriteAllBytes(file, image.EncodeToPNG());
            }
            finally
            {
                foreach (var pair in skins) if (pair.Key) pair.Key.forceMatrixRecalculationPerRender = pair.Value;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
#endif
