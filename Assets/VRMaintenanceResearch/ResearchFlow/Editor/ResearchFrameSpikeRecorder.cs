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
    }
}
#endif
