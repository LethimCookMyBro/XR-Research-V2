using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    [Serializable]
    public sealed class S1CableSpan
    {
        public Transform from, to;
        public Vector3 fromExit, toExit, fromDirection, toDirection;
        [NonSerialized] public Vector3[] cachedPath;
        [NonSerialized] public Vector3 cachedA, cachedB, cachedDa, cachedDb, cachedSide;
        [NonSerialized] public bool cached;
        public Transform[] bones;
        public Quaternion[] restRotations;
        public Vector3[] restPositions;
        public Vector3 restToPosition;
        public Quaternion restToRotation, restFromRotation;
        public Vector3[] restDirections;
        public float length, minimumRadius;
    }

    [DefaultExecutionOrder(14000)]
    public sealed class ResearchS1Cable : MonoBehaviour
    {
        public string assetId;
        public S1CableSpan[] spans;
        public SkinnedMeshRenderer skin;

        // Re-checking every bone name each frame allocated ~25 KB/frame across the 8 cables on Quest (GC every ~4 s).
        // shortcut: a pass is cached per spans array + assetId; bones destroyed in place are not re-detected (authoring never does that).
        S1CableSpan[] validatedSpans; string validatedId;
        public bool AuthoredValid()
        {
            if (validatedSpans != null && ReferenceEquals(validatedSpans, spans) && validatedId == assetId && skin && skin.sharedMesh) return true;
            bool valid = !string.IsNullOrEmpty(assetId) && skin && skin.sharedMesh && spans != null && spans.Length > 0 &&
                spans.All(s => s != null && s.from && s.to && s.length > 0 && s.bones != null && s.bones.Length >= 2 &&
                    s.restPositions != null && s.restPositions.Length == s.bones.Length && s.restRotations != null && s.restRotations.Length == s.bones.Length && s.restDirections != null && s.restDirections.Length == s.bones.Length &&
                    s.bones.All(b => b && b.name.StartsWith("B_" + assetId + "_", StringComparison.Ordinal)));
            validatedSpans = valid ? spans : null; validatedId = valid ? assetId : null;
            return valid;
        }

        public bool Allows(Transform moving, Matrix4x4 proposed)
        {
            if (!AuthoredValid()) return false;
            foreach (var span in spans)
            {
                Vector3 a = Exit(span.from, span.fromExit, moving, proposed), b = Exit(span.to, span.toExit, moving, proposed);
                if (Rigid(span,moving,proposed)) continue;
                Vector3 da = Direction(span.from, span.fromDirection, moving, proposed), db = Direction(span.to, span.toDirection, moving, proposed);
                if (Path(span,a,b,da,db) == null) return false;
            }
            return true;
        }

        private static Vector3 Exit(Transform endpoint, Vector3 offset, Transform moving, Matrix4x4 proposed)
        {
            var pose = endpoint.localToWorldMatrix;
            if (moving && (endpoint == moving || endpoint.IsChildOf(moving))) pose = proposed * moving.worldToLocalMatrix * pose;
            return pose.MultiplyPoint3x4(offset);
        }

        private static Vector3 Direction(Transform endpoint, Vector3 offset, Transform moving, Matrix4x4 proposed)
        {
            var pose = endpoint.localToWorldMatrix;
            if (moving && (endpoint == moving || endpoint.IsChildOf(moving))) pose = proposed * moving.worldToLocalMatrix * pose;
            return pose.MultiplyVector(offset);
        }
        private static bool Rigid(S1CableSpan span) => Rigid(span,null,Matrix4x4.identity);
        private static bool Rigid(S1CableSpan span,Transform moving,Matrix4x4 proposed)
        {
            var a=span.from.localToWorldMatrix;var b=span.to.localToWorldMatrix;
            if(moving && (span.from==moving || span.from.IsChildOf(moving)))a=proposed*moving.worldToLocalMatrix*a;
            if(moving && (span.to==moving || span.to.IsChildOf(moving)))b=proposed*moving.worldToLocalMatrix*b;
            var relative=a.inverse*b;
            return Vector3.Distance(relative.GetColumn(3),span.restToPosition)<.00001f && Quaternion.Angle(relative.rotation,span.restToRotation)<.01f;
        }
        private Vector3[] Path(S1CableSpan span,Vector3 a,Vector3 b,Vector3 da,Vector3 db)
        {
            if(span.cached && span.cachedA==a && span.cachedB==b && span.cachedDa==da && span.cachedDb==db)return span.cachedPath;
            span.cached=true;span.cachedA=a;span.cachedB=b;span.cachedDa=da;span.cachedDb=db;span.cachedPath=null;
            // A real cable may bow in any plane. Keep the last valid plane first so the drawn cable does not flip between frames.
            foreach(var bow in new[]{span.cachedSide,transform.up,-transform.up,transform.right,-transform.right,transform.forward,-transform.forward})
            {
                Vector3 side=Vector3.ProjectOnPlane(bow,b-a).normalized;
                if(side.sqrMagnitude<.5f)continue;
                span.cachedPath=ResearchS1CablePath.Solve(a,b,da,db,span.length,side,span.minimumRadius);
                if(span.cachedPath!=null){span.cachedSide=bow;break;}
            }
            return span.cachedPath;
        }
        public void RefreshShape()
        {
            if (!AuthoredValid()) return;
            foreach (var span in spans)
            {
                if (Rigid(span))
                {
                    for (int i = 0; i < span.bones.Length; i++) span.bones[i].SetPositionAndRotation(span.from.TransformPoint(span.restPositions[i]),
                        span.from.rotation * Quaternion.Inverse(span.restFromRotation) * span.restRotations[i]);
                    continue;
                }
                Vector3 a = span.from.TransformPoint(span.fromExit), b = span.to.TransformPoint(span.toExit);
                var path=Path(span,a,b,span.from.TransformDirection(span.fromDirection),span.to.TransformDirection(span.toDirection));
                if(path==null)continue;
                for (int i = 0; i < span.bones.Length; i++)
                {
                    Vector3 position,tangent;
                    ResearchS1CablePath.At(path,(double)i/span.bones.Length,out position,out tangent);
                    Quaternion rotation = Quaternion.FromToRotation(transform.TransformDirection(span.restDirections[i]), tangent) * transform.rotation * span.restRotations[i];
                    span.bones[i].SetPositionAndRotation(position, rotation);
                }
            }
        }
        private void LateUpdate() => RefreshShape();
    }
}
