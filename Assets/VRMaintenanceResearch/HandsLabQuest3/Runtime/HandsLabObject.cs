using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3.Runtime
{
    public sealed class HandsLabObject : MonoBehaviour
    {
        public string stableId = "handslab.object";
        public Rigidbody body;
        public Grabbable grabbable;
        public HandsLabSession session;
        [Min(1)] public float rotationEventDegrees = 15;
        [Min(.1f)] public float maxReleaseSampleSpeed = 12f;
        [Min(.1f)] public float maxReleaseSampleAngularSpeed = 35f;
        public bool IsHeld => pointers.Count != 0;
        public string SystemReason { get; private set; }
        public string SystemActionId { get; private set; }
        readonly HashSet<int> pointers = new HashSet<int>();
        VRMaintenanceResearch.ResearchFlow.ResearchSessionFlowController technicalHoldOwner;
        const int TechnicalHoldPointer = int.MinValue;
        Vector3 authoredPosition;
        Quaternion authoredRotation, grabRotation;
        bool initialized, subscribed, rotationLogged;
        readonly Queue<MotionSample> releaseMotion = new Queue<MotionSample>(12);
        double lastMotionTime;
        Pose lastMotionPose;
        readonly struct MotionSample
        {
            public readonly double time;
            public readonly Pose pose;
            public MotionSample(double time,Pose pose){this.time=time;this.pose=pose;}
        }

        public void Initialize(HandsLabSession owner)
        {
            session=owner;if(!body)body=GetComponent<Rigidbody>();if(!grabbable)grabbable=GetComponent<Grabbable>();
            if(!body||!grabbable||string.IsNullOrWhiteSpace(stableId))throw new InvalidOperationException("HandsLabObject requires stable ID, Rigidbody and Grabbable");
            if(!initialized){authoredPosition=transform.position;authoredRotation=transform.rotation;initialized=true;}
            // Register the SDK velocity writer before our snapshots, independent of Start order.
            _ = grabbable.VelocityThrow;
            if(!subscribed){grabbable.WhenPointerEventRaised+=Pointer;subscribed=true;}
        }
        public object CaptureSnapshot() => new { target=stableId,held=IsHeld,pointer_ids=pointers.OrderBy(x=>x).ToArray(),
            position=V(transform.position),rotation=Q(transform.rotation),velocity=body?V(body.linearVelocity):null,
            angular_velocity=body?V(body.angularVelocity):null };

        // State-only fixture: it never injects SDK pointer events, native tracking or physical grab evidence.
        public bool SetIsolatedTechnicalHold(VRMaintenanceResearch.ResearchFlow.ResearchSessionFlowController controller, bool held)
        {
            if (!held)
            {
                if (!controller || technicalHoldOwner != controller) return false;
                pointers.Remove(TechnicalHoldPointer); technicalHoldOwner = null;
                return true;
            }
            if (!controller || !VRMaintenanceResearch.ResearchFlow.ResearchSessionFlowController.TechnicalBuild
                || controller.Record?.group != VRMaintenanceResearch.ResearchFlow.ParticipantGroup.Test
                || controller.Record.test_data != true || controller.trainingObjects == null || !controller.trainingObjects.Contains(this)) return false;
            var scenario = UnityEngine.Object.FindFirstObjectByType<VRMaintenanceResearch.ResearchFlow.ResearchTechnicalScenario>();
            if (!scenario || !scenario.Owns(controller)) return false;
            technicalHoldOwner = controller;
            if (pointers.Add(TechnicalHoldPointer) && pointers.Count == 1)
            {
                grabRotation = transform.rotation; rotationLogged = false;
                ResetReleaseMotion(Time.realtimeSinceStartupAsDouble);
            }
            return true;
        }
        void Pointer(PointerEvent evt)
        {
            if(evt.Type!=PointerEventType.Select&&evt.Type!=PointerEventType.Unselect&&evt.Type!=PointerEventType.Cancel)return;
            var before=CaptureSnapshot();
            if(evt.Type==PointerEventType.Select)
            {
                if(!pointers.Add(evt.Identifier))return;
                if(pointers.Count==1)
                {
                    grabRotation=transform.rotation;rotationLogged=false;
                    ResetReleaseMotion(Time.realtimeSinceStartupAsDouble);
                }
            }
            else
            {
                if(!pointers.Contains(evt.Identifier))return;
                if(evt.Type==PointerEventType.Unselect&&pointers.Count==1&&!body.isKinematic)
                {
                    bool system=session&&session.IsSystemRelease(this,evt);
                    Vector3 linear=Vector3.zero,angular=Vector3.zero;
                    if(!system)
                    {
                        SampleReleaseMotion(Time.realtimeSinceStartupAsDouble);CalculateReleaseMotion(out linear,out angular);
                        if(!(linear.magnitude<=maxReleaseSampleSpeed)||!(angular.magnitude<=maxReleaseSampleAngularSpeed))
                            linear=angular=Vector3.zero;
                    }
                    body.linearVelocity=linear;body.angularVelocity=angular;
                }
                pointers.Remove(evt.Identifier);
            }
            if(session)session.ObjectPointer(this,evt,before,CaptureSnapshot());
        }
        void ResetReleaseMotion(double now)
        {
            releaseMotion.Clear();lastMotionTime=now;
            lastMotionPose=new Pose(transform.position,transform.rotation);
            releaseMotion.Enqueue(new MotionSample(now,lastMotionPose));
        }
        void LateUpdate(){if(IsHeld)SampleReleaseMotion(Time.realtimeSinceStartupAsDouble);}
        void SampleReleaseMotion(double now)
        {
            double dt=now-lastMotionTime;
            // Cap sampling at 90 Hz so fast Editor frames cannot erase the release history.
            if(dt<1d/90)return;
            var pose=new Pose(transform.position,transform.rotation);
            bool valid=dt<=.2&&Vector3.Distance(pose.position,lastMotionPose.position)/dt<=maxReleaseSampleSpeed
                &&Quaternion.Angle(pose.rotation,lastMotionPose.rotation)*Mathf.Deg2Rad/dt<=maxReleaseSampleAngularSpeed;
            if(!valid)releaseMotion.Clear();
            // Include stillness and rotation-only poses; SDK 205 discards unchanged positions.
            if(valid)releaseMotion.Enqueue(new MotionSample(now,pose));
            while(releaseMotion.Count>1&&now-releaseMotion.Peek().time>.12)releaseMotion.Dequeue();
            lastMotionTime=now;lastMotionPose=pose;
        }
        void CalculateReleaseMotion(out Vector3 linear,out Vector3 angular)
        {
            linear=angular=Vector3.zero;
            if(releaseMotion.Count<2)return;
            var first=releaseMotion.Peek();var previous=first;
            foreach(var sample in releaseMotion)
            {
                var delta=sample.pose.rotation*Quaternion.Inverse(previous.pose.rotation);
                delta.ToAngleAxis(out float degrees,out Vector3 axis);
                if(degrees>180)degrees-=360;
                if(Mathf.Abs(degrees)>.0001f)angular+=axis*(degrees*Mathf.Deg2Rad);
                previous=sample;
            }
            float duration=(float)(previous.time-first.time);
            linear=(previous.pose.position-first.pose.position)/duration;
            angular/=duration;
        }
        void Update()
        {
            if(!IsHeld||rotationLogged||!session||session.SystemChanging)return;
            if(Quaternion.Angle(grabRotation,transform.rotation)<rotationEventDegrees)return;
            rotationLogged=true;session.ObjectRotated(this,pointers.First(),CaptureSnapshot());
        }
        public void DetachForSystem(string reason,string actionId)
        {
            SystemReason=reason;SystemActionId=actionId;
            try
            {
                bool wasEnabled=grabbable.enabled;grabbable.enabled=false;
                // SDK PointableElement.OnDisable sends Cancel. If an inactive provider already
                // removed that callback, record the residual cleanup explicitly as system work.
                foreach(int id in pointers.ToArray())
                {
                    var before=CaptureSnapshot();pointers.Remove(id);
                    if(session)session.ResidualSystemRelease(this,id,before,CaptureSnapshot(),reason,actionId);
                }
                grabbable.enabled=wasEnabled;
                if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            }
            finally{SystemReason=null;SystemActionId=null;}
        }
        public void ResetToAuthored()
        {
            if(IsHeld)throw new InvalidOperationException("System detach must precede HandsLab object reset");
            bool kinematic=body.isKinematic;body.isKinematic=true;
            transform.SetPositionAndRotation(authoredPosition,authoredRotation);body.position=authoredPosition;body.rotation=authoredRotation;
            body.isKinematic=kinematic;if(!kinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            rotationLogged=false;
        }
        public void Unbind()
        {
            if(subscribed&&grabbable)grabbable.WhenPointerEventRaised-=Pointer;
            subscribed=false;pointers.Clear();
        }
        void OnDestroy(){Unbind();}
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
    }
}
