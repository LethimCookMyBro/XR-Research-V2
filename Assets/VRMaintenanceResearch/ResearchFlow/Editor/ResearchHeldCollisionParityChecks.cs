#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchHeldCollisionParityChecks
    {
        sealed class Query
        {
            public string id;
            public Transform root;
            public Matrix4x4 pose;
            public Collider contact;
            public bool? expected;
        }

        sealed class RunResult
        {
            public int run, calls, accepted;
            public string implementation;
            public double milliseconds;
            public long allocatedBytes;
        }

        static string[] SceneState() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty; }).ToArray();

        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Editor required.");
            if (Directory.Exists(directory)) throw new InvalidOperationException("Use a fresh evidence directory.");
            Directory.CreateDirectory(directory);
            var originalScene = SceneManager.GetActiveScene();
            var before = SceneState();
            var queries = new List<Query>();
            var mismatch = new List<string>();
            var details = new List<object>();
            var runs = new List<RunResult>();
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[4096];
            bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > probeBefore;
            GC.KeepAlive(allocationProbe);
            var parts = UnityEngine.Object.FindObjectsByType<ResearchS1FanPart>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.gameObject.scene == originalScene).OrderBy(p => p.stableId).Select(p => p.transform)
                .Concat(UnityEngine.Object.FindObjectsByType<QAPlug>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(p => p.gameObject.scene == originalScene && p.GetComponent<ResearchS1TaskAInput>()).OrderBy(p => p.stableId).Select(p => p.transform))
                .ToArray();
            foreach (var part in parts)
            {
                foreach (var delta in new[] { Vector3.zero, Vector3.up * .003f, Vector3.up * .02f, Vector3.right * .04f, Vector3.forward * -.02f })
                    queries.Add(new Query { id = "canonical:" + part.name + ":" + delta, root = part,
                        pose = Matrix4x4.TRS(part.position + delta, part.rotation, part.lossyScale) });
                queries.Add(new Query { id = "canonical:" + part.name + ":rotate5", root = part,
                    pose = Matrix4x4.TRS(part.position, Quaternion.AngleAxis(5, Vector3.up) * part.rotation, part.lossyScale) });
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                BoxCollider Box(string name, Vector3 at, Vector3 size, Transform parent = null)
                {
                    var go = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    if (parent) go.transform.SetParent(parent, true);
                    go.transform.position = at;
                    var collider = go.AddComponent<BoxCollider>(); collider.size = size;
                    return collider;
                }
                void Add(string id, Transform root, Vector3 delta, bool? expected, Quaternion? rotation = null, Collider contact = null) => queries.Add(new Query
                { id = id, root = root, pose = Matrix4x4.TRS(root.position + delta, rotation ?? root.rotation, root.lossyScale), expected = expected, contact = contact });
                var empty = Box("EmptySpace", new Vector3(40, 8, 40), Vector3.one * .02f);
                Add("empty_free", empty.transform, Vector3.right * .02f, true);
                Add("travel_cap", empty.transform, Vector3.right, false);
                var thin = Box("ThinBarrierMover", new Vector3(44, 8, 40), Vector3.one * .02f);
                Box("ThinBarrier", thin.transform.position + Vector3.right * .03f, new Vector3(.0001f, .2f, .2f));
                Add("thin_barrier_crossing", thin.transform, Vector3.right * .06f, false);
                var overlap = Box("InitialOverlapMover", new Vector3(48, 8, 40), Vector3.one * .05f);
                var wall = Box("InitialOverlapWall", overlap.transform.position + Vector3.right * .04f, Vector3.one * .05f);
                Add("initial_overlap_escape", overlap.transform, Vector3.left * .02f, true);
                Add("initial_overlap_deeper", overlap.transform, Vector3.right * .01f, false);
                Add("thread_contact_exception", overlap.transform, Vector3.right * .01f, true, contact: wall);
                var rot = Box("RotationMover", new Vector3(52, 8, 40), new Vector3(.2f, .02f, .02f));
                Box("RotationBarrier", rot.transform.position + new Vector3(.06f, .03464f, 0), new Vector3(.008f, .008f, .08f));
                Add("rotation_swept_contact", rot.transform, Vector3.zero, false, Quaternion.Euler(0, 0, 60));
                var dense = Box("DenseBroadphaseMover", new Vector3(56, 8, 40), Vector3.one * .02f);
                for (int i = 0; i < 100; i++)
                    Box("DenseForeign" + i, dense.transform.position + new Vector3(-.025f, ((i % 10) - 4.5f) * .0015f, ((i / 10) - 4.5f) * .0015f), Vector3.one * .001f);
                Add("overlap_buffer_saturation", dense.transform, Vector3.right * .02f, true);
                var ignored = Box("IgnoredMover", new Vector3(60, 8, 40), Vector3.one * .02f);
                var ignoredWall = Box("IgnoredWall", ignored.transform.position + Vector3.right * .02f, Vector3.one * .02f);
                Physics.IgnoreCollision(ignored, ignoredWall);
                Add("ignored_collision", ignored.transform, Vector3.right * .04f, true);
                var trigger = Box("TriggerMover", new Vector3(64, 8, 40), Vector3.one * .02f);
                Box("IgnoredTrigger", trigger.transform.position + Vector3.right * .02f, Vector3.one * .02f).isTrigger = true;
                Add("trigger_ignored", trigger.transform, Vector3.right * .04f, true);
                var unsupported = Box("Unsupported", new Vector3(68, 8, 40), Vector3.one * .02f);
                unsupported.gameObject.AddComponent<SphereCollider>().radius = .01f;
                Add("unsupported_collider", unsupported.transform, Vector3.right * .02f, false);
                Physics.SyncTransforms();
                foreach (var q in queries)
                {
                    bool baseline = ResearchS1HeldCollisionBaseline.Allows(q.root, q.pose, q.contact);
                    bool candidate = ResearchS1HeldCollision.Allows(q.root, q.pose, q.contact);
                    details.Add(new { q.id, baseline, candidate, q.expected, active = q.root.gameObject.activeInHierarchy });
                    if (baseline != candidate || (q.expected.HasValue && baseline != q.expected.Value)) mismatch.Add(q.id);
                }
                int overlapCapacity = ((Collider[])typeof(ResearchS1HeldCollision).GetField("overlaps", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Length;
                if (overlapCapacity <= 64) mismatch.Add("overlap_buffer_did_not_grow");
                var castCenter = new Vector3(72, 8, 40);
                for (int i = 0; i < 100; i++) Box("CastSaturation" + i, castCenter + Vector3.right * (.05f + i * .001f), Vector3.one * .001f);
                Physics.SyncTransforms();
                int castBaseline = Physics.BoxCastAll(castCenter, Vector3.one * .01f, Vector3.right, Quaternion.identity, .2f, ~0, QueryTriggerInteraction.Ignore).Length;
                int castCandidate = (int)typeof(ResearchS1HeldCollision).GetMethod("Cast", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { castCenter, Vector3.one * .01f, Vector3.right, Quaternion.identity, .2f });
                if (castBaseline != castCandidate || castBaseline < 65) mismatch.Add("cast_saturation_count");
                Func<Query, bool> baselineCall = q => ResearchS1HeldCollisionBaseline.Allows(q.root, q.pose, q.contact);
                Func<Query, bool> candidateCall = q => ResearchS1HeldCollision.Allows(q.root, q.pose, q.contact);
                // Warm both sides before five alternating pairs. Every result contributes to accepted count.
                foreach (var q in queries) { baselineCall(q); candidateCall(q); }
                RunResult Measure(int run, string name, Func<Query, bool> call)
                {
                    var result = new RunResult { run = run, implementation = name };
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    foreach (var q in queries) { if (call(q)) result.accepted++; result.calls++; }
                    result.milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    result.allocatedBytes = allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
                    return result;
                }
                for (int i = 0; i < 5; i++)
                {
                    if ((i & 1) == 0) { runs.Add(Measure(i, "baseline", baselineCall)); runs.Add(Measure(i, "candidate", candidateCall)); }
                    else { runs.Add(Measure(i, "candidate", candidateCall)); runs.Add(Measure(i, "baseline", baselineCall)); }
                }
                foreach (var group in runs.GroupBy(r => r.run))
                    if (group.Select(r => r.accepted).Distinct().Count() != 1 || group.Select(r => r.calls).Distinct().Count() != 1) mismatch.Add("run_result_mismatch:" + group.Key);
                object Summary(string name)
                {
                    var values = runs.Where(r => r.implementation == name).OrderBy(r => r.milliseconds).ToArray();
                    return new { median_ms = values[2].milliseconds, min_ms = values[0].milliseconds, max_ms = values[4].milliseconds,
                        median_allocated_bytes = values.Select(r => r.allocatedBytes).OrderBy(x => x).ElementAt(2), calls_per_run = values[0].calls };
                }
                File.WriteAllText(Path.Combine(directory, "result.json"), JsonConvert.SerializeObject(new
                {
                    status = mismatch.Count == 0 ? "PASS_EDITOR_PARITY" : "FAIL", scope = "Stopped Editor helper microbenchmark only, not Quest frame or wearer blur evidence",
                    canonical_parts = parts.Length, cases = queries.Count, mismatches = mismatch, overlapCapacity, castBaseline, castCandidate,
                    baseline = Summary("baseline"), candidate = Summary("candidate"), runs, details,
                    machine = new { cores = Environment.ProcessorCount, unity = Application.unityVersion },
                    limiter = "Main-thread Physics queries, matrix construction and managed allocations; other running work may contend. Both sides alternate."
                }, Formatting.Indented));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            }
            bool restored = before.SequenceEqual(SceneState());
            File.WriteAllText(Path.Combine(directory, "restore.txt"), "scene_state_restored=" + restored);
            if (!restored || mismatch.Count > 0) throw new InvalidOperationException("Collision parity or scene restoration failed; inspect " + directory);
            return "PASS_EDITOR_PARITY cases=" + queries.Count + " runs=5_pairs restored=" + restored;
        }
    }
}
#endif
