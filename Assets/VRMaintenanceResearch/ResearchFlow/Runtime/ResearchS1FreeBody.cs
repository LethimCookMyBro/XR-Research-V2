using System;
using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(14000)]
    public sealed class ResearchS1FreeBody : MonoBehaviour
    {
        Rigidbody body;
        Grabbable grab;
        Func<bool> bound, allowed;
        Func<Matrix4x4, bool> tetherAllows;
        ThrowWhenUnselected velocityThrow;
        bool ownsLock, configured, pendingThrow;
        Vector3 releaseVelocity, releaseSpin;
        int releaseFrame;
        Pose physicsPose;
        bool hasPhysicsPose;

        public bool IsFree => configured && !bound();
        public bool IsDynamic => IsFree && allowed() && grab.SelectingPointsCount == 0 && !body.isKinematic;

        public void Configure(Rigidbody target, Grabbable pointable, Func<bool> isBound,
            Func<bool> interactionAllowed, Func<Matrix4x4, bool> acceptsTether = null, Func<float> clock = null)
        {
            if (configured) throw new InvalidOperationException("Free-body ownership was already configured.");
            if (!target || !pointable || isBound == null || interactionAllowed == null)
                throw new ArgumentException("Free-body bindings are incomplete.");
            if (pointable.SelectingPointsCount != 0 || target.IsLocked())
                throw new InvalidOperationException("Configure release physics before any selection or socket lock.");
            bool hasSolid = false;
            foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                hasSolid |= collider.enabled && !collider.isTrigger && collider.attachedRigidbody == target;
            if (!hasSolid) throw new InvalidOperationException("Release physics requires a solid collider owned by this body.");
            body = target;
            grab = pointable;
            bound = isBound;
            allowed = interactionAllowed;
            tetherAllows = acceptsTether;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            grab.ForceKinematicDisabled = false;
            grab.InjectOptionalThrowWhenUnselected(true);
            velocityThrow = grab.VelocityThrow;
            velocityThrow.SetRigidBody(body);
            if (clock != null) velocityThrow.SetTimeProvider(clock);
            velocityThrow.WhenThrown += Thrown;
            grab.WhenPointerEventRaised += Pointer;
            configured = true;
            IgnorePermanentChildContacts();
            SavePhysicsPose();
            Refresh();
        }

        void IgnorePermanentChildContacts()
        {
            foreach (var child in body.GetComponentsInChildren<Rigidbody>(true))
            {
                if (child == body) continue;
                var clip = child.GetComponent<ResearchS1FanPart>();
                var screw = child.GetComponent<ResearchS1TaskAFastener>();
                if (!(clip && clip.kind == S1FanPartKind.FrontClip) && !(screw && screw.Captive)) continue;
                foreach (var parentCollider in body.GetComponentsInChildren<Collider>(true))
                {
                    if (parentCollider.isTrigger || parentCollider.attachedRigidbody != body) continue;
                    foreach (var childCollider in child.GetComponentsInChildren<Collider>(true))
                        if (!childCollider.isTrigger && childCollider.attachedRigidbody == child)
                            Physics.IgnoreCollision(parentCollider, childCollider, true);
                }
            }
        }

        void Pointer(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select || evt.Type == PointerEventType.Cancel)
                pendingThrow = false;
            if (evt.Type == PointerEventType.Cancel) StopMotion();
            Refresh();
        }

        void Thrown(Vector3 velocity, Vector3 spin)
        {
            var item = GetComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject>();
            float maxSpeed = item ? item.maxReleaseSampleSpeed : 12f;
            float maxSpin = item ? item.maxReleaseSampleAngularSpeed : 35f;
            bool valid = Finite(velocity) && Finite(spin) && velocity.magnitude <= maxSpeed && spin.magnitude <= maxSpin;
            releaseVelocity = valid ? velocity : Vector3.zero;
            releaseSpin = valid ? spin : Vector3.zero;
            releaseFrame = Time.frameCount;
            pendingThrow = true;
        }

        public void Refresh()
        {
            if (!configured) return;
            bool frozen = !isActiveAndEnabled || !allowed() || bound();
            if (frozen && !ownsLock)
            {
                StopMotion();
                body.LockKinematic();
                ownsLock = true;
            }
            else if (!frozen && ownsLock)
            {
                ownsLock = false;
                body.UnlockKinematic();
            }
            if (frozen) pendingThrow = false;
            body.interpolation = body.isKinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = body.isKinematic ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
        }

        void LateUpdate()
        {
            if (!configured) return;
            Refresh();
            if (pendingThrow && IsDynamic)
            {
                body.linearVelocity = releaseVelocity;
                body.angularVelocity = releaseSpin;
                pendingThrow = false;
            }
            if (pendingThrow && Time.frameCount > releaseFrame + 1) pendingThrow = false;
            if (!IsDynamic) SavePhysicsPose();
        }

        void FixedUpdate()
        {
            if (!configured) return;
            Refresh();
            if (!IsDynamic) { SavePhysicsPose(); return; }
            if (body.IsSleeping()) return;
            if (tetherAllows != null && !tetherAllows(Matrix4x4.TRS(body.position, body.rotation, transform.lossyScale)))
            {
                if (hasPhysicsPose)
                {
                    Vector3 blocked = body.position - physicsPose.position;
                    var velocity = body.linearVelocity;
                    if (blocked.sqrMagnitude > 1e-10f)
                    {
                        var outward = blocked.normalized;
                        velocity -= outward * Mathf.Max(0, Vector3.Dot(velocity, outward));
                    }
                    else velocity = Vector3.zero;
                    body.position = physicsPose.position;
                    body.rotation = physicsPose.rotation;
                    transform.SetPositionAndRotation(physicsPose.position, physicsPose.rotation);
                    body.linearVelocity = velocity;
                    body.angularVelocity = Vector3.zero;
                }
                return;
            }
            SavePhysicsPose();
        }

        void SavePhysicsPose()
        {
            physicsPose = new Pose(body.position, body.rotation);
            hasPhysicsPose = true;
        }

        void StopMotion()
        {
            if (!body || body.isKinematic) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        void OnDisable()
        {
            pendingThrow = false;
            Refresh();
        }

        void OnDestroy()
        {
            if (grab) grab.WhenPointerEventRaised -= Pointer;
            if (velocityThrow != null) velocityThrow.WhenThrown -= Thrown;
            if (ownsLock && body) { ownsLock = false; body.UnlockKinematic(); }
        }

        static bool Finite(Vector3 value) => !float.IsNaN(value.x + value.y + value.z)
            && !float.IsInfinity(value.x + value.y + value.z);
    }
}


