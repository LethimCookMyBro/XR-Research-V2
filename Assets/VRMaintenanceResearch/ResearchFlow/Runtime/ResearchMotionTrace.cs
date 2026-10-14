#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Unity.Profiling;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Bounded technical motion trace for the next wear session: per-frame samples in a preallocated array,
    // one file write after the window. Observes only; never smooths or moves tracking, hands, rays or panels.
    [DefaultExecutionOrder(32000)] // after Meta hand visuals, ray interactors and panel/drag LateUpdates
    public sealed class ResearchMotionTrace : MonoBehaviour
    {
        public const int MinSeconds = 5, MaxSeconds = 30, FramesPerSecondCap = 120;
        struct Sample
        {
            public int frame, gcCount;
            public double time;
            public float dt;
            public Vector3 head, rawLeft, rawRight, handLeft, handRight, rayLeft, rayRight, mainPanel, referencePanel, dragDesired;
            public Quaternion headRotation;
            // Callbacks fired since the previous frame: controller data, hand data, rendered hand skeleton.
            public byte controllerLeft, controllerRight, handDataLeft, handDataRight, visualLeft, visualRight, rejectedLeft, rejectedRight;
            public bool rawLeftValid, rawRightValid, dragging;
            public long gcAllocated, canvasNs, mainThreadNs;
        }

        ResearchSessionFlowController flow;
        Sample[] samples;
        int count;
        double endTime;
        readonly int[] controllerCalls = new int[2], handCalls = new int[2], visualCalls = new int[2], lastUnresolved = new int[2];
        Controller[] controllers;
        HandVisual[] visuals;
        RayInteractor[] rays;
        ResearchHandContactProxy[] proxies;
        ResearchDragHandle[] drags;
        Action[] unsubscribe = Array.Empty<Action>();
        ProfilerRecorder gcRecorder, canvasRecorder, mainRecorder;
        bool[] profilerValid = new bool[3];
        public string State { get; private set; } = "idle";
        public string Path { get; private set; }
        public object Status => new { state = State, frames = count, path = Path, hardware_verified = false };

        public static int Clamp(int seconds) => Mathf.Clamp(seconds, MinSeconds, MaxSeconds);
        static int Side(Handedness h) => h == Handedness.Left ? 0 : 1;
        static int Side(Component c) => c.GetComponentsInParent<Transform>(true).Any(t => t.name.EndsWith("Left", StringComparison.Ordinal)) ? 0 : 1;
        static int Side(ResearchHandContactProxy p) => p.handVisual && p.handVisual.Hand != null ? Side(p.handVisual.Hand.Handedness) : Side((Component)p);

        public bool Begin(ResearchSessionFlowController owner, int seconds, out string error)
        {
            error = null;
            if (State == "capturing") { error = "A motion trace is already capturing."; return false; }
            if (!owner || !owner.origin) { error = "Research rig unavailable."; return false; }
            flow = owner;
            var origin = owner.origin.transform;
            controllers = origin.GetComponentsInChildren<Controller>(true);
            visuals = origin.GetComponentsInChildren<HandVisual>(true);
            rays = origin.GetComponentsInChildren<RayInteractor>(true);
            proxies = origin.GetComponentsInChildren<ResearchHandContactProxy>(true);
            drags = owner.view ? owner.view.GetComponentsInChildren<ResearchDragHandle>(true) : Array.Empty<ResearchDragHandle>();
            samples = new Sample[Clamp(seconds) * FramesPerSecondCap];
            count = 0;
            Array.Clear(controllerCalls, 0, 2); Array.Clear(handCalls, 0, 2); Array.Clear(visualCalls, 0, 2);
            foreach (var p in proxies) lastUnresolved[Side(p)] = p.UnresolvedContacts;
            var undo = new System.Collections.Generic.List<Action>();
            foreach (var c in controllers) { int s = Side(c.Handedness); Action a = () => controllerCalls[s]++; c.WhenUpdated += a; undo.Add(() => c.WhenUpdated -= a); }
            foreach (var v in visuals)
            {
                if (v.Hand == null) continue;
                int s = Side(v.Hand.Handedness); var hand = v.Hand;
                Action data = () => handCalls[s]++, render = () => visualCalls[s]++;
                hand.WhenHandUpdated += data; v.WhenHandVisualUpdated += render;
                undo.Add(() => { hand.WhenHandUpdated -= data; v.WhenHandVisualUpdated -= render; });
            }
            unsubscribe = undo.ToArray();
            gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            canvasRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases");
            mainRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            profilerValid = new[] { gcRecorder.Valid, canvasRecorder.Valid, mainRecorder.Valid };
            endTime = Time.realtimeSinceStartupAsDouble + Clamp(seconds);
            Path = null; State = "capturing"; enabled = true;
            return true;
        }

        void LateUpdate()
        {
            if (State != "capturing") return;
            if (count == samples.Length || Time.realtimeSinceStartupAsDouble >= endTime) { Finish(); return; }
            var head = flow.origin.Camera.transform;
            ref var s = ref samples[count++];
            s.frame = Time.frameCount; s.time = Time.realtimeSinceStartupAsDouble; s.dt = Time.unscaledDeltaTime;
            s.head = head.position; s.headRotation = head.rotation;
            foreach (var c in controllers)
                if (c.isActiveAndEnabled && c.TryGetPose(out Pose pose))
                { if (Side(c.Handedness) == 0) { s.rawLeft = pose.position; s.rawLeftValid = true; } else { s.rawRight = pose.position; s.rawRightValid = true; } }
            foreach (var v in visuals)
                if (v.isActiveAndEnabled && v.Hand != null && v.Root) { if (Side(v.Hand.Handedness) == 0) s.handLeft = v.Root.position; else s.handRight = v.Root.position; }
            foreach (var r in rays)
                if (r.isActiveAndEnabled) { if (Side(r) == 0) s.rayLeft = r.End; else s.rayRight = r.End; }
            foreach (var p in proxies)
            {
                int side = Side(p), rejected = p.UnresolvedContacts - lastUnresolved[side];
                lastUnresolved[side] = p.UnresolvedContacts;
                if (side == 0) s.rejectedLeft = (byte)Mathf.Min(255, s.rejectedLeft + rejected); else s.rejectedRight = (byte)Mathf.Min(255, s.rejectedRight + rejected);
            }
            if (flow.view) { s.mainPanel = flow.view.mainPanel.transform.position; s.referencePanel = flow.view.referencePanel.transform.position; }
            foreach (var d in drags) if (d.IsDragging) { s.dragging = true; s.dragDesired = d.DesiredPosition; }
            s.controllerLeft = (byte)Mathf.Min(255, controllerCalls[0]); s.controllerRight = (byte)Mathf.Min(255, controllerCalls[1]);
            s.handDataLeft = (byte)Mathf.Min(255, handCalls[0]); s.handDataRight = (byte)Mathf.Min(255, handCalls[1]);
            s.visualLeft = (byte)Mathf.Min(255, visualCalls[0]); s.visualRight = (byte)Mathf.Min(255, visualCalls[1]);
            Array.Clear(controllerCalls, 0, 2); Array.Clear(handCalls, 0, 2); Array.Clear(visualCalls, 0, 2);
            s.gcAllocated = gcRecorder.Valid ? gcRecorder.LastValue : -1;
            s.canvasNs = canvasRecorder.Valid ? canvasRecorder.LastValue : -1;
            s.mainThreadNs = mainRecorder.Valid ? mainRecorder.LastValue : -1;
            s.gcCount = GC.CollectionCount(0);
        }

        // One write after the window; nothing is serialized per frame.
        void Finish()
        {
            Stop();
            try
            {
                string directory = System.IO.Path.Combine(Application.persistentDataPath, "MotionTrace");
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_motion.jsonl");
                var text = new StringBuilder();
                var c = CultureInfo.InvariantCulture;
                string V(Vector3 v) => string.Format(c, "[{0:F4},{1:F4},{2:F4}]", v.x, v.y, v.z);
                text.Append("{\"header\":true,\"participant_id\":\"").Append(flow.Record?.participant_id).Append("\",\"flow_state\":\"").Append(flow.Flow?.State)
                    .Append("\",\"frames\":").Append(count).Append(",\"profiler_valid\":[").Append(string.Join(",", profilerValid.Select(v => v ? "1" : "0")))
                    .Append("],\"units\":\"metres, seconds, ns, bytes; callbacks counted since previous sampled frame\",\"hardware_verified\":false}\n");
                for (int i = 0; i < count; i++)
                {
                    var s = samples[i];
                    text.Append(string.Format(c, "{{\"f\":{0},\"t\":{1:F5},\"dt\":{2:F5},\"head\":{3},\"head_yaw\":{4:F2},\"head_pitch\":{5:F2}", s.frame, s.time, s.dt, V(s.head), s.headRotation.eulerAngles.y, s.headRotation.eulerAngles.x))
                        .Append(",\"raw\":[").Append(s.rawLeftValid ? V(s.rawLeft) : "null").Append(',').Append(s.rawRightValid ? V(s.rawRight) : "null").Append(']')
                        .Append(",\"hand\":[").Append(V(s.handLeft)).Append(',').Append(V(s.handRight)).Append(']')
                        .Append(",\"ray_end\":[").Append(V(s.rayLeft)).Append(',').Append(V(s.rayRight)).Append(']')
                        .Append(",\"panel\":[").Append(V(s.mainPanel)).Append(',').Append(V(s.referencePanel)).Append(']')
                        .Append(",\"drag\":").Append(s.dragging ? V(s.dragDesired) : "null")
                        .Append(string.Format(c, ",\"cb_controller\":[{0},{1}],\"cb_hand\":[{2},{3}],\"cb_visual\":[{4},{5}],\"contact_rejected\":[{6},{7}]",
                            s.controllerLeft, s.controllerRight, s.handDataLeft, s.handDataRight, s.visualLeft, s.visualRight, s.rejectedLeft, s.rejectedRight))
                        .Append(string.Format(c, ",\"gc_alloc\":{0},\"gc_count\":{1},\"canvas_ns\":{2},\"main_ns\":{3}}}\n", s.gcAllocated, s.gcCount, s.canvasNs, s.mainThreadNs));
                }
                File.WriteAllText(Path, text.ToString());
                State = "completed";
                Debug.Log("RESEARCH_MOTION_TRACE " + Path + " frames=" + count);
            }
            catch (Exception ex) { State = "write_failed"; Debug.LogError("RESEARCH_MOTION_TRACE_FAILED " + ex.Message); }
            samples = null;
        }

        void Stop()
        {
            foreach (var undo in unsubscribe) undo();
            unsubscribe = Array.Empty<Action>();
            gcRecorder.Dispose(); canvasRecorder.Dispose(); mainRecorder.Dispose();
        }

        void OnDisable() { if (State == "capturing") { Stop(); State = "aborted"; samples = null; } }
    }
}
#endif
