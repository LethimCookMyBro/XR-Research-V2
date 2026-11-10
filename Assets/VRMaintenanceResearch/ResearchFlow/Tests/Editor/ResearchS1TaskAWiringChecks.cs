#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1TaskAWiringChecks
    {
        public static string Run(ResearchS1TaskAMechanics task)
        {
            if (Application.isPlaying || !task || !task.wiring) throw new ArgumentException("Explicit Edit Mode fixture required.");
            int n = 0;
            Action<bool,string> check = (ok, why) => { n++; if (!ok) throw new InvalidOperationException(why); };
            var w = task.wiring;
            check(w.AuthoredBindingsValid(), "Wiring authored graph invalid.");
            check(w.cables.All(c => !UnityEditor.PrefabUtility.IsPartOfPrefabInstance(c.gameObject)), "Cable heads still locked in imported prefab hierarchy.");
            var s = task.Capture();
            check(s.cables.Length == 3 && s.cables.All(c => !c.sourceConnected && !c.targetConnected), "Task cables must start loose at both ends.");
            check(s.baseState.atx24Connected && s.baseState.epsConnected && s.baseState.cpuFanConnected && s.baseState.frontPanelConnected, "Base source contacts not seated.");
            check(s.baseState.powerIsolated && s.systemIntegrityValid, "AC isolation or authored scoring integrity lost.");
            foreach (var contact in w.contacts)
            {
                check(contact.Connected, "Original fixed endpoint not seated " + contact.mateName);
                var pose = contact.mate.localPosition;
                try { contact.mate.localPosition += Vector3.right * .002f; check(!contact.Connected, "Moved endpoint still connected."); }
                finally { contact.mate.localPosition = pose; }
            }
            foreach (var plug in w.ends.SelectMany(e => new[] { e.plug, e.sourcePlug }))
            {
                check(plug.GetComponent<ResearchS1TaskAInput>().plug == plug, "Cable endpoint input ownership lost.");
                check(w.Allows(plug.transform, plug.transform.localToWorldMatrix), "Initial flexible pose violates its original length.");
                check(w.Allows(plug.transform, Matrix4x4.TRS(plug.transform.position + Vector3.one * 4, plug.transform.rotation, Vector3.one)), "A cable loose at both ends limited the hand.");
                check(!plug.transform.IsChildOf(task.ssds[0].transform) && !plug.transform.IsChildOf(task.fixedAssembly[1]) && !plug.transform.IsChildOf(task.fixedAssembly[4]),
                    "Loose cable head owned by a part, the board or the PSU.");
            }
            foreach (var target in w.targets.Where(t => t.connector == S1Connector.GpuPcie8))
                check(Vector3.Distance(target.socket.seat.localPosition, new Vector3(-.0021f,0,0)) < 1e-7f, "GPU 6+2 source offset lost.");
            var source = w.ends[0].sourceSockets;
            try { w.ends[0].sourceSockets = new VRMaintenanceResearch.AssetLibraryQA.QASocket[0]; check(!w.AuthoredBindingsValid(), "A task cable without source ports accepted."); }
            finally { w.ends[0].sourceSockets = source; }
            return "S1_TASK_A_WIRING_AUTHORED checks=" + n + "; actual_input_e2e=false; integrity=false";
        }
    }
}
#endif
