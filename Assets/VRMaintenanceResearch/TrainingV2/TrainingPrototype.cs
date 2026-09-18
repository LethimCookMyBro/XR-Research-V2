using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRMaintenanceResearch.TrainingV2;

namespace VRTrainingV2
{
    public sealed class TrainingPrototype : MonoBehaviour, IXRSelectFilter
    {
        public XRGrabInteractable block;
        public BoxCollider blockCollider, targetPad;
        public TrainingPanel panel;
        public Transform head, left, right, origin;
        public NearFarInteractor leftInteractor, rightInteractor;
        public InputActionReference leftGrip, rightGrip;
        public TrainingRules prototypeRules = new TrainingRules();
        [Range(1,60)] public float movementHz = 10;
        [Tooltip("Prototype support-height tolerance in metres")] public float supportToleranceM=.012f;
        [Tooltip("Numerical contact slop in metres, not placement tolerance")] public float contactSlopM=.001f;
        public string logRoot = "";
        public TrainingSession Session { get; private set; }
        public string actorSource = "User";
        public bool canProcess => isActiveAndEnabled;
        Rigidbody body;
        Vector3 startPosition;
        Quaternion startRotation;
        TrainingSnapshot lastSnapshot;
        bool systemDetach, wasPlaced, waitLeftRelease, waitRightRelease, initialized;
        double nextMovement;
        int manualPage;
        string panelSource="System";
        string lastFeedback = "", lastInspectAction = "";
        readonly List<Collider> nearTargets = new List<Collider>();
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (Session == null || Session.State != TrainingState.Running) return false;
            if (block.isSelected) return block.interactorsSelecting.Contains(interactor);
            var near = interactor as NearFarInteractor;
            if (!near || (near==leftInteractor && waitLeftRelease) || (near==rightInteractor && waitRightRelease)) return false;
            if(TrainingUIArbitration.PanelOwnsRay(near,panel.transform as RectTransform))return false;
            // Close grab only; ray remains available for UI. No answer-based socket/filter or snapping.
            nearTargets.Clear();
            if (!near.nearInteractionCaster.TryGetColliderTargets(near.interactionManager,nearTargets) || !nearTargets.Contains(blockCollider)) return false;
            if (near.TryGetCurrentUIRaycastResult(out var ui) && ui.isValid) return false;
            return true;
        }
        void Start()
        {
            body=block.GetComponent<Rigidbody>();if(!initialized){startPosition=block.transform.position;startRotation=block.transform.rotation;}initialized=true;
            nextMovement=0;wasPlaced=false;panel.SetCommandsEnabled(true);
            var source=InputSystem.devices.Any(d=>d.layout.StartsWith("XRSimulated",StringComparison.Ordinal)) ? "Simulator" : "Device";
            Session=new TrainingSession(string.IsNullOrWhiteSpace(logRoot)?Path.Combine(Application.persistentDataPath,"TrainingV2_TestData"):logRoot,source,movementHz);
            Session.Rules.positionToleranceM=prototypeRules.positionToleranceM;
            Session.Rules.angleToleranceDeg=prototypeRules.angleToleranceDeg;
            Session.Rules.maxSpeedMps=prototypeRules.maxSpeedMps;
            Session.WriteFailed += ShowWriteError;
            block.selectFilters.Add(this); block.selectEntered.AddListener(Grab); block.selectExited.AddListener(Release);
            foreach(var near in new[]{leftInteractor,rightInteractor}){var gate=near.GetComponent<TrainingUIArbitration>();if(!gate)gate=near.gameObject.AddComponent<TrainingUIArbitration>();gate.Configure(this,near);}
            panel.Command+=Command; panel.PageChanged+=PageChanged;
            body.isKinematic=true; lastSnapshot=Snapshot();
            Session.Record("PANEL_SHOWN","training.panel.start","start",lastSnapshot,lastSnapshot,"System");
            panel.ShowPage("start");
            if(Session.State==TrainingState.WriteError)ShowWriteError(Session.LastError);
        }
        void ShowWriteError(string message) { Debug.LogError("TRAINING_WRITE_ERROR: "+message); Freeze(); panel.SetTrialStatus("ระบบขัดข้อง"); panel.ShowPage("aborted","การบันทึกขัดข้อง — รอบนี้มีข้อมูลไม่สมบูรณ์\nโปรดแจ้งผู้ดูแลก่อนเริ่มใหม่");panel.SetCommandsEnabled(false); }
        void PageChanged(string before,string after)
        {
            if(Session==null)return;
            var s=Snapshot();
            Session.Record("PANEL_HIDDEN","training.panel."+before,before,s,s,"System");
            Session.Record("PANEL_SHOWN","training.panel."+after,after,s,s,"System");
            if(before=="manual" && after!="manual") Session.Record("MANUAL_CLOSED","training.manual",before,s,s,panelSource);
            panelSource="System";
        }
        public void Command(string command)
        {
            if(Session==null || Session.State==TrainingState.WriteError)return;
            panelSource=actorSource;
            var s=Snapshot();
            if(command=="start" || command=="new_trial")
            {
                if(Session.State!=TrainingState.Ready && Session.State!=TrainingState.Passed && Session.State!=TrainingState.Aborted)return;
                // The start event records its actual state; the subsequent system reset has its own before/after.
                if(!Session.StartTrial(s,actorSource))return;
                waitLeftRelease=true;waitRightRelease=true;
                systemDetach=true; block.enabled=false; body.isKinematic=true;
                block.transform.SetPositionAndRotation(startPosition,startRotation); body.position=startPosition;body.rotation=startRotation;
                block.enabled=true; systemDetach=false; body.isKinematic=false;
                wasPlaced=false;manualPage=0;lastFeedback="";
                if(!Session.Record("OBJECT_RESET","training.block","authored_start",s,Snapshot(),"System"))return;
                panel.SetTrialStatus("บทฝึกกลาง • รอบ "+Session.TrialId); panel.ShowPage("training");
            }
            else if(command=="pause" && Session.State==TrainingState.Running)
            {
                if(!Session.Pause(s,actorSource))return; Freeze(); if(Session.State==TrainingState.WriteError)return; panel.ShowPage("pause");
            }
            else if(command=="resume" && Session.State==TrainingState.Paused)
            {
                if(!Session.Resume(s,actorSource))return;waitLeftRelease=true;waitRightRelease=true;block.enabled=true;body.isKinematic=false;panel.ShowPage("training");
            }
            else if(command=="abort" && (Session.State==TrainingState.Running || Session.State==TrainingState.Paused))
            {
                if(!Session.Abort(s,actorSource))return;Freeze();if(Session.State==TrainingState.WriteError)return;panel.ShowPage("aborted");
            }
            else if(Session.State==TrainingState.Running)
            {
                switch(command)
                {
                    case "manual_open": manualPage=0; if(!Session.Record("MANUAL_OPENED","training.manual","page_0",s,s,actorSource))return; panel.ShowManualPage(manualPage);panel.ShowPage("manual");break;
                    case "manual_next": case "manual_previous":
                        var old=manualPage;manualPage=Mathf.Clamp(manualPage+(command=="manual_next"?1:-1),0,2);
                        if(old!=manualPage){if(!Session.Record("MANUAL_PAGE_CHANGED","training.manual",old+"->"+manualPage,s,s,actorSource))return;panel.ShowManualPage(manualPage);}break;
                    case "manual_close":case "back": panel.ShowPage("training");break;
                    case "examine":if(!Session.Record("EXAMINE","training.block","ลูกศรบนชิ้นงานบอกทิศด้านหน้า; การดูข้อมูลไม่เปลี่ยนชิ้นงาน",s,s,actorSource))return;panel.ShowPage("examine");break;
                    case "inspect":
                        var result=Session.Inspect(s,source:actorSource);
                        if(!result.Accepted)return;
                        lastFeedback=result.DisplayFeedback;lastInspectAction=result.ActionId;
                        if(result.Passed)Freeze();
                    if(Session.State==TrainingState.WriteError)return;
                        panel.ShowPage(result.Passed?"passed":"inspect",lastFeedback);
                        Session.Record("INSPECT_RESULT_SHOWN","training.inspect",lastFeedback,s,s,"System",lastInspectAction);break;
                }
            }
        }
        void Freeze()
        {
            if(systemDetach)return;
            var before=Snapshot();
            systemDetach=true;
            block.enabled=false;body.isKinematic=true;systemDetach=false;
            if(before.held)Session.Record("SYSTEM_HOLD_SUSPENDED","training.block","selection_cancelled_not_participant_release",before,Snapshot(),"System");
        }
        void Grab(SelectEnterEventArgs args)
        {
            var before=lastSnapshot??Snapshot();var after=Snapshot();
            Session.Record("GRAB","training.block","selected",before,after,actorSource);lastSnapshot=after;
        }
        void Release(SelectExitEventArgs args)
        {
            if(systemDetach || args.isCanceled || Session.State!=TrainingState.Running)return;
            var after=Snapshot();Session.Record("RELEASE","training.block","released",lastSnapshot,after,actorSource);lastSnapshot=after;
        }
        public TrainingSnapshot Snapshot()
        {
            if(!body)body=block.GetComponent<Rigidbody>();
            var t=block.transform;var local=targetPad.transform.InverseTransformPoint(t.position)-targetPad.center;
            var half=targetPad.size*.5f; bool inside=true;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var corner=t.TransformPoint(blockCollider.center+Vector3.Scale(blockCollider.size*.5f,new Vector3(x,y,z)));
                var p=targetPad.transform.InverseTransformPoint(corner)-targetPad.center;
                if(Mathf.Abs(p.x)>half.x || Mathf.Abs(p.z)>half.z)inside=false;
            }
            var expected=targetPad.bounds.max.y+blockCollider.bounds.extents.y;
            inside &= Mathf.Abs(t.position.y-expected)<supportToleranceM;
            // Exact collider contact geometry, with at most 2 mm of numerical solver slop.
            // A 1 cm zero-velocity hover is not supported, unlike the previous proximity ray.
            float slop=Mathf.Clamp(contactSlopM,0f,.002f);
            bool supported=blockCollider.enabled && targetPad.enabled && !blockCollider.isTrigger && !targetPad.isTrigger &&
                Physics.ComputePenetration(blockCollider,t.position-Vector3.up*slop,t.rotation,
                    targetPad,targetPad.transform.position,targetPad.transform.rotation,out var normal,out var depth) && normal.y>.8f && depth>0f;
            return new TrainingSnapshot{held=block.isSelected,insideTarget=inside,supported=supported,
                positionErrorM=Vector3.ProjectOnPlane(t.position-targetPad.transform.TransformPoint(targetPad.center),targetPad.transform.up).magnitude,angleErrorDeg=Quaternion.Angle(t.rotation,targetPad.transform.rotation),
                speedMps=body?body.linearVelocity.magnitude:0,position=V(t.position),rotation=Q(t.rotation),
                holder=block.isSelected?block.interactorsSelecting[0].transform.parent.name:""};
        }
        void Update()
        {
            if(Session==null)return;
            if(!leftInteractor.selectInput.ReadIsPerformed())waitLeftRelease=false;
            if(!rightInteractor.selectInput.ReadIsPerformed())waitRightRelease=false;
            // Built-in blockUIOnInteractableSelection covers each NearFar independently.
            if(Session.Timestamp>=nextMovement){nextMovement=Session.Timestamp+1.0/movementHz;Session.SampleMovement(Pose(head,"Head"),Pose(left,"LeftHand"),Pose(right,"RightHand"),Pose(origin,null));}
            var s=Snapshot();
            if(Session.State==TrainingState.Running)
            {
                bool placed=s.insideTarget&&s.supported&&!s.held&&s.speedMps<prototypeRules.maxSpeedMps;
                if(placed!=wasPlaced){Session.Record(placed?"PLACED":"REMOVED","training.target",placed?"supported_in_target":"no_longer_placed",lastSnapshot,s,"System");wasPlaced=placed;}
            }
            lastSnapshot=s;
        }
        TrainingPose Pose(Transform t,string usage)
        {
            bool valid=usage==null;
            if(usage!=null){var device=InputSystem.devices.FirstOrDefault(d=>usage=="Head"?d is UnityEngine.InputSystem.XR.XRHMD:d.usages.Any(u=>u.ToString()==usage));var tracked=device?.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("isTracked");var state=device?.TryGetChildControl<UnityEngine.InputSystem.Controls.IntegerControl>("trackingState");valid=tracked!=null&&tracked.isPressed&&state!=null&&(state.ReadValue()&3)==3;}
            return new TrainingPose{position=V(t.position),rotation=Q(t.rotation),trackingValid=valid};
        }
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z}; static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
        void OnEnable() {if(initialized && Application.isPlaying)Start();}
        void OnDisable() {Shutdown();}
        void OnDestroy() {Shutdown();}
        void Shutdown()
        {
            if(Session==null)return;
            if(panel){panel.Command-=Command;panel.PageChanged-=PageChanged;}
            if(block){block.selectEntered.RemoveListener(Grab);block.selectExited.RemoveListener(Release);block.selectFilters.Remove(this);}
            foreach(var near in new[]{leftInteractor,rightInteractor})if(near){var gate=near.GetComponent<TrainingUIArbitration>();if(gate)gate.enabled=false;}
            Session.Record("PANEL_HIDDEN","training.panel."+(panel?panel.CurrentPage:"unknown"),"prototype_disabled",lastSnapshot,lastSnapshot,"System");if(Session.State==TrainingState.Running||Session.State==TrainingState.Paused)Session.Abort(lastSnapshot,"System");Session.Dispose();Session=null;
        }
    }
}
