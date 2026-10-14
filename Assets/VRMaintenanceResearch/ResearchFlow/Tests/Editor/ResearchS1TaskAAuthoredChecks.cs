#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskAAuthoredChecks
    {
        public static string Run(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task) throw new ArgumentException("Use an explicit authored fixture in Edit Mode.");
            int checks = 0;
            Action<bool, string> check = (value, message) => { checks++; if (!value) throw new InvalidOperationException(message); };
            check(task.AuthoredBindingsValid(), "Authored graph invalid.");
            var initial = task.Capture();
            check(ResearchS1TaskEvaluator.Evaluate(initial).Category == S1InspectionCategory.SystemError, "Incomplete modules cannot be participant failure.");
            check(initial.ram.Length == 5 && initial.ssds.Length == 3 && initial.gpus.Length == 2, "Wrong option count.");
            check(initial.ram.All(p => p.placement == S1Placement.NotInstalled), "RAM installed during authoring.");
            check(initial.ssds.All(p => p.placement == S1Placement.NotInstalled && p.trayFastener == S1ThreadState.Absent), "SSD auto fastened.");
            check(initial.gpus.All(p => p.placement == S1Placement.NotInstalled && p.bracketFastener == S1ThreadState.Absent), "GPU auto fastened.");
            check(initial.cables.Length == 3 && initial.cables.All(c => c.sourceConnected && !c.targetConnected), "Cable device ends must start loose with measured source continuity.");
            check(initial.baseState.atx24Connected && initial.baseState.epsConnected && initial.baseState.cpuFanConnected && initial.baseState.frontPanelConnected && initial.baseState.powerIsolated, "Preassembled physical base wiring not ready.");
            check(!task.port.contentApproved, "Authoring cannot approve study content.");
            foreach (var label in task.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            {
                label.ForceMeshUpdate();
                check(label.textBounds.size.x <= .1f && label.textBounds.size.y <= .03f, "Option label exceeds the smallest physical part: " + label.name);
            }
            foreach (var socket in task.ramSlots)
            {
                check(socket.latches.Count == 1, "Real Q-DIMM is single-latch.");
                var ddr4 = SocketRules.Match(socket.rules, "RAM_DDR4_UDIMM_BARE", "Edge", 0, 1);
                var ddr5 = SocketRules.Match(socket.rules, "RAM_DDR5_UDIMM_BARE", "Edge", 0, 1);
                var reverse = SocketRules.Match(socket.rules, "RAM_DDR4_UDIMM_BARE", "Edge", 180, 1);
                check(ddr4 != null && ddr4.outcome == PairOutcome.Seat, "Compatible RAM rejected from " + socket.seatName);
                check(ddr5 != null && ddr5.outcome == PairOutcome.Block && ddr5.reachMm == 2.89f, "Wrong family not physically stopped.");
                check(reverse != null && reverse.outcome == PairOutcome.Block, "Reversed RAM auto corrected.");
            }
            foreach (var socket in task.gpuSlots)
            {
                var rule = SocketRules.Match(socket.rules, "GPU_GXB_8PIN", "Edge", 0, 1);
                check(rule != null && rule.outcome == PairOutcome.Seat, "Compatible GPU rejected from " + socket.seatName);
            }
            foreach (var ssd in task.ssds)
            {
                check(ssd.GetComponentsInChildren<Oculus.Interaction.Grabbable>(true).Length == 2, "SSD+sledge has one body grab and one independent thumb only.");
                check(ssd.transform.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SSD_SATA_25_7MM"), "SSD missing from sledge.");
                check(ssd.transform.GetComponentsInChildren<Transform>(true).Any(t => t.name == "MOV_CASE_SLED_25_Thumb"), "Separate tray fastener geometry lost.");
            }
            check(task.sleds.Length == 3 && task.gpuScrews.Length == 2, "Missing physical fasteners.");
            foreach (var sled in task.sleds)
            {
                check(sled.thumb.ProgressDegrees == 0 && sled.thumb.State == S1ThreadState.Absent, "Thumb starts backed off.");
                check(sled.thumb.travelMm == 2.5f && !sled.Hooked && sled.Tray == -1, "Wrong sled initial state.");
            }
            check(task.gpuScrews.All(p => p.State == S1ThreadState.Absent && p.SocketIndex == -1 && p.travelMm == 5.1f), "GPU screws cannot be pre-fastened.");
            var parts = task.ram.Concat(task.ssds).Concat(task.gpus).ToArray();
            check(parts.Select(p => p.stableId).Distinct().Count() == 10, "Repeated stable IDs.");
            var originalIds = parts.Select(p => p.stableId).ToArray();
            try
            {
                parts[0].stableId = "M00";
                check(!task.AuthoredBindingsValid() && !task.Capture().systemIntegrityValid, "Bad ID did not fail closed.");
            }
            finally { for (int i = 0; i < parts.Length; i++) parts[i].stableId = originalIds[i]; }
            var slots = task.ramSlots;
            try
            {
                task.ramSlots = new QASocket[0];
                check(!task.AuthoredBindingsValid() && !task.Capture().systemIntegrityValid, "Missing slots did not fail closed.");
            }
            finally { task.ramSlots = slots; }
            var latch = task.ramSlots[0].latches[0];
            try
            {
                task.ramSlots[0].latches[0] = null;
                check(!task.AuthoredBindingsValid(), "Null latch accepted.");
                task.ramSlots[0].latches[0] = task.ramSlots[1].latches[0];
                check(!task.AuthoredBindingsValid(), "Swapped latch accepted.");
            }
            finally { task.ramSlots[0].latches[0] = latch; }
            var input = task.inputs[0];
            try
            {
                task.inputs[0] = task.inputs[1];
                check(!task.AuthoredBindingsValid(), "Duplicate input accepted.");
            }
            finally { task.inputs[0] = input; }
            var pointable = task.inputs[0].pointable;
            try
            {
                task.inputs[0].pointable = task.inputs[1].pointable;
                check(!task.AuthoredBindingsValid(), "Wrong latch pointable accepted.");
            }
            finally { task.inputs[0].pointable = pointable; }
            var copiedClip = UnityEngine.Object.Instantiate(latch.closeClip);
            try
            {
                var rotation = latch.transform.localRotation;
                check(ResearchS1TaskABuilder.LatchClipMatches(copiedClip, rotation), "Exact authored clip rejected.");
                var extra = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");
                AnimationUtility.SetEditorCurve(copiedClip, extra, AnimationCurve.Constant(0, 1, 1));
                check(!ResearchS1TaskABuilder.LatchClipMatches(copiedClip, rotation), "Extra position binding accepted.");
                AnimationUtility.SetEditorCurve(copiedClip, extra, null);
                copiedClip.legacy = false;
                check(!ResearchS1TaskABuilder.LatchClipMatches(copiedClip, rotation), "Non-legacy clip accepted.");
                copiedClip.legacy = true;
                AnimationUtility.SetEditorCurve(copiedClip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.x"),
                    AnimationCurve.Linear(0, rotation.x, 2, rotation.x + 1));
                check(!ResearchS1TaskABuilder.LatchClipMatches(copiedClip, rotation), "Wrong sampled endpoint accepted.");
            }
            finally { UnityEngine.Object.DestroyImmediate(copiedClip); }
            var layout = task.layout;
            try
            {
                task.layout = "R";
                check(!task.AuthoredBindingsValid(), "Unknown layout accepted.");
            }
            finally { task.layout = layout; }
            var pivot = task.servicePivot;
            try
            {
                task.servicePivot = null;
                check(!task.AuthoredBindingsValid() && !task.Capture().systemIntegrityValid, "Missing pivot did not fail closed.");
            }
            finally { task.servicePivot = pivot; }
            check(task.AuthoredBindingsValid(), "Regression check failed to restore fixture.");
            return "S1_TASK_A_AUTHORED_CHECKS_PASS checks=" + checks;
        }
    }
}
#endif
