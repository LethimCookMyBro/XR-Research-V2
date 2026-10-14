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
            check(s.cables.Length == 3 && s.cables.All(c => c.sourceConnected && !c.targetConnected), "Initial endpoints are not independently loose.");
            check(s.baseState.atx24Connected && s.baseState.epsConnected && s.baseState.cpuFanConnected && s.baseState.frontPanelConnected, "Base source contacts not seated.");
            check(s.baseState.powerIsolated && s.systemIntegrityValid, "AC isolation or authored scoring integrity lost.");
            foreach (var contact in w.contacts)
            {
                check(contact.Connected, "Original fixed endpoint not seated " + contact.mateName);
                var pose = contact.mate.localPosition;
                try { contact.mate.localPosition += Vector3.right * .002f; check(!contact.Connected, "Moved endpoint still connected."); }
                finally { contact.mate.localPosition = pose; }
            }
            foreach (var e in w.ends)
            {
                check(e.plug.GetComponent<ResearchS1TaskAInput>().plug == e.plug, "Cable endpoint input ownership lost.");
                check(w.Allows(e.plug.transform, e.plug.transform.localToWorldMatrix), "Initial flexible pose violates its original length.");
                check(!w.Allows(e.plug.transform, Matrix4x4.TRS(e.plug.transform.position + Vector3.one * 4, e.plug.transform.rotation, Vector3.one)), "Cable overextension accepted.");
                check(!e.plug.transform.IsChildOf(task.ssds[0].transform), "Loose cable grab owned by SSD.");
            }
            foreach (var target in w.targets.Where(t => t.connector == S1Connector.GpuPcie8))
                check(Vector3.Distance(target.socket.seat.localPosition, new Vector3(-.0021f,0,0)) < 1e-7f, "GPU 6+2 source offset lost.");
            var source = w.ends[0].sourceContact;
            try { w.ends[0].sourceContact = -1; check(!w.AuthoredBindingsValid(), "Invalid source index accepted."); }
            finally { w.ends[0].sourceContact = source; }
            return "S1_TASK_A_WIRING_AUTHORED checks=" + n + "; actual_input_e2e=false; integrity=false";
        }
    }
}
#endif
