#if UNITY_EDITOR
using System;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1RemainingMechanicsChecks
    {
        public static string RunReviewRegressions()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run isolated socket checks outside Play Mode.");
            var root = new GameObject("S1_RemainingMechanics_ReviewCheck");
            int checks = 0;
            Action<bool, string> check = (value, message) => { checks++; if (!value) throw new InvalidOperationException(message); };
            try
            {
                root.transform.position = new Vector3(90, -50, 90);
                var pivot = new GameObject("ServicePivot").transform; pivot.SetParent(root.transform, false);
                var socketObject = new GameObject("Socket"); socketObject.transform.SetParent(pivot, false);
                var socket = socketObject.AddComponent<QASocket>(); socket.seat = socket.transform;
                if (!QASocket.All.Contains(socket)) socket.SendMessage("OnEnable");
                socket.rules.Add(new PairRule { plug = "fixture", mate = "Edge", outcome = PairOutcome.Seat });
                var plugObject = new GameObject("Plug"); plugObject.transform.SetParent(root.transform, false);
                var plug = plugObject.AddComponent<QAPlug>(); plug.assetId = "fixture";
                plug.SendMessage("Awake");
                plug.body = plugObject.AddComponent<Rigidbody>(); plug.body.isKinematic = true;
                plugObject.AddComponent<Oculus.Interaction.RigidbodyKinematicLocker>().SendMessage("Awake");
                var mate = new GameObject("MATE_fixture_Edge").transform; mate.SetParent(plug.transform, false); plug.mates.Add(mate);
                var partial = Matrix4x4.TRS(socket.transform.position - Vector3.forward * .0002f, Quaternion.identity, Vector3.one);
                var accepted = QASocket.Constrain(plug, partial, pose => true);
                plug.transform.SetPositionAndRotation(accepted.GetColumn(3), accepted.rotation);
                Action<string, object> held = (name, value) => typeof(QAPlug).GetField("<" + name + ">k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(plug, value);
                held("HeldSocket", socket); held("HeldMate", mate); held("HeldGuidance", socket.Last);
                plug.PoseAllowed = pose => Vector3.Distance(pose.GetColumn(3), accepted.GetColumn(3)) < .00001f;
                QASocket.Released(plug);
                check(socket.State == SocketState.Partial && socket.Last.depthMm > .1f, "Denied seat changed the partial state/depth.");
                check(Vector3.Distance(plug.transform.position, accepted.GetColumn(3)) < .00001f, "Denied seat snapped the accepted pose.");
                check(plug.transform.parent == socket.transform && plug.body.isKinematic, "Denied partial did not remain held under its socket.");
                var relative = socket.transform.worldToLocalMatrix * plug.transform.localToWorldMatrix;
                pivot.Rotate(Vector3.up, 90, Space.World);
                var rotated = socket.transform.worldToLocalMatrix * plug.transform.localToWorldMatrix;
                check(Vector3.Distance(relative.GetColumn(3), rotated.GetColumn(3)) < .00001f && Quaternion.Angle(relative.rotation, rotated.rotation) < .01f, "Partial pose did not follow service yaw.");
                check(plug.Socket == socket && socket.Occupant == plug, "Yaw lost partial socket ownership.");
                var placement = typeof(ResearchS1TaskAMechanics).GetMethod("Placement", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { plug });
                check((S1Placement)placement == S1Placement.Partial, "Snapshot placement lost the partial assembly.");
                var free = Matrix4x4.TRS(root.transform.position + Vector3.up, Quaternion.identity, Vector3.one);
                QASocket.Constrain(plug, free, pose => true);
                plug.transform.SetPositionAndRotation(free.GetColumn(3), free.rotation);
                QASocket proposed;
                var guided = QASocket.Constrain(plug, socket.seat.localToWorldMatrix, pose => false, out proposed);
                check(proposed == socket, "Denied guide lost its prospective socket.");
                check(!plug.Socket && !socket.Occupant && guided == free, "Denied guide committed ownership or pose.");
                var unbound = QASocket.Constrain(plug, free, pose => false, out proposed);
                check(!proposed && !plug.Socket && unbound == free, "Denied free movement fabricated a guided socket.");
                return "S1_REMAINING_REVIEW_CHECKS_PASS checks=" + checks + "; isolated_hierarchy=true; snapshot_placement=true; sdk_e2e=false; adapter_logging=offline_source_slice_only";
            }
            finally
            {
                foreach (var socket in root.GetComponentsInChildren<QASocket>(true)) QASocket.All.Remove(socket);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static string RunPhysics()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run isolated physics checks outside Play Mode.");
            var root = new GameObject("S1_RemainingMechanics_PhysicsCheck");
            var obstacle = new GameObject("S1_RemainingMechanics_Barrier");
            int checks = 0;
            Action<bool, string> check = (value, message) => { checks++; if (!value) throw new InvalidOperationException(message); };
            try
            {
                root.transform.position = new Vector3(90, -50, 90);
                var box = root.AddComponent<BoxCollider>(); box.size = Vector3.one * .01f;
                var barrier = obstacle.AddComponent<BoxCollider>(); barrier.size = new Vector3(.001f, .05f, .05f);
                obstacle.transform.position = root.transform.position + new Vector3(.05f, .1f, 0);
                var start = root.transform.localToWorldMatrix;
                var end = Matrix4x4.TRS(root.transform.position + Vector3.right * .1f, Quaternion.identity, Vector3.one);
                check(ResearchS1HeldCollision.Allows(root.transform, end), "Unobstructed held translation denied.");
                obstacle.transform.position = root.transform.position + Vector3.right * .05f;
                check(!ResearchS1HeldCollision.Allows(root.transform, end), "Held part passed through a 1 mm barrier.");
                check(root.transform.localToWorldMatrix == start, "Collision query mutated the part.");
                obstacle.transform.position = root.transform.position + Vector3.right * .003f;
                check(!ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position + Vector3.right * .002f, Quaternion.identity, Vector3.one)), "Increasing initial penetration allowed.");
                check(ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position - Vector3.right * .01f, Quaternion.identity, Vector3.one)), "Escape from initial penetration denied.");
                barrier.size = Vector3.one * .002f;
                box.size = new Vector3(.08f, .004f, .004f);
                obstacle.transform.position = root.transform.position + new Vector3(.02f, 0, -.02f);
                check(!ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position, Quaternion.Euler(0, 90, 0), Vector3.one)), "Rotating held part crossed another part.");
                obstacle.SetActive(false);
                check(ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position, Quaternion.Euler(0, 90, 0), Vector3.one)), "Unobstructed yaw denied.");
                check(!ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position + Vector3.right, Quaternion.identity, Vector3.one)), "Tracking-size jump bypassed bounded sweep.");
                check(!ResearchS1HeldCollision.Allows(root.transform, Matrix4x4.TRS(root.transform.position, Quaternion.identity, Vector3.one * 2)), "Scale mutation accepted.");
                Vector3 direction;
                check(!ResearchS1ServiceYaw.HandDirection(Vector3.zero, Vector3.up, out direction), "Vertical hand motion fabricated yaw.");
                check(ResearchS1ServiceYaw.HandDirection(Vector3.zero, Vector3.right * .2f + Vector3.up * 2, out direction) && direction == Vector3.right * .2f, "Yaw did not project onto world horizontal plane.");
                check(ResearchS1HeldCollision.Samples(float.MaxValue, 1, 1) == -1 && ResearchS1HeldCollision.Samples(float.NaN, 1, 1) == -1, "Nonfinite/overflowing sweep accepted.");
                return "S1_REMAINING_PHYSICS_CHECKS_PASS checks=" + checks + "; isolated_primitives=true; authored_geometry=false; sdk_e2e=false";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(obstacle);
                Physics.SyncTransforms();
            }
        }

        public static string RunAuthored(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task || !task.AuthoredBindingsValid()) throw new ArgumentException("Use an explicit authored Task A outside Play.");
            if (!task.serviceYaw || task.inputs.Length != 25 || !task.Capture().systemIntegrityValid)
                throw new InvalidOperationException("Service handle binding or authored scoring integrity changed.");
            return "S1_REMAINING_AUTHORED_BINDINGS_PASS inputs=25; integrity=true";
        }
    }
}
#endif
