#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// Read-only evidence probe (5 Nov 2026): what a participant gets when trying to grab the rear guard / rear ring with the
    /// front parts still on, what the part chip says, and whether any "View condition" control exists on the Task B panel.
    /// Phase A presses the simulated controller on real colliders; phase B records what the participant would see while the
    /// existing ResearchS1TaskBSdkRunner removes the obstructions. Changes no mechanics, no Inspect rule and no logging.
    public static class ResearchS1RearAccessProbe
    {
        static ResearchS1TaskBMechanics task;
        static HandGrabInteractor hand;
        static string output;
        static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        static readonly List<object> rows = new List<object>();
        static string lastWatch;
        static int shot;
        public static string Status { get; private set; } = "NotStarted";
        const int Side = 1;

        public static string Begin(string evidenceDirectory)
        {
            if (routines.Count > 0) throw new InvalidOperationException("Probe already running.");
            task = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsSortMode.None).Single();
            var c = task.controller;
            if (!EditorApplication.isPlaying || c.Flow == null || !c.Flow.IsTaskActive || c.Flow.CurrentTask != "B" || !c.Record.test_data)
                throw new InvalidOperationException("Requires an active DEV Task B.");
            hand = UnityEngine.Object.FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None)
                .Single(h => h.isActiveAndEnabled && h.Hand != null && h.Hand.Handedness == Handedness.Right);
            output = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(output);
            rows.Clear(); lastWatch = null; shot = 0;
            routines.Push(PhaseA()); Status = "PhaseA";
            EditorApplication.update += Tick;
            return Status;
        }

        static void Tick()
        {
            try
            {
                if (routines.Count == 0) { EditorApplication.update -= Tick; Status = "PhaseADone"; Save(); return; }
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); return; }
                if (current.Current is IEnumerator nested) routines.Push(nested);
            }
            catch (Exception e) { EditorApplication.update -= Tick; routines.Clear(); Status = "Failed: " + e.Message; Row("failure", e.ToString()); Save(); }
        }

        static IEnumerator PhaseA()
        {
            Row("panel", PanelState());
            Row("initial", null);
            yield return Attempt(task.rearGuard, false, "rear guard rim, Grip");
            yield return Attempt(task.rearLock, true, "rear lock ring, Trigger");
            yield return Attempt(task.blade, false, "blade, Grip");
            yield return Attempt(task.frontGuard, false, "front guard, Grip (control)");
            Row("after-phase-a", null);
        }

        static IEnumerator Attempt(ResearchS1FanPart part, bool pinch, string label)
        {
            yield return Neutral();
            var cam = task.controller.origin.Camera.transform.position;
            var col = part.GetComponentsInChildren<Collider>().Where(x => x.enabled && !x.isTrigger && x.gameObject.activeInHierarchy && x.attachedRigidbody == part.body)
                .OrderBy(x => Vector3.Distance(x.bounds.center, cam)).First();
            var pose = ResearchFlowVisualInput.ControllerWorldPose(Side);
            var point = col.ClosestPoint(cam);
            pose.position += point - (pinch ? hand.PinchPoint.position : hand.PalmPoint.position);
            ResearchFlowVisualInput.SetControllerWorldPose(Side, pose);
            yield return Frames(20);
            Row("approach:" + label, new { collider = col.name, hover = Hover() });
            var seen = new HashSet<string>();
            for (int i = 0; i < 45; i++)
            {
                ResearchFlowVisualInput.SetControllerWorldPose(Side, pose, pinch ? 0 : 1, pinch ? 1 : 0);
                string snap = JsonConvert.SerializeObject(Snapshot(part));
                if (seen.Add(snap)) Row("pressed:" + label, Snapshot(part));
                yield return null;
            }
            // small pull and twist while pressed, to provoke the block reason the part reports on an attempt
            for (int i = 0; i < 20; i++)
            {
                var moved = new Pose(pose.position - part.seat.forward * (.0015f * i), Quaternion.AngleAxis(-2f * i, part.seat.forward) * pose.rotation);
                ResearchFlowVisualInput.SetControllerWorldPose(Side, moved, pinch ? 0 : 1, pinch ? 1 : 0);
                string snap = JsonConvert.SerializeObject(Snapshot(part));
                if (seen.Add(snap)) Row("pressed-move:" + label, Snapshot(part));
                if (i == 19) ScreenCapture.CaptureScreenshot(Path.Combine(output, "attempt_" + (++shot) + "_" + part.stableId + ".png"));
                yield return null;
            }
            yield return Frames(3);
            ResearchFlowVisualInput.SetControllerWorldPose(Side, pose);
            yield return Frames(20);
            Row("released:" + label, Snapshot(part));
        }

        static IEnumerator Neutral()
        {
            var guard = task.controller.guards.First(g => g && g.side == Handedness.Right);
            double until = EditorApplication.timeSinceStartup + 6;
            while (!guard.InteractionsAllowed || guard.WaitingForNeutral || hand.State == InteractorState.Select)
            {
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Input guard did not re-arm: " + guard.LastReason);
                ResearchFlowVisualInput.SetControllerWorldPose(Side, ResearchFlowVisualInput.ControllerWorldPose(Side));
                yield return null;
            }
        }

        static IEnumerator Frames(int n) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }

        // Phase B: call before ResearchS1TaskBSdkRunner.Begin; logs each distinct participant-visible chip text and the
        // rear parts' block reasons at the real intermediate states the runner reaches.
        public static string Watch(string evidenceDirectory)
        {
            task = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsSortMode.None).Single();
            output = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(output);
            rows.Clear(); lastWatch = null;
            EditorApplication.update -= WatchTick; EditorApplication.update += WatchTick; Status = "Watching";
            return Status;
        }
        public static string StopWatch() { EditorApplication.update -= WatchTick; Save(); Status = "WatchSaved"; return output; }

        static void WatchTick()
        {
            if (!EditorApplication.isPlaying || !task) { StopWatch(); return; }
            var s = task.Capture();
            var key = JsonConvert.SerializeObject(new
            {
                chip = ChipText(), front = task.frontGuard.Placement.ToString(), cap = task.spinner.ThreadState.ToString(), blade = task.blade.Placement.ToString(),
                ring = task.rearLock.ThreadState.ToString(), ring_place = task.rearLock.Placement.ToString(),
                block_rear_guard = task.ManipulationBlockReason(task.rearGuard).ToString(), block_ring = task.ManipulationBlockReason(task.rearLock).ToString()
            });
            if (key == lastWatch) return;
            lastWatch = key;
            rows.Add(new { frame = Time.frameCount, t = Time.realtimeSinceStartupAsDouble, runner_status = ResearchS1TaskBSdkRunner.Status, state = JsonConvert.DeserializeObject(key),
                rear_observation_th = ResearchS1FanObservations.Get(s, "B.rear_guard_mount", "Thai"), rear_observation_ja = ResearchS1FanObservations.Get(s, "B.rear_guard_mount", "Japanese") });
        }

        static object Snapshot(ResearchS1FanPart part)
        {
            var i = task.controller.CurrentInteraction;
            return new
            {
                hand = hand.State.ToString(), selected = hand.Interactable ? hand.Interactable.transform.parent.name : null, held = part.IsHeld,
                placement = part.Placement.ToString(), thread = part.ThreadState.ToString(), block = task.ManipulationBlockReason(part).ToString(),
                interaction = i.HasValue ? new { i.Value.Target, op = i.Value.Operation.ToString(), reason = i.Value.Reason.ToString() } : null,
                chip = ChipText(), hover = Hover()
            };
        }

        static object Hover() { var h = task.controller.CurrentHover; return h.HasValue ? h.Value.Target : null; }

        static string ChipText()
        {
            var view = task.controller.view;
            var m = view.GetType().GetMethod("PartInteractionText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (string)m.Invoke(view, null);
        }

        static object PanelState()
        {
            var c = task.controller;
            return new
            {
                training_version = c.Record.training_version, direct_part_feedback = c.UsesDirectPartFeedback,
                examine_targets_available = ResearchExamineTarget.AvailableOnMain(c).Select(t => t.stableTarget).ToArray(),
                panel_buttons = c.view.GetComponentsInChildren<ResearchUIAction>(false).Where(a => a.isActiveAndEnabled).Select(a => a.command).Distinct().ToArray(),
                whole_fan_text_th = ResearchS1FanObservations.Get(task.Capture(), "B.module", "Thai"),
                whole_fan_text_ja = ResearchS1FanObservations.Get(task.Capture(), "B.module", "Japanese")
            };
        }

        static void Row(string what, object data)
        {
            rows.Add(new { what, frame = Time.frameCount, data,
                parts = new[] { task.frontClip, task.frontGuard, task.spinner, task.blade, task.rearLock, task.rearGuard }
                    .Select(p => new { p.stableId, placement = p.Placement.ToString(), thread = p.ThreadState.ToString(), held = p.IsHeld, block = task.ManipulationBlockReason(p).ToString() }) });
        }

        static void Save() => File.WriteAllText(Path.Combine(output, Status.StartsWith("Watch") ? "phase_b_watch.json" : "phase_a_attempts.json"),
            JsonConvert.SerializeObject(rows, Formatting.Indented));
    }
}
#endif
