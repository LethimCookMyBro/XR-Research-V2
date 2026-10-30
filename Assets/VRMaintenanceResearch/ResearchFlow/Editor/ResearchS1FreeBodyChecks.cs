#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1FreeBodyChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }
        static void Set(object value, string name, object data) => Field(value.GetType(), name).SetValue(value, data);
        static object Call(object value, string name) => value.GetType().GetMethod(name, Hidden).Invoke(value, null);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        public static string Run(string directory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Fresh absolute evidence directory required.");
            var a = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            var b = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!a || !b) throw new InvalidOperationException("Both canonical task donors are required.");
            var donors = new[] { a.ram[0].gameObject, a.gpus[0].gameObject, a.sleds[0].gameObject,
                a.gpuScrews[0].gameObject, b.frontGuard.gameObject, b.rearGuard.gameObject, b.blade.gameObject, b.spinner.gameObject,
                a.wiring.ends[0].plug.gameObject };
            var beforeScenes = Scenes();
            var beforeActive = SceneManager.GetActiveScene();
            var rows = new List<object>();
            var failures = new List<string>();
            Scene scene = default;
            int count = 0;
            string current = "";
            void Check(bool ok, string name, object value = null)
            {
                count++;
                rows.Add(new { donor = current, name, passed = ok, value = value?.ToString() });
                if (!ok) failures.Add(current + ":" + name);
            }
            try
            {
                scene = EditorSceneManager.NewPreviewScene();

                var physics = scene.GetPhysicsScene();
                var floor = new GameObject("TEST_SOLID_FLOOR");
                SceneManager.MoveGameObjectToScene(floor, scene);
                var box = floor.AddComponent<BoxCollider>();
                box.center = new Vector3(0, -.1f, 0);
                box.size = new Vector3(20, .2f, 20);
                foreach (var donor in donors)
                {
                    current = donor.name;
                    var clone = UnityEngine.Object.Instantiate(donor);
                    clone.name = "TEST_FREE_" + donor.name;
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.SetActive(true);
                    foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    var body = clone.GetComponent<Rigidbody>();
                    var grab = clone.GetComponent<Grabbable>();
                    if (!body || !grab) throw new InvalidOperationException("Donor lacks body or grab: " + current);
                    foreach (var child in clone.GetComponentsInChildren<Rigidbody>(true))
                        if (child != body) { child.isKinematic = true; child.useGravity = false; }
                    grab.enabled = true;
                    Set(grab, "_points", new List<Pose>());
                    Set(grab, "_pointIds", new List<int>());
                    Set(grab, "_selectingPoints", new List<Pose>());
                    Set(grab, "_selectingPointIds", new List<int>());
                    Set(grab, "OneGrabTransformer", null);
                    Set(grab, "TwoGrabTransformer", null);
                    Set(grab, "_activeTransformer", null);
                    Set(grab, "_targetTransform", clone.transform);
                    grab.InjectOptionalRigidbody(body);
                    grab.InjectOptionalKinematicWhileSelected(true);
                    grab.MaxGrabPoints = 1;
                    var locker = clone.GetComponent<RigidbodyKinematicLocker>() ?? clone.AddComponent<RigidbodyKinematicLocker>();
                    Set(locker, "_rigidbody", body);
                    Set(locker, "_counter", 0);
                    var solids = clone.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.attachedRigidbody == body).ToArray();
                    Check(solids.Length > 0, "real donor owns solid colliders", solids.Length);
                    if (solids.Length == 0) { UnityEngine.Object.DestroyImmediate(clone); continue; }
                    void PoseAt(Vector3 position)
                    {
                        clone.transform.SetPositionAndRotation(position, Quaternion.identity);
                        body.position = position; body.rotation = Quaternion.identity;
                        Physics.SyncTransforms();
                    }
                    PoseAt(new Vector3(0, 1, 0));
                    float raise = .7f - solids.Min(c => c.bounds.min.y);
                    PoseAt(body.position + Vector3.up * raise);
                    var start = body.position;
                    bool originalKinematic = body.isKinematic, originalGravity = body.useGravity;
                    for (int i = 0; i < 50; i++) physics.Simulate(.01f);
                    Check(originalKinematic && !originalGravity && Vector3.Distance(start, body.position) < .0001f,
                        "baseline donor floats", new { originalKinematic, originalGravity, start, end = body.position });
                    bool bound = false, allowed = true, tether = false;
                    float clock = 1;
                    var helper = clone.AddComponent<ResearchS1FreeBody>();
                    helper.Configure(body, grab, () => bound, () => allowed,
                        pose => !tether || Mathf.Abs(((Vector3)pose.GetColumn(3)).x) <= .2f, () => clock);
                    void Sync() => Call(helper, "LateUpdate");
                    void Tick(int n)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            Call(helper, "FixedUpdate");
                            physics.Simulate(.01f);
                            Call(helper, "FixedUpdate");
                            Sync();
                        }
                    }
                    Tick(160);
                    Check(!body.isKinematic && body.useGravity && body.position.y < start.y - .25f,
                        "free donor drops", new { start, end = body.position });
                    Check(body.linearVelocity.magnitude < .15f && solids.Min(c => c.bounds.min.y) > -.035f,
                        "free donor rests on solid floor", new { speed = body.linearVelocity.magnitude, minY = solids.Min(c => c.bounds.min.y) });
                    PoseAt(start);
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                    void Send(PointerEventType type, int id = 73)
                    {
                        var pose = new Pose(body.position, body.rotation);
                        if (type == PointerEventType.Select && !((List<int>)Field(grab.GetType(), "_pointIds").GetValue(grab)).Contains(id))
                            grab.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Hover, pose));
                        grab.ProcessPointerEvent(new PointerEvent(id, type, pose));
                        if (type == PointerEventType.Unselect)
                            grab.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Unhover, pose));
                    }
                    void MoveForThrow()
                    {
                        Send(PointerEventType.Select);
                        Sync();
                        Check(body.isKinematic && body.interpolation == RigidbodyInterpolation.None,
                            "SDK selected body is kinematic without interpolation");
                        for (int i = 0; i < 20; i++)
                        {
                            clock += .02f;
                            PoseAt(start + Vector3.right * (i * .01f));
                            body.rotation = Quaternion.Euler(0, i * 3, 0); clone.transform.rotation = body.rotation;
                            Send(PointerEventType.Move);
                            Sync();
                        }
                        clock += .02f;
                    }
                    MoveForThrow();
                    Send(PointerEventType.Unselect);
                    Sync();
                    float releaseSpeed = body.linearVelocity.x;
                    float releaseSpin = body.angularVelocity.magnitude; var releaseRotation = body.rotation;
                    var releasePosition = body.position;
                    Tick(20);
                    Check(releaseSpeed > .1f && body.position.x > releasePosition.x + .015f,
                        "real SDK accepted-pose samples produce horizontal throw", new { releaseSpeed, releasePosition, end = body.position });
                    Check(releaseSpin > .1f && Quaternion.Angle(releaseRotation, body.rotation) > 1, "SDK accepted rotation produces spin", new { releaseSpin });
                    allowed = false; helper.Refresh();
                    var paused = body.position;
                    Tick(30);
                    Check(body.isKinematic && Vector3.Distance(paused, body.position) < .0001f, "pause freezes flight");
                    allowed = true; helper.Refresh();
                    Check(!body.isKinematic && body.linearVelocity.magnitude < .001f, "resume clears old throw velocity");
                    bound = true; helper.Refresh();
                    var retained = body.position;
                    Tick(30);
                    Check(body.isKinematic && Vector3.Distance(retained, body.position) < .0001f, "assembly-bound body stays retained");
                    bound = false; helper.Refresh();
                    PoseAt(start);
                    MoveForThrow();
                    Send(PointerEventType.Cancel);
                    Sync();
                    Check(body.linearVelocity.magnitude < .001f, "cancel does not apply throw impulse", body.linearVelocity);
                    PoseAt(start); body.linearVelocity = body.angularVelocity = Vector3.zero;
                    tether = true;
                    Sync();
                    body.linearVelocity = Vector3.right * 2;
                    Tick(100);
                    Check(Mathf.Abs(body.position.x) <= .2001f && float.IsFinite(body.position.sqrMagnitude),
                        "tether rejects escape without nonfinite state", body.position);
                    tether = false;
                    for (int i = 0; i < 100; i++)
                    {
                        Send(PointerEventType.Select); Sync();
                        Send(PointerEventType.Cancel); Sync();
                    }
                    Check(!locker.IsLocked && !body.isKinematic, "100 select/cancel cycles balance SDK and helper locks");
                    UnityEngine.Object.DestroyImmediate(clone);
                }
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                if (beforeActive.IsValid() && beforeActive.isLoaded) SceneManager.SetActiveScene(beforeActive);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            }
            Check(beforeScenes.SequenceEqual(Scenes()) && SceneManager.GetActiveScene() == beforeActive,
                "caller scenes, active scene and dirty flags preserved");
            Directory.CreateDirectory(directory);
            var report = new { evidence = "EDITOR_ISOLATED_PHYSICS_AND_SDK_SYNTHETIC_NOT_QUEST", checks = count, failed = failures.Count, failures, rows };
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
            return "FREE_BODY_CHECKS " + (failures.Count == 0 ? "PASS" : "FAIL") + " checks=" + count + " failed=" + failures.Count;
        }
    }
}
#endif

