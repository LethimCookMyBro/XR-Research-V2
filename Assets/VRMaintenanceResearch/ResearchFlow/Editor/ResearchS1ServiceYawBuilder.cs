#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static partial class ResearchS1TaskABuilder
    {
        public static ResearchS1ServiceYaw AuthorRemaining(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task) throw new ArgumentException("Author the service handle on an explicit Task A outside Play Mode.");
            if (task.serviceYaw)
            {
                if (!task.AuthoredBindingsValid()) throw new InvalidOperationException("Existing service handle bindings are inconsistent.");
                return task.serviceYaw;
            }
            if (!task.servicePivot || task.servicePivot.Find("S1_ServiceBaseHandle") || task.inputs == null || task.inputs.Length != 24 ||
                !task.wiring || !task.wiring.AuthoredBindingsValid()) throw new InvalidOperationException("Expected the intact 24-input Task A wiring graph.");
            var original = task.inputs;
            var inputs = original.ToList();
            try
            {
                task.serviceYaw = ServiceYaw(task, inputs);
                task.inputs = inputs.ToArray();
                if (!task.AuthoredBindingsValid()) throw new InvalidOperationException("New service handle bindings are inconsistent.");
                UnityEditor.EditorUtility.SetDirty(task);
                return task.serviceYaw;
            }
            catch
            {
                if (task.serviceYaw) UnityEngine.Object.DestroyImmediate(task.serviceYaw.gameObject);
                task.serviceYaw = null;
                task.inputs = original;
                throw;
            }
        }

        private static ResearchS1ServiceYaw ServiceYaw(ResearchS1TaskAMechanics task, List<ResearchS1TaskAInput> inputs)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                handle.name = "S1_ServiceBaseHandle";
                handle.transform.SetParent(task.servicePivot, false);
                var bounds = new Bounds();
                bool first = true;
                foreach (var collider in task.fixedAssembly[0].GetComponentsInChildren<BoxCollider>())
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                var corner = collider.transform.TransformPoint(collider.center + Vector3.Scale(collider.size * .5f, new Vector3(x, y, z)));
                                var point = task.servicePivot.InverseTransformPoint(corner);
                                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                                else bounds.Encapsulate(point);
                            }
                handle.transform.localPosition = new Vector3(bounds.center.x, bounds.min.y + .025f, bounds.max.z + .035f);
                handle.transform.localScale = new Vector3(.12f, .025f, .025f);
                handle.GetComponent<Renderer>().sharedMaterial = task.fixedAssembly[0].GetComponentInChildren<Renderer>().sharedMaterial;
                var body = handle.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                var grab = handle.AddComponent<Grabbable>(); grab.MaxGrabPoints = 1;
                grab.InjectOptionalRigidbody(body); grab.InjectOptionalKinematicWhileSelected(true); grab.InjectOptionalThrowWhenUnselected(false);
                var yaw = handle.AddComponent<ResearchS1ServiceYaw>(); yaw.owner = task; yaw.grabbable = grab;
                grab.InjectOptionalOneGrabTransformer(yaw);
                var hand = Hand(handle.transform, body, grab, GrabTypeFlags.All);
                var input = handle.AddComponent<ResearchS1TaskAInput>(); input.owner = task; input.stableId = "A.service_handle";
                input.pointable = grab; input.interactable = hand; input.customMotion = true; yaw.input = input; inputs.Add(input);
                var item = handle.AddComponent<HandsLabObject>(); item.stableId = input.stableId; item.body = body; item.grabbable = grab;
                return yaw;
            }
            catch { UnityEngine.Object.DestroyImmediate(handle); throw; }
        }
    }
}
#endif
