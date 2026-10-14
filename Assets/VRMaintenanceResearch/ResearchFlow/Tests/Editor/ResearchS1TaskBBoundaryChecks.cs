#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskBBoundaryChecks
    {
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Task B boundary checks require Edit Mode.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var parent = new GameObject("TaskBBoundaryFixture");
                SceneManager.MoveGameObjectToScene(parent, scene);
                var port = parent.AddComponent<ResearchTaskPort>();
                port.taskId = "B";
                var controller = parent.AddComponent<ResearchSessionFlowController>();
                var task = ResearchS1TaskBBuilder.Build(parent.transform, port, controller);
                string initial = ResearchS1TaskBBuilder.CheckAuthored(task);
                int checks = 0;
                var failures = new List<string>();
                Action<bool, string> check = (condition, message) => { checks++; if (!condition) failures.Add(message); };
                Action<string, Action, Action> reject = (name, corrupt, restore) =>
                {
                    try
                    {
                        corrupt();
                        var state = task.Capture();
                        var category = ResearchS1TaskEvaluator.Evaluate(state).Category;
                        check(!state.systemIntegrityValid, name + " expected systemIntegrityValid=false actual=" + state.systemIntegrityValid);
                        check(category == S1InspectionCategory.SystemError, name + " expected SystemError actual=" + category);
                        bool rejected = false;
                        try { task.Bind(); } catch (InvalidOperationException) { rejected = true; }
                        check(rejected, name + " Bind accepted malformed bindings");
                        rejected = false;
                        try { ResearchS1TaskBBuilder.CheckAuthored(task); } catch (InvalidOperationException) { rejected = true; }
                        check(rejected, name + " CheckAuthored accepted malformed bindings");
                    }
                    finally { restore(); }
                };
                var assembly = task.fixedAssembly;
                var positions = task.fixedPositions;
                var rotations = task.fixedRotations;
                reject("null fixedAssembly", () => task.fixedAssembly = null, () => task.fixedAssembly = assembly);
                reject("short fixedAssembly", () => task.fixedAssembly = new[] { assembly[0] }, () => task.fixedAssembly = assembly);
                reject("null fixedPositions", () => task.fixedPositions = null, () => task.fixedPositions = positions);
                reject("short fixedPositions", () => task.fixedPositions = new Vector3[1], () => task.fixedPositions = positions);
                reject("null fixedRotations", () => task.fixedRotations = null, () => task.fixedRotations = rotations);
                reject("short fixedRotations", () => task.fixedRotations = new Quaternion[1], () => task.fixedRotations = rotations);
                var basis = assembly[0];
                reject("null fixed reference", () => assembly[0] = null, () => assembly[0] = basis);
                reject("wrong fixed reference", () => assembly[0] = assembly[1], () => assembly[0] = basis);
                var position = positions[0];
                reject("nonfinite authored position", () => positions[0] = new Vector3(float.NaN, 0, 0), () => positions[0] = position);
                var rotation = rotations[0];
                reject("invalid authored rotation", () => rotations[0] = new Quaternion(0, 0, 0, 0), () => rotations[0] = rotation);
                string id = task.rearLock.stableId;
                reject("wrong stable ID", () => task.rearLock.stableId = "B.spinner", () => task.rearLock.stableId = id);
                var seat = task.rearLock.seat;
                reject("wrong seat binding", () => task.rearLock.seat = task.spinner.seat, () => task.rearLock.seat = seat);
                var body = task.spinner.body;
                reject("foreign body binding", () => task.spinner.body = task.rearLock.body, () => task.spinner.body = body);
                var owner = task.blade.owner;
                reject("missing part owner", () => task.blade.owner = null, () => task.blade.owner = owner);
                string taskId = port.taskId;
                reject("wrong port ID", () => port.taskId = "A", () => port.taskId = taskId);
                var shaft = task.motorShaft;
                reject("wrong shaft reference", () => task.motorShaft = task.rearLock.mate, () => task.motorShaft = shaft);
                var originalPosition = basis.localPosition;
                try
                {
                    basis.localPosition += Vector3.right * .02f;
                    var moved = task.Capture();
                    check(moved.systemIntegrityValid, "user base displacement became an authoring error");
                    check(!moved.baseStable, "user base displacement remained stable");
                    check(ResearchS1TaskEvaluator.Evaluate(moved).Category == S1InspectionCategory.PreconditionNotReady,
                        "user base displacement expected PreconditionNotReady");
                    task.Bind();
                }
                finally { basis.localPosition = originalPosition; }
                check(ResearchS1TaskBBuilder.Build(parent.transform, port, controller) == task, "repeat Build replaced the existing fixture");
                ResearchS1TaskBBuilder.CheckAuthored(task);
                if (failures.Count > 0) throw new InvalidOperationException("S1_TASK_B_BOUNDARY_FAIL checks=" + checks + "\n" + string.Join("\n", failures));
                return initial + "\nS1_TASK_B_BOUNDARY_PASS checks=" + checks + "; scope=disposable_editor_binding_checks; input_e2e=false";
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif
