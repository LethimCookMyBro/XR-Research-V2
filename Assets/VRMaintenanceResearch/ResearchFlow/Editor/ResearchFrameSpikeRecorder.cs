#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Per-frame main-thread time plus the player-loop markers that usually explain a spike, kept in ring
    /// buffers without a full profiler capture. Begin() during Play, Dump() after: frames over the threshold with each marker.</summary>
    public static class ResearchFrameSpikeRecorder
    {
        static readonly string[] Markers = { "Main Thread", "FixedUpdate.PhysicsFixedUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "PostLateUpdate.PlayerUpdateCanvases",
            "PostLateUpdate.FinishFrameRendering", "GC.Collect", "Physics.Processing", "Physics.Simulate", "EditorLoop", "Canvas.SendWillRenderCanvases" };
        static readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        const int Capacity = 40000;

        public static string Begin()
        {
            Stop();
            foreach (string marker in Markers)
                recorders.Add(ProfilerRecorder.StartNew(marker == "Main Thread" ? ProfilerCategory.Internal : ProfilerCategory.Scripts, marker, Capacity));
            return "recording " + string.Join(", ", Markers.Where((m, i) => recorders[i].Valid));
        }

        public static string Dump(string path, double thresholdMs = 150)
        {
            var rows = new List<string> { "frame_from_end," + string.Join(",", Markers.Select(m => m.Replace(',', ';'))) };
            var main = recorders[0]; int count = main.Count, spikes = 0;
            double worst = 0;
            for (int i = 0; i < count; i++)
            {
                double ms = main.GetSample(i).Value / 1e6; worst = System.Math.Max(worst, ms);
                if (ms < thresholdMs) continue;
                spikes++;
                rows.Add((count - i) + "," + string.Join(",", recorders.Select(r => r.Valid && i < r.Count ? (r.GetSample(i).Value / 1e6).ToString("F1") : "")));
            }
            File.WriteAllLines(path, rows);
            string summary = "frames=" + count + " spikes>" + thresholdMs + "ms=" + spikes + " worst=" + worst.ToString("F1") + "ms";
            Stop();
            return summary;
        }

        static void Stop() { foreach (var r in recorders) r.Dispose(); recorders.Clear(); }

        // Profiler capture that freezes on the first slow frame of an active task, so the offending script sample can be read
        // afterwards. Armed through EditorPrefs before Play (survives the domain reload), so nothing touches the Editor mid-run.
        const string TrapKey = "ResearchFlow.SpikeTrapMs", TrappedKey = "ResearchFlow.SpikeTrapFrame";
        public static string Trap(double thresholdMs = 200)
        {
            UnityEditor.EditorPrefs.SetFloat(TrapKey, (float)thresholdMs); UnityEditor.EditorPrefs.SetInt(TrappedKey, -1);
            Install(); return "trap armed >" + thresholdMs + "ms for the next active task";
        }
        static int editorOnlySpikes;
        public static string TrapStatus() => (UnityEditor.EditorPrefs.GetInt(TrappedKey, -1) < 0
            ? (UnityEditor.EditorPrefs.HasKey(TrapKey) ? "armed" : "off") : "trapped frame " + UnityEditor.EditorPrefs.GetInt(TrappedKey))
            + "; skipped Editor-only spikes " + editorOnlySpikes;
        [UnityEditor.InitializeOnLoadMethod]
        static void Install() { UnityEditor.EditorApplication.update -= Watch; if (UnityEditor.EditorPrefs.HasKey(TrapKey)) UnityEditor.EditorApplication.update += Watch; }
        static void Watch()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return;
            var controller = Object.FindFirstObjectByType<ResearchSessionFlowController>();
            bool active = controller && controller.Flow != null && controller.Flow.IsTaskActive && !controller.Flow.Paused;
            if (!UnityEditorInternal.ProfilerDriver.enabled) { if (active) { UnityEditorInternal.ProfilerDriver.ClearAllFrames(); UnityEditorInternal.ProfilerDriver.enabled = true; } return; }
            float threshold = UnityEditor.EditorPrefs.GetFloat(TrapKey);
            if (!active || Time.unscaledDeltaTime * 1000 < threshold) return;
            UnityEditorInternal.ProfilerDriver.enabled = false;
            int frame = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
            // An EditorLoop-dominated frame (focus change, bridge poll, repaint) cannot happen on Quest; keep waiting for app code.
            if (PlayerLoopMs(frame) < threshold * .5f) { editorOnlySpikes++; UnityEditorInternal.ProfilerDriver.enabled = true; return; }
            UnityEditor.EditorPrefs.SetInt(TrappedKey, frame);
            UnityEditor.EditorPrefs.DeleteKey(TrapKey); UnityEditor.EditorApplication.update -= Watch;
        }

        static float PlayerLoopMs(int frame)
        {
            using (var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(frame, 0,
                UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.Default, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) return float.MaxValue;
                var children = new List<int>(); view.GetItemChildren(view.GetRootItemID(), children);
                foreach (int id in children)
                    if (view.GetItemName(id) == "PlayerLoop") return view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
                return float.MaxValue;
            }
        }
    }
}
#endif
