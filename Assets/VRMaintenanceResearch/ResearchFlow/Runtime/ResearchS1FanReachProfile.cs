using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchS1FanReachProfile
    {
        public const string Version = "S1_FAN_REACH_LOWER_20261031";
        public const float LoweringMetres = .30f;
        readonly ResearchS1TaskBMechanics fan;
        readonly Transform head;
        readonly ResearchS1FanPart[] parts;
        readonly List<NodePose> nodes = new List<NodePose>();
        readonly Vector3 localDrop;
        bool lowered;

        readonly struct NodePose
        {
            readonly Transform node;
            readonly Vector3 originalPosition, originalScale, lowerPosition, lowerScale;
            public NodePose(Transform value, Vector3 position, Vector3 scale)
            {
                node = value; originalPosition = value.localPosition; originalScale = value.localScale;
                lowerPosition = position; lowerScale = scale;
            }
            public void Apply(bool lower)
            {
                node.localPosition = lower ? lowerPosition : originalPosition;
                node.localScale = lower ? lowerScale : originalScale;
            }
        }

        public ResearchS1FanReachProfile(ResearchS1TaskBMechanics value)
        {
            if (!value || value.fixedAssembly == null || value.fixedAssembly.Length != 3)
                throw new ArgumentException("A complete authored fan is required.");
            fan = value; head = fan.fixedAssembly[2];
            var stand = fan.fixedAssembly[1];
            parts = new[] { fan.frontGuard, fan.blade, fan.spinner, fan.rearGuard, fan.rearLock };
            if (parts.Any(p => !p || !p.body || !p.grabbable)) throw new ArgumentException("Fan reach bindings are incomplete.");
            localDrop = head.InverseTransformVector(Vector3.down * LoweringMetres);
            if (Mathf.Abs(localDrop.x) > .0001f || Mathf.Abs(localDrop.y) > .0001f || localDrop.z <= 0)
                throw new InvalidOperationException("The authored fan pipe must be upright on local -Z.");
            float standFactor = ShorteningFactor(Find(stand, "COL_FAN_PED_STAND_Tube"));
            float headFactor = ShorteningFactor(Find(head, "COL_FAN_PED_HEAD_Pipe"));
            foreach (string name in new[] { "COL_FAN_PED_STAND_Tube", "SEL_FAN_PED_STAND_Stand", "VIS_FAN_PED_STAND_LOD0" })
                Compress(Find(stand, name), standFactor, true);
            foreach (string name in new[] { "GRAB_FAN_PED_STAND_Tube", "MOV_FAN_PED_STAND_SlideKnob", "SEAT_FAN_PED_STAND_PipeScrew" })
                Compress(Find(stand, name), standFactor, false);
            foreach (string name in new[] { "COL_FAN_PED_HEAD_Pipe", "VIS_FAN_PED_HEAD_LOD0" })
                Compress(Find(head, name), headFactor, true);
            Compress(Find(head, "GRAB_FAN_PED_HEAD_Pipe"), headFactor, false);
            var swing = Find(head, "MOV_FAN_PED_HEAD_Swing");
            if (swing.parent != head) throw new InvalidOperationException("The authored swing parent changed.");
            nodes.Add(new NodePose(swing, swing.localPosition + localDrop, swing.localScale));
        }

        public bool Lowered => lowered;

        public void SetLowered(bool value)
        {
            if (value == lowered) return;
            if (fan.GetComponentsInChildren<Oculus.Interaction.Grabbable>(true).Any(g => g.SelectingPointsCount != 0))
                throw new InvalidOperationException("Release the fan before changing its reach profile.");
            var delta = head.TransformVector(localDrop) * (value ? 1 : -1);
            foreach (var node in nodes) node.Apply(value);
            foreach (var part in parts)
            {
                part.transform.position += delta;
                part.body.position = part.transform.position;
                part.body.rotation = part.transform.rotation;
            }
            lowered = value;
            Physics.SyncTransforms();
        }

        float ShorteningFactor(Transform node)
        {
            var box = node.GetComponent<BoxCollider>();
            if (!box) throw new InvalidOperationException("The authored pipe collider is missing.");
            float length = node.TransformVector(Vector3.forward * box.size.z).magnitude;
            if (length <= LoweringMetres + .15f) throw new InvalidOperationException("The pipe is too short for this profile.");
            return (length - LoweringMetres) / length;
        }

        void Compress(Transform node, float factor, bool scaleGeometry)
        {
            var position = node.localPosition; position.z *= factor;
            var scale = node.localScale; if (scaleGeometry) scale.z *= factor;
            nodes.Add(new NodePose(node, position, scale));
        }

        static Transform Find(Transform parent, string name)
        {
            var found = parent.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Expected one fan reach node: " + name);
            return found[0];
        }
    }
}
