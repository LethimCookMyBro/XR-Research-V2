#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchPcFastenerMotionChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(name);
        }
        static void SetField(object value, string name, object state) => FindField(value.GetType(), name).SetValue(value, state);
        static T Field<T>(object value, string name) => (T)FindField(value.GetType(), name).GetValue(value);
        static void SetProperty(object value, string name, object state) =>
            value.GetType().GetProperty(name, Hidden).GetSetMethod(true).Invoke(value, new[] { state });
        static object Call(object value, string name, Type[] types, params object[] args) =>
            value.GetType().GetMethod(name, Hidden, null, types, null).Invoke(value, args);
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty; }).ToArray();

        // expectedModified=false is for the original Fastener source under a root-owned source transaction.
        // It does not patch or swap source, load a scene, save a scene, or control any device.
        public static string Run(string directory, bool expectedModified = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Stopped Editor with XR inactive required.");
            if (!Path.IsPathRooted(directory) || Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("Fresh absolute evidence directory required.");
            var donor = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            if (!donor) throw new InvalidOperationException("Authored Task A donor required.");
            var callerScenes = Scenes();
            int previews = EditorSceneManager.previewSceneCount;
            var originalSockets = QASocket.All.ToArray();
            var scene = EditorSceneManager.NewPreviewScene();
            var rows = new List<object>();
            var failures = new List<string>();
            int checks = 0;
            GameObject root = null, controlRoot = null;
            void Check(bool ok, string name, object value = null)
            {
                checks++; rows.Add(new { name, passed = ok, value });
                if (!ok) failures.Add(name);
            }
            try
            {
                root = UnityEngine.Object.Instantiate(donor.gameObject);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.name = "TEST_PC_FASTENER_MOTION_CLONE";
                // Separate the owned fixture from the donor's coincident world colliders.
                // Internal clone collisions and all cable/held-collision predicates remain active.
                root.transform.position += new Vector3(100, 0, 0);
                root.SetActive(true);
                // QAPlug.IsHeld and child-held gates query every SDK PointableElement, not only the screw.
                // In a stopped preview none of those components is guaranteed to have received Awake.
                foreach (var pointable in root.GetComponentsInChildren<PointableElement>(true))
                {
                    if (pointable.SelectingPoints != null) continue;
                    SetField(pointable, "_points", new List<Pose>());
                    SetField(pointable, "_pointIds", new List<int>());
                    SetField(pointable, "_selectingPoints", new List<Pose>());
                    SetField(pointable, "_selectingPointIds", new List<int>());
                }
                foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    var locker = body.GetComponent<RigidbodyKinematicLocker>() ?? body.gameObject.AddComponent<RigidbodyKinematicLocker>();
                    SetField(locker, "_rigidbody", body);
                }
                var pc = root.GetComponent<ResearchS1TaskAMechanics>();
                controlRoot = new GameObject("TEST_PC_INPUT_CONTEXT");
                SceneManager.MoveGameObjectToScene(controlRoot, scene);
                var controller = controlRoot.AddComponent<ResearchSessionFlowController>();
                var guard = controlRoot.AddComponent<HandsLabInputGuard>();
                var pointerRoot = new GameObject("TEST_PC_POINTER");
                pointerRoot.transform.SetParent(controlRoot.transform, false);
                var pointer = pointerRoot.AddComponent<RayInteractor>();
                // Stopped-Editor objects may not have run Awake. Only this owned TEST pointer is initialized.
                try { _ = pointer.Identifier; }
                catch (NullReferenceException) { Call(pointer, "Awake", Type.EmptyTypes); }
                guard.interactionsRoot = pointerRoot;
                SetProperty(guard, "TrackingValid", true);
                SetProperty(guard, "Mode", HandsLabInputGuard.InputMode.Controller);
                controller.guards = new[] { guard };
                pc.controller = controller;
                var port = controlRoot.AddComponent<ResearchTaskPort>();
                port.taskId = "A"; port.demoMode = true;
                pc.port = port;
                port.BindSnapshot(() => pc.Capture(), _ => throw new InvalidOperationException("This test must not evaluate task correctness."), _ => { });
                port.SetInteractionAllowed(true);
                int pointerId = pointer.Identifier;
                Check(pc.InputSource(pointerId) != "None", "owned input source resolves");
                if (pc.InputSource(pointerId) == "None") throw new InvalidOperationException("TEST pointer not resolved by real input gate.");

                // Arrange a physically seated GPU in the owned clone, without a task answer or session credit.
                var gpu = pc.gpus[0]; var gpuSocket = pc.gpuSlots[0]; var gpuMate = gpu.mates[0];
                SetProperty(gpu, "Socket", gpuSocket); SetProperty(gpuSocket, "Occupant", gpu);
                SetProperty(gpuSocket, "Mate", gpuMate); SetProperty(gpuSocket, "State", SocketState.Seated);
                var gpuPose = SocketRules.RootPose(gpuSocket.seat.localToWorldMatrix, 0, 0, gpu.MateInRoot(gpuMate));
                gpu.transform.SetPositionAndRotation(gpuPose.GetColumn(3), gpuPose.rotation);
                Check(pc.GpuSeated(0), "GPU fixture seated at actual authored socket");
                var sled = pc.sleds[0];
                SetField(sled, "tray", 0);
                SetField(sled, "motion", new S1SledMotion(S1SledPhase.Hooked, 0, 0));
                var sledMate = sled.transform.worldToLocalMatrix * sled.mate.localToWorldMatrix;
                var sledPose = pc.traySeats[0].localToWorldMatrix * sledMate.inverse;
                sled.transform.SetPositionAndRotation(sledPose.GetColumn(3), sledPose.rotation);
                Check(sled.Placement == S1Placement.Seated, "captive fixture sled seated");
                Physics.SyncTransforms();

                foreach (var fastener in new[] { pc.gpuScrews[0], sled.thumb })
                {
                    string kind = fastener.Captive ? "captive" : "gpu";
                    var input = fastener.input;
                    input.owner = pc;
                    var points = fastener.grabbable.GrabPoints;
                    if (points == null)
                    {
                        SetField(fastener.grabbable, "_selectingPoints", new List<Pose>());
                        SetField(fastener.grabbable, "_selectingPointIds", new List<int>());
                        points = fastener.grabbable.GrabPoints;
                    }
                    fastener.Initialize(fastener.grabbable);
                    // Initialize binds SDK points only. Runtime Start captures these matrices.
                    // Stopped-Editor preview clones do not receive runtime Start automatically.
                    Call(fastener, "Start", Type.EmptyTypes);
                    Check(Mathf.Abs(Field<Matrix4x4>(fastener, "mateInRoot").determinant) > 1e-8f,
                        kind + "/runtime Start initialized non-singular mate matrix");
                    Matrix4x4 Seat() => fastener.Captive ? fastener.captiveSeat.localToWorldMatrix
                        : pc.gpuScrewSeats[0].localToWorldMatrix * Matrix4x4.Translate(new Vector3(0, 0, -.0009f));
                    void End()
                    {
                        fastener.EndTransform();
                        var attempt = Field<ResearchTaskPort.Attempt>(input, "attempt");
                        if (attempt != null) port.EndAttempt(attempt, true);
                        SetField(input, "attempt", null); SetField(input, "endPending", false);
                        points.Clear();
                    }
                    void Begin(float initial)
                    {
                        End();
                        SetField(fastener, "socketIndex", fastener.Captive ? -1 : 0);
                        SetField(fastener, "progress", initial);
                        var pose = (Matrix4x4)Call(fastener, "ThreadPose", Type.EmptyTypes);
                        Call(fastener, "Place", new[] { typeof(Matrix4x4) }, pose);
                        points.Add(new Pose(fastener.transform.position, fastener.transform.rotation));
                        SetField(input, "pointer", pointerId);
                        SetField(input, "cancelled", false);
                        SetField(input, "attempt", port.BeginAttempt(input.stableId, pc.InputSource(pointerId)));
                        fastener.BeginTransform();
                        if (!input.Allowed) throw new InvalidOperationException(kind + " fixture failed real input.Allowed.");
                    }
                    void Offer(float initial, float delta, float lateral, float tilt)
                    {
                        var mateInRoot = Field<Matrix4x4>(fastener, "mateInRoot");
                        float depth = (float)ResearchS1FanKinematics.ThreadDepthMm(initial, fastener.FullDegrees, fastener.travelMm);
                        var swing = Quaternion.AngleAxis(tilt, Vector3.right);
                        var twist = Quaternion.AngleAxis(-initial - delta, Vector3.forward);
                        var desired = Seat() * Matrix4x4.TRS(new Vector3(lateral / 1000, 0, -depth / 1000), swing * twist, Vector3.one) * mateInRoot.inverse;
                        var hand = desired * Field<Matrix4x4>(fastener, "grabToRoot").inverse;
                        points[0] = new Pose(hand.GetColumn(3), hand.rotation);
                        var offered = SocketRules.Measure(Seat(), desired * mateInRoot);
                        Check(Mathf.Abs(Mathf.DeltaAngle(-initial, offered.turnDeg) + delta) < .1f,
                            kind + "/offered twist is actual 20 degree delta", offered.ToString());
                        if (lateral > 0) Check(offered.lateralMm > 29 && offered.tiltDeg > 19,
                            kind + "/offered off-axis geometry exists", offered.ToString());
                    }
                    float RunTwist(float initial, float lateral, float tilt)
                    {
                        Begin(initial); Offer(initial, 20, lateral, tilt);
                        var expectedPose = SocketRules.RootPose(Seat(),
                            (float)ResearchS1FanKinematics.ThreadDepthMm(Mathf.Min(initial + 20, fastener.FullDegrees), fastener.FullDegrees, fastener.travelMm),
                            -Mathf.Min(initial + 20, fastener.FullDegrees), Field<Matrix4x4>(fastener, "mateInRoot"));
                        var permission = pc.MotionBlockReason(fastener.transform, expectedPose);
                        Check(permission == PartBlockReason.None, kind + "/real cable and collision gates permit constrained pose", permission.ToString());
                        if (permission != PartBlockReason.None) throw new InvalidOperationException(kind + " fixture pose rejected: " + permission);
                        fastener.UpdateTransform();
                        float result = fastener.ProgressDegrees;
                        rows.Add(new { name = kind + "/sample", initial, lateral, tilt, result,
                            reason = port.CurrentInteraction?.Reason.ToString(), state = fastener.State.ToString() });
                        return result;
                    }
                    float centered = RunTwist(120, 0, 0);
                    Check(Mathf.Abs(centered - 140) < .1f, kind + "/centered advances 20 degrees", centered);
                    float offset = RunTwist(120, 30, 20);
                    Check(Mathf.Abs(offset - (expectedModified ? centered : 120)) < .1f,
                        kind + (expectedModified ? "/engaged off-axis equals centered" : "/baseline reproduces off-axis lock"), offset);
                    Check(fastener.SocketIndex == (fastener.Captive ? -1 : 0), kind + "/capture retained");

                    float zeroOffset = RunTwist(0, 30, 20);
                    float expectedZero = fastener.Captive && expectedModified ? 20 : 0;
                    Check(Mathf.Abs(zeroOffset - expectedZero) < .1f,
                        kind + "/zero progress preserves free-entry versus captive distinction", zeroOffset);

                    Begin(120);
                    if (fastener.Captive) SetField(sled, "tray", -1); else SetProperty(gpu, "Socket", null);
                    Offer(120, 20, 0, 0); fastener.UpdateTransform();
                    Check(Mathf.Abs(fastener.ProgressDegrees - 120) < .1f, kind + "/missing retained prerequisite blocks");
                    if (expectedModified) Check(port.CurrentInteraction?.Reason == PartBlockReason.Retained, kind + "/retained branch reason");
                    if (fastener.Captive) SetField(sled, "tray", 0); else SetProperty(gpu, "Socket", gpuSocket);

                    float atStop = RunTwist(fastener.FullDegrees, 0, 0);
                    Check(Mathf.Abs(atStop - fastener.FullDegrees) < .1f, kind + "/end stop retained", atStop);
                    if (expectedModified) Check(port.CurrentInteraction?.Reason == PartBlockReason.EndStop, kind + "/end stop reason");
                    Begin(120); Offer(120, 20, 0, 0);
                    port.SetInteractionAllowed(false); fastener.UpdateTransform();
                    Check(Mathf.Abs(fastener.ProgressDegrees - 120) < .1f && !input.Allowed,
                        kind + "/paused input gate prevents motion");
                    Check(port.CurrentInteraction == null, kind + "/pause clears live feedback");
                    port.SetInteractionAllowed(true); End();
                }
            }
            catch (Exception error)
            {
                var actual = error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;
                Check(false, "execution", actual.ToString());
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (controlRoot) UnityEngine.Object.DestroyImmediate(controlRoot);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Check(callerScenes.SequenceEqual(Scenes()) && previews == EditorSceneManager.previewSceneCount, "caller scene and preview ownership restored");
            Check(originalSockets.SequenceEqual(QASocket.All), "global socket registry restored");
            Directory.CreateDirectory(directory);
            string status = failures.Count == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(directory, "pc-fastener-motion.json"), JsonConvert.SerializeObject(new
            {
                status, expected_modified = expectedModified, checks, failures, rows,
                scope = "Stopped Editor owned preview clone, translated 100 m from donor to avoid duplicate source colliders. Actual Fastener.UpdateTransform and SDK GrabPoints. Runtime Start is invoked on cloned fasteners. Private setup seeds a hooked sled, seated GPU, technical attempt and tracked TEST input guard; not native controller or wearer evidence. Real gate getters and internal cable/collision predicates remain active. No external bench/world collision acceptance, evaluator or task completion.",
                source_sha256 = Hash(File.ReadAllBytes(Path.Combine(Application.dataPath, "VRMaintenanceResearch/ResearchFlow/Runtime/ResearchS1TaskAFastener.cs")))
            }, Formatting.Indented));
            return "PC_FASTENER_MOTION_" + status + " checks=" + checks + " expected_modified=" + expectedModified;
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
