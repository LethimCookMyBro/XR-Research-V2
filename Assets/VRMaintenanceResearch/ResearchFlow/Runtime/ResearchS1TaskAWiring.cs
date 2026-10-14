using System;
using System.Linq;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    [Serializable]
    public sealed class S1FixedCableContact
    {
        public string circuit, assetId, mateName, seatName;
        public ResearchS1Cable cable;
        public Transform mate, seat, equipment;
        public Vector3 offset;
        public bool Valid => cable && cable.assetId == assetId && mate && mate.name == mateName && seat && seat.name == seatName &&
            equipment && seat.IsChildOf(equipment) && mate.IsChildOf(equipment) && cable.AuthoredValid();
        public bool Connected
        {
            get
            {
                if (!Valid || !mate.gameObject.activeInHierarchy || !seat.gameObject.activeInHierarchy || !cable.Allows(null, Matrix4x4.identity)) return false;
                var relative = (seat.localToWorldMatrix * Matrix4x4.Translate(offset)).inverse * mate.localToWorldMatrix;
                return ((Vector3)relative.GetColumn(3)).magnitude <= .0003f && Quaternion.Angle(relative.rotation, Quaternion.identity) <= 1;
            }
        }
    }

    [Serializable]
    public sealed class S1CableTarget
    {
        public QASocket socket;
        public QAPlug part;
        public S1Connector connector;
    }

    [Serializable]
    public sealed class S1TaskACableEnd
    {
        public ResearchS1Cable cable;
        public QAPlug plug;
        public int sourceContact;
        public string sourcePartId;
        public S1Connector sourceConnector;
    }

    public sealed class ResearchS1TaskAWiring : MonoBehaviour
    {
        public ResearchS1TaskAMechanics owner;
        public ResearchS1Cable[] cables;
        public S1FixedCableContact[] contacts;
        public S1TaskACableEnd[] ends;
        public S1CableTarget[] targets;
        public Transform acSeat, acMate;
        public QASocket acSocket;

        public bool AuthoredBindingsValid()
        {
            if (!owner || cables == null || cables.Length != 8 || cables.Distinct().Count() != 8 || !cables.All(c => c && c.AuthoredValid()) ||
                contacts == null || contacts.Length != 16 || !contacts.All(c => c != null && c.Valid) ||
                ends == null || ends.Length != 3 || targets == null || targets.Length != 8 ||
                !acSeat || acSeat.name != "SEAT_PSU_ATX_MX650_AC" || !acSeat.IsChildOf(owner.fixedAssembly[4]) ||
                !acMate || acMate.name != "MATE_CABLE_AC_C13_JP_EndA" || !acSocket || acSocket.seat != acSeat) return false;
            if (targets.Any(t => t == null || !t.socket || !t.part || t.socket.fixtureId != t.part.stableId || !t.socket.seat ||
                !t.socket.seat.IsChildOf(t.part.transform) || t.connector == S1Connector.None) || targets.Select(t => t.socket).Distinct().Count() != 8) return false;
            return ends.All(e => e != null && e.cable && cables.Contains(e.cable) && e.plug && e.plug.assetId == e.cable.assetId &&
                e.sourceContact >= 0 && e.sourceContact < contacts.Length && contacts[e.sourceContact].cable == e.cable &&
                e.sourcePartId == (e.sourceConnector == S1Connector.MotherboardSata ? "A.motherboard" : "A.psu") &&
                (e.sourceConnector == S1Connector.MotherboardSata || e.sourceConnector == S1Connector.PsuSata || e.sourceConnector == S1Connector.PsuPcie8) &&
                e.plug.mates.Count == 1 && owner.inputs.Count(i => i && i.plug == e.plug && i.pointable == e.plug.grabbable) == 1);
        }

        public bool Allows(Transform moving, Matrix4x4 pose) => cables != null && cables.All(c => c && c.Allows(moving, pose));

        public void Capture(S1TaskAState state)
        {
            state.baseState.atx24Connected = Circuit("atx24", 3);
            state.baseState.epsConnected = Circuit("eps8", 3);
            state.baseState.cpuFanConnected = Circuit("cpu_fan", 2);
            state.baseState.frontPanelConnected = Circuit("front_panel", 5);
            state.baseState.powerIsolated = acSocket && !acSocket.Occupant && acMate && acSeat &&
                Vector3.Distance(acMate.position, acSeat.position) > .05f;
            state.cables = ends.Select(e =>
            {
                var target = targets.FirstOrDefault(t => t.socket == e.plug.Socket);
                return new S1CableState { id = e.plug.stableId, sourcePartId = e.sourcePartId, sourceConnector = e.sourceConnector,
                    sourceConnected = contacts[e.sourceContact].Connected, targetPartId = target == null ? null : target.part.stableId,
                    targetConnector = target == null ? S1Connector.None : target.connector,
                    targetConnected = target != null && Seated(e.plug) && e.cable.Allows(null, Matrix4x4.identity) };
            }).ToArray();
            state.assemblyObstructed |= Application.isPlaying && ends.Any(e => e.plug.IsHeld);
        }
        private bool Circuit(string id, int expected)
        {
            var subset = contacts.Where(c => c.circuit == id).ToArray();
            return subset.Length == expected && subset.All(c => c.Connected);
        }
        private static bool Seated(QAPlug plug)
        {
            var s = plug.Socket;
            if (!s || s.Occupant != plug || !s.Mate || s.Last.stop != Stop.Seat || s.Last.rule == null || s.Last.rule.outcome != PairOutcome.Seat ||
                (s.State != SocketState.Seated && s.State != SocketState.Latched)) return false;
            var pose = SocketRules.Measure(s.seat.localToWorldMatrix, s.Mate.localToWorldMatrix);
            return Mathf.Abs(pose.depthMm) <= s.spec.seatedDepthTolMm && pose.lateralMm <= s.spec.posTolMm &&
                pose.tiltDeg <= s.spec.angTolDeg && Mathf.Abs(Mathf.DeltaAngle(pose.turnDeg, s.Last.rule.turnDeg)) <= s.spec.angTolDeg;
        }
    }
}
