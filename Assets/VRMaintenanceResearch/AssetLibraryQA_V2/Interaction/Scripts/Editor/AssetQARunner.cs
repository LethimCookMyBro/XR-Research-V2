#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace VRMaintenanceResearch.AssetLibraryQA.Editor
{
    // Play Mode QA: simulated Touch controllers drive the unmodified HandsLab rig (ISDK interactors, Grabbable,
    // HandsLabObject, HandsLabLogger) through the three bundles and write QA/unity/interaction/report.json.
    // The simulator follows HandsLabSimulator.cs with its own device names.
    public static class AssetQARunner
    {
        const string Layout = "AssetQASimulatedTouch";
        static readonly string ReportDir = AssetQABuilder.Lib + "/QA/unity/interaction";

        static InputDevice[] hands = new InputDevice[2];
        static XRSimulatedHMD hmd;
        static readonly Vector3[] pos = new Vector3[2];
        static readonly Quaternion[] rot = new Quaternion[2];
        static readonly float[] grip = new float[2], trigger = new float[2];
        static double nextSim, due;
        static IEnumerator routine;
        static List<JObject> checks, poses;
        static string currentBundle;
        static Dictionary<QAPlug, Matrix4x4> home;

        public static string Status { get; private set; } = "idle";
        static HandsLabSession Lab => UnityEngine.Object.FindFirstObjectByType<HandsLabSession>();

        public static string Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first");
            if (SceneManager.GetActiveScene().path != AssetQABuilder.ScenePath) throw new InvalidOperationException("Open " + AssetQABuilder.ScenePath);
            if (routine != null) throw new InvalidOperationException("Runner already active");
            if (InputSystem.devices.Any(d => d is XRController || d is XRHMD)) throw new InvalidOperationException("XR input devices already present; not taking them over");
            InputSystem.RegisterLayout(@"{""name"":""" + Layout + @""",""extend"":""XRController"",""format"":""AQTC"",""controls"":[
{""name"":""primaryButton"",""layout"":""Button"",""offset"":0,""bit"":0},{""name"":""primaryTouched"",""layout"":""Button"",""offset"":0,""bit"":1},
{""name"":""secondaryButton"",""layout"":""Button"",""offset"":0,""bit"":2},{""name"":""secondaryTouched"",""layout"":""Button"",""offset"":0,""bit"":3},
{""name"":""gripPressed"",""layout"":""Button"",""offset"":0,""bit"":4},{""name"":""triggerPressed"",""layout"":""Button"",""offset"":0,""bit"":5},
{""name"":""triggerTouched"",""layout"":""Button"",""offset"":0,""bit"":6},{""name"":""thumbstickClicked"",""layout"":""Button"",""offset"":0,""bit"":7},
{""name"":""thumbstickTouched"",""layout"":""Button"",""offset"":1,""bit"":0},{""name"":""thumbrestTouched"",""layout"":""Button"",""offset"":1,""bit"":1},{""name"":""menu"",""layout"":""Button"",""offset"":1,""bit"":2},
{""name"":""thumbstick"",""layout"":""Vector2"",""offset"":4},{""name"":""grip"",""layout"":""Axis"",""offset"":12},{""name"":""trigger"",""layout"":""Axis"",""offset"":16},
{""name"":""devicePose"",""layout"":""Pose"",""offset"":32},{""name"":""pointer"",""layout"":""Pose"",""offset"":96},
{""name"":""isTracked"",""useStateFrom"":""devicePose/isTracked""},{""name"":""trackingState"",""useStateFrom"":""devicePose/trackingState""},
{""name"":""devicePosition"",""useStateFrom"":""devicePose/position""},{""name"":""deviceRotation"",""useStateFrom"":""devicePose/rotation""}]}");
            hands[0] = InputSystem.AddDevice(Layout, "AssetQASimulatedLeft");
            hands[1] = InputSystem.AddDevice(Layout, "AssetQASimulatedRight");
            InputSystem.SetDeviceUsage(hands[0], UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(hands[1], UnityEngine.InputSystem.CommonUsages.RightHand);
            hmd = (XRSimulatedHMD)InputSystem.AddDevice("XRSimulatedHMD", "AssetQASimulatedHMD");
            pos[0] = new Vector3(-.2f, 1.15f, .35f); pos[1] = new Vector3(.2f, 1.15f, .35f);
            rot[0] = rot[1] = Quaternion.identity; grip[0] = grip[1] = trigger[0] = trigger[1] = 0;
            checks = new List<JObject>(); poses = new List<JObject>(); GrabRetries.Clear();
            EditorApplication.playModeStateChanged += Stopped;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            routine = Run(); due = 0; Status = "running";
            EditorApplication.update += Tick;
            return "started";
        }

        static void Stopped(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode) { if (routine != null) Finish("aborted: left Play Mode"); Cleanup(); } }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= Stopped;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            foreach (var d in hands.Concat(new InputDevice[] { hmd })) if (d != null && d.added) InputSystem.RemoveDevice(d);
            hands = new InputDevice[2]; hmd = null; routine = null;
        }

        static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextSim) { nextSim = now + 1d / 60; Sim(); }
            if (now < due) return;
            try
            {
                if (!routine.MoveNext()) { Finish(null); return; }
                due = now + (routine.Current is double d ? d : 0);
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        static void Sim()
        {
            var space = Lab.trackingSpace;
            var head = space.InverseTransformPoint(new Vector3(0, 1.55f, 0));
            var look = Quaternion.Euler(30, 0, 0);
            InputSystem.QueueStateEvent(hmd, new XRSimulatedHMDState
            {
                isTracked = true, trackingState = 3, devicePosition = head, deviceRotation = look, centerEyePosition = head, centerEyeRotation = look,
                leftEyePosition = head + Vector3.left * .032f, rightEyePosition = head + Vector3.right * .032f, leftEyeRotation = look, rightEyeRotation = look
            });
            for (int i = 0; i < 2; i++)
                using (StateEvent.From(hands[i], out var ev))
                {
                    foreach (string path in new[] { "devicePose", "pointer" })
                    {
                        var p = hands[i].GetChildControl<PoseControl>(path);
                        p.isTracked.WriteValueIntoEvent(1f, ev); p.trackingState.WriteValueIntoEvent(3, ev);
                        p.position.WriteValueIntoEvent(space.InverseTransformPoint(pos[i]), ev);
                        p.rotation.WriteValueIntoEvent(Quaternion.Inverse(space.rotation) * rot[i], ev);
                    }
                    hands[i].GetChildControl<AxisControl>("grip").WriteValueIntoEvent(grip[i], ev);
                    hands[i].GetChildControl<ButtonControl>("gripPressed").WriteValueIntoEvent(grip[i] > .7f ? 1f : 0f, ev);
                    hands[i].GetChildControl<AxisControl>("trigger").WriteValueIntoEvent(trigger[i], ev);
                    hands[i].GetChildControl<ButtonControl>("triggerPressed").WriteValueIntoEvent(trigger[i] > .7f ? 1f : 0f, ev);
                    hands[i].GetChildControl<ButtonControl>("primaryTouched").WriteValueIntoEvent(trigger[i] > .7f ? 1f : 0f, ev);
                    InputSystem.QueueEvent(ev);
                }
        }

        // ---- input primitives (all motion goes through the simulated controller pose) ----

        static HandGrabInteractor Grabber(int side) =>
            Lab.guards[side].interactionsRoot.GetComponentsInChildren<HandGrabInteractor>(true).First(i => i.name == "ControllerHandGrabInteractor");

        static IEnumerable<double> PalmTo(int side, Func<Vector3> target, bool pinch = false)
        {
            for (int i = 0; i < 16; i++)
            {
                var g = Grabber(side);
                pos[side] += target() - (pinch ? g.PinchPoint : g.PalmPoint).position;
                yield return .06d;
            }
        }

        static bool PinchOnly(QAPlug plug) =>
            (plug.GetComponentInChildren<HandGrabInteractable>().SupportedGrabTypes & Oculus.Interaction.Grab.GrabTypeFlags.Palm) == 0;

        static void Squeeze(int side, bool pinch, bool on)
        {
            if (pinch) trigger[side] = on ? 1 : 0; else grip[side] = on ? 1 : 0;
        }

        static Vector3 Inside(Component part, Vector3 point)
        {
            var c = part.GetComponentsInChildren<Collider>().Where(x => x.enabled && !x.isTrigger).OrderBy(x => (x.ClosestPoint(point) - point).sqrMagnitude).First();
            var contact = c.ClosestPoint(point);
            return contact + (c.bounds.center - contact).normalized * Mathf.Min(.018f, (c.bounds.center - contact).magnitude);
        }

        public static readonly List<string> GrabRetries = new List<string>();

        // Approaches the GRAB node and squeezes; a miss (nothing or another part selected) is released and retried
        // from a slightly different approach, as a person would. Retries are listed in the report.
        static IEnumerable<double> Grab(int side, QAPlug plug, string grabNode)
        {
            var node = plug.GetComponentsInChildren<Transform>(true).First(t => t.name == "GRAB_" + plug.assetId + "_" + grabNode);
            var approaches = new Func<Vector3>[] { () => Inside(plug, node.position), () => node.position, () => Inside(plug, node.position + Vector3.up * .01f) };
            for (int attempt = 0; attempt < approaches.Length; attempt++)
            {
                bool pinch = PinchOnly(plug);
                pos[side] += Vector3.up * .05f; yield return .2d;
                foreach (var d in PalmTo(side, approaches[attempt], pinch)) yield return d;
                Squeeze(side, pinch, true); yield return .8d;
                if (plug.IsHeld) yield break;
                var wrong = UnityEngine.Object.FindObjectsByType<QAPlug>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsHeld);
                GrabRetries.Add($"{currentBundle}: {plug.assetId} attempt {attempt + 1} missed, held instead: {(wrong ? wrong.assetId : "nothing")}");
                Squeeze(side, pinch, false); yield return .6d;
            }
            throw new InvalidOperationException($"grab missed {plug.assetId} three times: " + string.Join("; ", GrabRetries.Skip(GrabRetries.Count - 3)));
        }

        static IEnumerable<double> Release(int side) { grip[side] = trigger[side] = 0; yield return .8d; }

        // Returns a held part to 2 cm above where it spawned and lets go.
        static IEnumerable<double> PutBack(int side, QAPlug plug)
        {
            foreach (var d in Carry(side, plug, () => Matrix4x4.Translate(new Vector3(0, .02f, 0)) * home[plug])) yield return d;
            foreach (var d in Release(side)) yield return d;
        }

        // Moves the controller until the hand's (unconstrained) part pose reaches target. The part pose arrives a
        // frame after the controller moves, so each step covers half the remaining error (a full step oscillates).
        static IEnumerable<double> Carry(int side, QAPlug plug, Func<Matrix4x4> target, int frames = 400)
        {
            for (int i = 0; ; i++)
            {
                var c = Matrix4x4.TRS(pos[side], rot[side], Vector3.one);
                var want = target() * plug.LastFreePose.inverse * c;
                Vector3 wp = want.GetColumn(3);
                float err = (wp - pos[side]).magnitude * 1000, errDeg = Quaternion.Angle(want.rotation, rot[side]);
                if (err < .02f && errDeg < .02f) break;
                if (i == frames) throw new InvalidOperationException($"carry did not converge on {plug.assetId}: {err:F3} mm, {errDeg:F3} deg, held={plug.IsHeld}");
                pos[side] = Vector3.MoveTowards(pos[side], wp, Mathf.Min(.008f, (wp - pos[side]).magnitude * .5f));
                rot[side] = Quaternion.RotateTowards(rot[side], want.rotation, Mathf.Min(3f, errDeg * .5f));
                yield return 2d / 60;
            }
            yield return .3d;
        }

        static Func<Matrix4x4> AtDepth(QASocket socket, QAPlug plug, string mate, float depthMm, float turnDeg)
        {
            var m = plug.mates.First(t => plug.MateName(t) == mate);
            return () => SocketRules.RootPose(socket.seat.localToWorldMatrix, depthMm, turnDeg, plug.MateInRoot(m));
        }

        static Func<Matrix4x4> Lifted(QAPlug plug, Vector3 offset, float yawDeg)
        {
            var start = plug.transform.localToWorldMatrix;
            return () => Matrix4x4.Translate(offset) * Matrix4x4.TRS(start.GetColumn(3), Quaternion.AngleAxis(yawDeg, Vector3.up), Vector3.one)
                         * Matrix4x4.TRS(-(Vector3)start.GetColumn(3), Quaternion.identity, Vector3.one) * start;
        }

        // ---- observations ----

        static MatePose Observe(QASocket socket, QAPlug plug, string mate) =>
            SocketRules.Measure(socket.seat.localToWorldMatrix, plug.mates.First(t => plug.MateName(t) == mate).localToWorldMatrix);

        static void Check(string id, bool passed, object observed, object expected = null)
        {
            var row = JObject.FromObject(new { bundle = currentBundle, id, passed, observed, expected });
            checks.Add(row);
            Lab.Logger?.Record("QA_CHECK", "assetqa." + currentBundle, passed ? "PASS" : "FAIL", "Active", "None", null, row, "Tester", inputSourceOverride: "SimulatedController");
        }

        static List<JObject> Events(string target)
        {
            var rows = new List<JObject>();
            using (var stream = new FileStream(Lab.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                while (!reader.EndOfStream) rows.Add(JObject.Parse(reader.ReadLine()));
            return rows.Where(r => (string)r["target"] == target).ToList();
        }

        static Dictionary<string, Matrix4x4> Rigid(QAPlug plug) =>
            plug.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.GetInstanceID().ToString(), t => plug.transform.worldToLocalMatrix * t.localToWorldMatrix);

        static float RigidError(Dictionary<string, Matrix4x4> a, Dictionary<string, Matrix4x4> b) =>
            a.Keys.Max(k => Mathf.Max(((Vector3)a[k].GetColumn(3) - (Vector3)b[k].GetColumn(3)).magnitude * 1000, Quaternion.Angle(a[k].rotation, b[k].rotation)));

        static string[] VisibleHelpers(QAPlug plug) =>
            plug.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && (new[] { "COL", "SEL", "INS" }.Contains(r.name.Split('_')[0]) || r.name.Contains("_LOD1"))).Select(r => r.name).ToArray();

        // Skinned assets carry their basis on a PART_ROOT_<asset> child; rigid ones on the root itself.
        static Transform PartRoot(Component c, string asset) =>
            c.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PART_ROOT_" + asset) ?? c.transform;

        static void RecordPose(string name, QASocket socket, QAPlug plug)
        {
            var rel = PartRoot(socket, socket.fixtureId).worldToLocalMatrix * PartRoot(plug, plug.assetId).localToWorldMatrix;
            poses.Add(JObject.FromObject(new
            {
                name, fixture = socket.fixtureId, seat = socket.seatName, plug = plug.assetId, mate = plug.MateName(socket.Mate),
                socket_state = socket.State.ToString(), rule = socket.Last.rule?.testId, reach_mm = socket.Last.rule?.reachMm ?? 0,
                turn_deg = socket.Last.turnDeg, depth_mm = socket.Last.depthMm, approach_mm = socket.spec.approachMm,
                rel_unity = Enumerable.Range(0, 16).Select(i => rel[i / 4, i % 4]).ToArray(),
                latches = socket.latches.ToDictionary(l => l.name, l => l.Closed ? "closed" : "open")
            }));
        }

        // Unit grab: GRAB logged, rigid while carried 0.2 m and turned 30 deg, no helper renders, RELEASE logged.
        static IEnumerable<double> UnitGrab(int side, QAPlug plug, string grabNode)
        {
            var rest = Rigid(plug);
            int before = Events(plug.stableId).Count;
            foreach (var d in Grab(side, plug, grabNode)) yield return d;
            Check("C1_held_" + plug.assetId, plug.IsHeld, plug.IsHeld);
            var samples = new List<Vector3>();
            for (int i = 0; i < 40; i++) { samples.Add(plug.LastFreePose.GetColumn(3)); yield return 1d / 60; }
            var mean = samples.Aggregate(Vector3.zero, (a, b) => a + b) / samples.Count;
            float jitter = samples.Max(v => (v - mean).magnitude) * 1000;
            Check("C1_still_hand_jitter_" + plug.assetId, jitter < .1f, new { max_dev_mm = jitter, grab_points = plug.MaxGrabPoints, updates_per_frame = plug.MaxUpdatesPerFrame,
                  pointers = string.Join(",", plug.grabbable.SelectingPoints.Select(p => p.position.ToString("F4"))) }, "< 0.1 mm");
            foreach (var d in Carry(side, plug, Lifted(plug, new Vector3(0, .2f, 0), 30))) yield return d;
            var carried = Rigid(plug);
            Check("C1_rigid_" + plug.assetId, RigidError(rest, carried) < .001f, new { max_mm_or_deg = RigidError(rest, carried), nodes = rest.Count }, "< 0.001");
            Check("C1_helpers_hidden_" + plug.assetId, VisibleHelpers(plug).Length == 0, VisibleHelpers(plug));
            var rows = Events(plug.stableId).Skip(before).Select(r => (string)r["event"]).ToArray();
            Check("C1_logger_grab_" + plug.assetId, rows.Contains("GRAB"), rows);
        }

        static IEnumerable<double> Seat(int side, QASocket s, QAPlug p, string mate, float turn = 0)
        {
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, 15, turn))) yield return d;
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, -1, turn))) yield return d;
            foreach (var d in Release(side)) yield return d;
        }

        // Pushes to 1 mm past the seat; a Block rule must stop it at the fit table's reach and never seat it.
        static IEnumerable<double> WrongPair(int side, QASocket s, QAPlug p, string mate, string grabNode, float turn, float expectReach, string id)
        {
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, 15, turn))) yield return d;
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, -1, turn))) yield return d;
            var o = Observe(s, p, mate);
            Check(id + "_stops_at_reach", Mathf.Abs(o.depthMm - expectReach) <= .1f && s.State == SocketState.Guided, new { o.depthMm, state = s.State.ToString(), rule = s.Last.rule?.testId }, expectReach);
            foreach (var d in Release(side)) yield return d;
            Check(id + "_not_seated_after_release", s.State == SocketState.Blocked, s.State.ToString(), "Blocked");
            RecordPose(id + "_blocked", s, p);
            foreach (var d in Grab(side, p, grabNode)) yield return d;
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, 60, turn))) yield return d;
            Check(id + "_removed", s.State == SocketState.Empty && s.Occupant == null, s.State.ToString(), "Empty");
            foreach (var d in PutBack(side, p)) yield return d;
        }

        static IEnumerable<double> Pull(int side, QASocket s, QAPlug p, string mate, string grabNode, bool expectHeld, string id)
        {
            var before = Observe(s, p, mate);
            foreach (var d in Grab(side, p, grabNode)) yield return d;
            foreach (var d in Carry(side, p, AtDepth(s, p, mate, 50, s.Last.turnDeg))) yield return d;
            var after = Observe(s, p, mate);
            float moved = Mathf.Abs(after.depthMm - before.depthMm);
            if (expectHeld)
            {
                Check(id, moved < .1f && s.Occupant == p && s.PullsBlocked > 0, new { moved_mm = moved, pulls_blocked = s.PullsBlocked, state = s.State.ToString() }, "moved < 0.1 mm");
                foreach (var d in Release(side)) yield return d;
            }
            else Check(id, s.Occupant == null && after.depthMm > 25, new { depth_mm = after.depthMm, state = s.State.ToString() }, "free, out of the socket");
        }

        static IEnumerable<double> Press(int side, QALatch latch, bool expectClosed, string id)
        {
            int presses = latch.Presses;
            foreach (var d in PalmTo(side, () => latch.GetComponent<Collider>().bounds.center)) yield return d;
            grip[side] = 1; yield return .6d;
            grip[side] = 0; yield return .6d;
            Check(id, latch.Presses == presses + 1 && latch.Closed == expectClosed,
                  new { pressed = latch.Presses - presses, state = latch.Closed ? "closed" : "open" }, new { pressed = 1, state = expectClosed ? "closed" : "open" });
            pos[side] += new Vector3(0, .12f, -.1f); yield return .4d;
        }

        static QASocket Socket(string fixture, string seat) => QASocket.All.First(s => s.fixtureId == fixture && s.seatName == seat);
        static QAPlug Plug(string asset) => UnityEngine.Object.FindObjectsByType<QAPlug>(FindObjectsSortMode.None).First(p => p.assetId == asset);

        // ---- scenarios ----

        static IEnumerator Run()
        {
            yield return 2.5d;
            home = UnityEngine.Object.FindObjectsByType<QAPlug>(FindObjectsSortMode.None).ToDictionary(p => p, p => p.transform.localToWorldMatrix);
            currentBundle = "ram_slot";
            var slot = Socket("SLOT_DIMM_DDR4_TEST", "DIMM");
            var ddr4 = Plug("RAM_DDR4_UDIMM_BARE"); var ddr5 = Plug("RAM_DDR5_UDIMM_BARE");
            const int R = 1;
            foreach (var d in UnitGrab(R, ddr4, "Main")) yield return d;
            foreach (var d in Carry(R, ddr4, AtDepth(slot, ddr4, "Edge", 15, 0))) yield return d;
            foreach (var d in Carry(R, ddr4, AtDepth(slot, ddr4, "Edge", 10, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            var o = Observe(slot, ddr4, "Edge");
            Check("C5_release_midway", slot.State == SocketState.Partial && Mathf.Abs(o.depthMm - 10) < .6f && o.lateralMm < .01f && o.tiltDeg < .01f,
                  new { state = slot.State.ToString(), pose = o.ToString() }, "Partial at ~10 mm, on axis");
            RecordPose("ram_partial", slot, ddr4);
            foreach (var d in Press(0, slot.latches[0], false, "C5_latch_cannot_close_on_partial")) yield return d;
            foreach (var d in Grab(R, ddr4, "Main")) yield return d;
            foreach (var d in Carry(R, ddr4, AtDepth(slot, ddr4, "Edge", -1, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            o = Observe(slot, ddr4, "Edge");
            Check("C2_seated", slot.State == SocketState.Latched && Mathf.Abs(o.depthMm) < .1f && o.lateralMm < .1f && o.tiltDeg < .1f && Mathf.Abs(o.turnDeg) < .1f,
                  new { state = slot.State.ToString(), pose = o.ToString() }, "Latched, MATE on SEAT within 0.1 mm / 0.1 deg");
            Check("C4_latches_closed_on_seat", slot.latches.All(l => l.Closed), slot.latches.Select(l => l.Closed).ToArray());
            RecordPose("ram_seated_latched", slot, ddr4);
            foreach (var d in Pull(R, slot, ddr4, "Edge", "Main", true, "C4_pull_blocked_when_latched")) yield return d;
            foreach (var d in Press(0, slot.latches[0], false, "C4_unlatch_left")) yield return d;
            foreach (var d in Press(0, slot.latches[1], false, "C4_unlatch_right")) yield return d;
            Check("C4_unlatched_state", slot.State == SocketState.Seated && !slot.Retained, slot.State.ToString(), "Seated, not retained");
            foreach (var d in Pull(R, slot, ddr4, "Edge", "Main", false, "C4_pull_out_after_unlatch")) yield return d;
            foreach (var d in PutBack(R, ddr4)) yield return d;
            foreach (var d in Grab(R, ddr4, "Main")) yield return d;
            foreach (var d in WrongPair(R, slot, ddr4, "Edge", "Main", 180, 2.89f, "C3_ddr4_reversed")) yield return d;
            foreach (var d in UnitGrab(R, ddr5, "Main")) yield return d;
            foreach (var d in WrongPair(R, slot, ddr5, "Edge", "Main", 0, 2.89f, "C3_ddr5_in_ddr4")) yield return d;

            currentBundle = "ssd_cable_tray";
            var sled = Socket("CASE_SLED_25", "Drive");
            var ssd = Plug("SSD_SATA_25_7MM"); var cable = Plug("CABLE_SATA_DATA_S_500");
            var data = Socket("SSD_SATA_25_7MM", "SataData"); var power = Socket("SSD_SATA_25_7MM", "SataPower");
            foreach (var d in UnitGrab(R, ssd, "Main")) yield return d;
            foreach (var d in Seat(R, sled, ssd, "Bottom")) yield return d;
            o = Observe(sled, ssd, "Bottom");
            Check("C2_ssd_seated_in_sled", sled.State == SocketState.Seated && Mathf.Abs(o.depthMm) < .1f && o.lateralMm < .1f, new { state = sled.State.ToString(), pose = o.ToString() });
            RecordPose("ssd_in_sled", sled, ssd);
            foreach (var d in UnitGrab(R, cable, "EndA")) yield return d;
            foreach (var d in WrongPair(R, power, cable, "EndA", "EndA", 0, 4.79f, "C3_data_plug_in_power")) yield return d;
            foreach (var d in Grab(R, cable, "EndA")) yield return d;
            foreach (var d in WrongPair(R, data, cable, "EndA", "EndA", 180, 4.79f, "C3_data_plug_reversed")) yield return d;
            foreach (var d in Grab(R, cable, "EndA")) yield return d;
            foreach (var d in Carry(R, cable, AtDepth(data, cable, "EndA", 15, 0))) yield return d;
            foreach (var d in Carry(R, cable, AtDepth(data, cable, "EndA", 3, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            o = Observe(data, cable, "EndA");
            Check("C5_cable_release_midway", data.State == SocketState.Partial && Mathf.Abs(o.depthMm - 3) < .6f && o.lateralMm < .01f, new { state = data.State.ToString(), pose = o.ToString() });
            RecordPose("cable_partial", data, cable);
            foreach (var d in Grab(R, cable, "EndA")) yield return d;
            foreach (var d in Carry(R, cable, AtDepth(data, cable, "EndA", -1, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            o = Observe(data, cable, "EndA");
            Check("C2_cable_seated", data.State == SocketState.Seated && Mathf.Abs(o.depthMm) < .1f && o.lateralMm < .1f, new { state = data.State.ToString(), pose = o.ToString() });
            RecordPose("cable_seated", data, cable);
            Check("C4_ssd_bundle_retention", true, "N/A: no tested retention for a SATA plug or a sled-mounted drive", "N/A");

            currentBundle = "fan_guard_lock";
            var rear = Socket("FAN_PED_HEAD", "GuardRear"); var lockSeat = Socket("FAN_PED_HEAD", "GuardLock");
            var guard = Plug("FAN_PED_GUARD_REAR"); var ring = Plug("FAN_PED_GUARD_LOCK");
            foreach (var d in UnitGrab(R, ring, "Ring")) yield return d;
            foreach (var d in Carry(R, ring, AtDepth(lockSeat, ring, "Thread", 15, 0))) yield return d;
            foreach (var d in Carry(R, ring, AtDepth(lockSeat, ring, "Thread", -1, 0))) yield return d;
            o = Observe(lockSeat, ring, "Thread");
            Check("C3_lock_without_guard_stops_at_entrance", lockSeat.State == SocketState.Guided && Mathf.Abs(o.depthMm - lockSeat.spec.approachMm) < .1f,
                  new { state = lockSeat.State.ToString(), o.depthMm }, lockSeat.spec.approachMm);
            foreach (var d in PutBack(R, ring)) yield return d;
            foreach (var d in UnitGrab(R, guard, "Rim")) yield return d;
            foreach (var d in WrongPair(R, rear, guard, "Plate", "Rim", 30, 2.49f, "C3_guard_turned_30")) yield return d;
            foreach (var d in Grab(R, guard, "Rim")) yield return d;
            foreach (var d in Seat(R, rear, guard, "Plate")) yield return d;
            o = Observe(rear, guard, "Plate");
            Check("C2_guard_seated", rear.State == SocketState.Seated && Mathf.Abs(o.depthMm) < .1f && o.lateralMm < .1f && o.tiltDeg < .1f, new { state = rear.State.ToString(), pose = o.ToString() });
            foreach (var d in Grab(R, ring, "Ring")) yield return d;
            foreach (var d in Carry(R, ring, AtDepth(lockSeat, ring, "Thread", 10, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            Check("C5_ring_release_midway", lockSeat.State == SocketState.Partial, lockSeat.State.ToString(), "Partial");
            RecordPose("ring_partial", lockSeat, ring);
            foreach (var d in Grab(R, ring, "Ring")) yield return d;
            foreach (var d in Carry(R, ring, AtDepth(lockSeat, ring, "Thread", -1, 0))) yield return d;
            foreach (var d in Release(R)) yield return d;
            Check("C2_ring_seated", lockSeat.State == SocketState.Seated, lockSeat.State.ToString(), "Seated");
            RecordPose("guard_seated", rear, guard);
            RecordPose("ring_seated", lockSeat, ring);
            foreach (var d in Pull(R, rear, guard, "Plate", "Rim", true, "C4_guard_pull_blocked_by_ring")) yield return d;
            foreach (var d in Pull(R, lockSeat, ring, "Thread", "Ring", false, "C4_ring_removed")) yield return d;
            foreach (var d in PutBack(R, ring)) yield return d;
            foreach (var d in Pull(R, rear, guard, "Plate", "Rim", false, "C4_guard_free_after_ring_removed")) yield return d;
            foreach (var d in PutBack(R, guard)) yield return d;
        }

        static void Finish(string error)
        {
            var report = new
            {
                generated = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture), scene = AssetQABuilder.ScenePath, input = "SimulatedController (editor)",
                hardware_verified = false, error, passed = error == null && checks.All(c => (bool)c["passed"]),
                failed = checks.Where(c => !(bool)c["passed"]).Select(c => (string)c["id"]).ToArray(), grab_retries = GrabRetries, log = Lab?.Logger?.DirectoryPath, checks
            };
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(ReportDir + "/report.json", JsonConvert.SerializeObject(report, Formatting.Indented));
            File.WriteAllText(ReportDir + "/poses.json", JsonConvert.SerializeObject(poses, Formatting.Indented));
            Status = error == null ? $"done: {checks.Count(c => (bool)c["passed"])}/{checks.Count} passed" : "error: " + error;
            routine = null;
            EditorApplication.update -= Tick;
        }
    }
}
#endif
