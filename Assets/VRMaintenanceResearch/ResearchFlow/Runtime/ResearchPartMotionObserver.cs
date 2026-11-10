using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow
{
    [DefaultExecutionOrder(15000)]
    public sealed class ResearchPartMotionObserver : MonoBehaviour
    {
        public const string Version = "S1_PART_MOTION_OBSERVATIONS_20261031";
        const int BoundaryLimit = 32;
        readonly Queue<Boundary> boundaries = new Queue<Boundary>();
        readonly ResearchPartMotionCadence cadence = new ResearchPartMotionCadence();
        ResearchSessionFlowController controller;
        ResearchTaskPort port;
        ResearchS1FanPart fan;
        ResearchS1TaskAFastener screw;
        ResearchS1FreeBody physics;
        Rigidbody body;
        Grabbable grab;
        string target, runId, attemptId, input = "None";
        string epoch;
        bool active, contacted;
        int lostBoundaries;
        long sequence;
        double lastSampleTime;
        Vector3 samplePosition;
        Quaternion sampleRotation;
        float? sampleThread;

        readonly struct Boundary
        {
            public readonly PartMotionEvent Kind;
            public readonly double ObservedAt;
            public readonly string AttemptId, Input;
            public readonly float[] Position, Rotation, Velocity, Spin;
            public Boundary(PartMotionEvent kind, double time, string attempt, string source, Rigidbody body)
            {
                Kind = kind; ObservedAt = time; AttemptId = attempt; Input = source;
                var q = body.rotation;
                Position = Finite(body.position) ? V(body.position) : null;
                Rotation = Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) ? new[] { q.x, q.y, q.z, q.w } : null;
                Velocity = Finite(body.linearVelocity) ? V(body.linearVelocity) : null;
                Spin = Finite(body.angularVelocity) ? V(body.angularVelocity) : null;
            }
        }

        public static void Attach(ResearchS1FreeBody physics, Rigidbody body, Grabbable grab)
        {
            var fan = physics.GetComponent<ResearchS1FanPart>();
            var pc = physics.GetComponent<ResearchS1TaskAInput>();
            if ((!fan || !fan.owner) && (!pc || !pc.owner)) return;
            if (physics.GetComponent<ResearchPartMotionObserver>()) throw new InvalidOperationException("Part motion observer is already bound.");
            var observer = physics.gameObject.AddComponent<ResearchPartMotionObserver>();
            observer.physics = physics; observer.body = body; observer.grab = grab; observer.fan = fan;
            observer.screw = physics.GetComponent<ResearchS1TaskAFastener>();
            observer.port = fan ? fan.owner.port : pc.owner.port;
            observer.controller = fan ? fan.owner.controller : pc.owner.controller;
            observer.target = fan ? fan.stableId : pc.stableId;
            if (!observer.port || !observer.controller || string.IsNullOrWhiteSpace(observer.target))
                throw new InvalidOperationException("Part motion observation requires an authored target, task and controller.");
            observer.port.Behavior += observer.AttemptObserved;
        }

        bool OwnsCurrentTask => controller && port && controller.Flow != null
            && controller.Flow.CurrentTask == port.taskId && controller.Logger != null
            && controller.Logger.State == HandsLabLogState.Active;
        bool OwnsRun => controller && controller.Logger != null && controller.Logger.State == HandsLabLogState.Active
            && controller.Logger.RunId == runId;
        bool CanObserve => OwnsCurrentTask && port.InteractionAllowed && controller.Flow.CanInteract && controller.Flow.IsTaskActive;
        float? Thread => fan && fan.IsThread ? fan.ThreadProgressDegrees : screw ? screw.ProgressDegrees : (float?)null;

        void AttemptObserved(string name, string observedTarget, object before, object after)
        {
            if (observedTarget != target || !(after is ResearchTaskPort.AttemptPayload payload)) return;
            if (!CanObserve && !(payload.SystemTerminal && OwnsRun && !string.IsNullOrEmpty(epoch))) return;
            if (CanObserve) BeginEpochIfNeeded();
            attemptId = payload.AttemptId;
            input = payload.TryGetValue("input_source", out var source) ? source as string ?? "None" : "None";
            PartMotionEvent kind;
            if (name == "GRAB") { kind = PartMotionEvent.SELECTED; contacted = false; }
            else if (name == "RELEASE") { kind = payload.SystemTerminal ? PartMotionEvent.CANCELLED : PartMotionEvent.RELEASED; contacted = false; }
            else return;
            if (boundaries.Count == BoundaryLimit) { lostBoundaries++; return; }
            boundaries.Enqueue(new Boundary(kind, Time.unscaledTimeAsDouble, attemptId, input, body));
        }

        void BeginEpochIfNeeded()
        {
            string currentRun = controller.Logger.RunId;
            if (active && runId == currentRun) return;
            if (runId != currentRun) { boundaries.Clear(); lostBoundaries = 0; attemptId = null; input = "None"; }
            bool resumed = !string.IsNullOrEmpty(epoch) && runId == currentRun;
            runId = currentRun; epoch = Guid.NewGuid().ToString("N"); active = true; contacted = false;
            sequence = 0; cadence.Reset(Time.unscaledTimeAsDouble); RememberSample();
            Record(PartMotionEvent.OBSERVATION_STARTED, new { resumed, coverage = "sampled_part_motion_not_continuous_tracking" });
        }

        void LateUpdate()
        {
            if (!body || !physics) return;
            if (!CanObserve)
            {
                FlushBoundaries(); active = false; return;
            }
            BeginEpochIfNeeded();
            FlushBoundaries();
            var now = Time.unscaledTimeAsDouble;
            bool moving = Vector3.Distance(samplePosition, body.position) > .0001f
                || Quaternion.Angle(sampleRotation, body.rotation) > .02f || sampleThread != Thread
                || (!body.isKinematic && (body.linearVelocity.sqrMagnitude > .0001f || body.angularVelocity.sqrMagnitude > .0001f));
            var decision = cadence.Observe(now, moving, physics.IsDynamic);
            if ((decision & PartMotionDecision.Sample) != 0)
            {
                Record(PartMotionEvent.MOTION_SAMPLE, new { seconds_since_previous_sample = now - lastSampleTime });
                RememberSample();
            }
            if ((decision & PartMotionDecision.Settled) != 0) Record(PartMotionEvent.SETTLED, null);
        }

        void FlushBoundaries()
        {
            if (!body || !OwnsRun)
            { boundaries.Clear(); lostBoundaries = 0; return; }
            while (boundaries.Count > 0)
            {
                var boundary = boundaries.Dequeue();
                if (!OwnsCurrentTask && boundary.Kind != PartMotionEvent.CANCELLED) continue;
                Record(boundary.Kind, new { boundary_observed_unscaled_seconds = boundary.ObservedAt,
                    snapshot_delay_seconds = Time.unscaledTimeAsDouble - boundary.ObservedAt,
                    boundary_position_world = boundary.Position, boundary_rotation_world = boundary.Rotation,
                    boundary_linear_velocity_world = boundary.Velocity, boundary_angular_velocity_world_rad_s = boundary.Spin }, boundary.AttemptId, boundary.Input);
                RememberSample();
            }
            if (lostBoundaries > 0)
            {
                Record(PartMotionEvent.OBSERVATION_GAP, new { dropped_boundary_events = lostBoundaries, reason = "boundary_queue_capacity" });
                lostBoundaries = 0;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!CanObserve || contacted || !physics || !physics.IsDynamic) return;
            BeginEpochIfNeeded(); contacted = true;
            var contact = collision.contactCount > 0 ? collision.GetContact(0) : default(ContactPoint);
            var otherFan = collision.collider ? collision.collider.GetComponentInParent<ResearchS1FanPart>() : null;
            var otherPc = collision.collider ? collision.collider.GetComponentInParent<ResearchS1TaskAInput>() : null;
            Record(PartMotionEvent.FIRST_CONTACT, new { other_collider = collision.collider ? collision.collider.name : null,
                other_target = otherFan ? otherFan.stableId : otherPc ? otherPc.stableId : null,
                contact_count = collision.contactCount, point_world = collision.contactCount > 0 ? V(contact.point) : null,
                normal_world = collision.contactCount > 0 ? V(contact.normal) : null,
                relative_velocity_world = V(collision.relativeVelocity), impulse_world = V(collision.impulse) });
        }

        void Record(PartMotionEvent kind, object detail, string correlation = null, string source = null)
        {
            if (!body || !OwnsRun || (!OwnsCurrentTask && kind != PartMotionEvent.CANCELLED)) return;
            Vector3 position = body.position, velocity = body.linearVelocity, angular = body.angularVelocity;
            Quaternion rotation = body.rotation;
            bool valid = Finite(position) && Finite(velocity) && Finite(angular)
                && Finite(rotation.x) && Finite(rotation.y) && Finite(rotation.z) && Finite(rotation.w);
            float? thread = Thread;
            if (thread.HasValue && !Finite(thread.Value)) thread = null;
            string observedAttempt = correlation ?? attemptId;
            controller.Log("PART_" + kind, target, "Observed", after: new
            {
                payload_version = "research.part-motion.1", observation_version = Version,
                observation_epoch = epoch, observation_sequence = ++sequence, sampled_at_unscaled_seconds = Time.unscaledTimeAsDouble,
                sample_period_seconds = ResearchPartMotionCadence.SamplePeriodSeconds,
                attempt_id = observedAttempt, attempt_id_known = !string.IsNullOrEmpty(observedAttempt),
                pose_valid = valid, position_world = valid ? V(position) : null,
                rotation_world = valid ? new[] { rotation.x, rotation.y, rotation.z, rotation.w } : null,
                linear_velocity_world = valid ? V(velocity) : null, angular_velocity_world_rad_s = valid ? V(angular) : null,
                thread_progress_degrees = thread, held = grab.SelectingPointsCount > 0,
                free = physics.IsFree, dynamic = physics.IsDynamic, kinematic = body.isKinematic,
                observation_phase = "LateUpdateOrCollisionCallback", detail
            }, source: "System", inputSource: source ?? input, actionId: observedAttempt,
                phaseOverride: "Experiment", taskIdOverride: port.taskId, taskState: port.CaptureState());
        }

        void RememberSample()
        {
            samplePosition = body.position; sampleRotation = body.rotation; sampleThread = Thread;
            lastSampleTime = Time.unscaledTimeAsDouble;
        }
        void OnDisable() { FlushBoundaries(); active = false; }
        void OnDestroy() { if (port) port.Behavior -= AttemptObserved; }
        static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
