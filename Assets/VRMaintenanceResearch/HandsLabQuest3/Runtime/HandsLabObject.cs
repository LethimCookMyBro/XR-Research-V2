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
        public bool IsHeld => pointers.Count != 0;
        public string SystemReason { get; private set; }
        public string SystemActionId { get; private set; }
        readonly HashSet<int> pointers = new HashSet<int>();
        Vector3 authoredPosition;
        Quaternion authoredRotation, grabRotation;
        bool initialized, subscribed, rotationLogged;

        public void Initialize(HandsLabSession owner)
        {
            session=owner;if(!body)body=GetComponent<Rigidbody>();if(!grabbable)grabbable=GetComponent<Grabbable>();
            if(!body||!grabbable||string.IsNullOrWhiteSpace(stableId))throw new InvalidOperationException("HandsLabObject requires stable ID, Rigidbody and Grabbable");
            if(!initialized){authoredPosition=transform.position;authoredRotation=transform.rotation;initialized=true;}
            if(!subscribed){grabbable.WhenPointerEventRaised+=Pointer;subscribed=true;}
        }
        public object CaptureSnapshot() => new { target=stableId,held=IsHeld,pointer_ids=pointers.OrderBy(x=>x).ToArray(),
            position=V(transform.position),rotation=Q(transform.rotation),velocity=body?V(body.linearVelocity):null };
        void Pointer(PointerEvent evt)
        {
            if(evt.Type!=PointerEventType.Select&&evt.Type!=PointerEventType.Unselect&&evt.Type!=PointerEventType.Cancel)return;
            var before=CaptureSnapshot();
            if(evt.Type==PointerEventType.Select)
            {
                if(!pointers.Add(evt.Identifier))return;
                if(pointers.Count==1){grabRotation=transform.rotation;rotationLogged=false;}
            }
            else if(!pointers.Remove(evt.Identifier))return;
            if(session)session.ObjectPointer(this,evt,before,CaptureSnapshot());
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
