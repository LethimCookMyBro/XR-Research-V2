using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using HandSide = Oculus.Interaction.Input.Handedness;

namespace VRMaintenanceResearch.HandsLabQuest3.Runtime
{
    /// <summary>Lab-only runtime bridge. No research task/session controller or Training V2 changes.</summary>
    public sealed class HandsLabSession : MonoBehaviour
    {
        public Transform origin;
        [Tooltip("Provider tracking-space reference (XR Origin camera floor offset); never a visual offset.")]
        public Transform trackingSpace;
        Transform ProviderSpace => trackingSpace ? trackingSpace : origin;
        public Camera head;
        public HandsLabPanel panel;
        public HandsLabObject[] objects=Array.Empty<HandsLabObject>();
        public HandsLabInputGuard[] guards=Array.Empty<HandsLabInputGuard>();
        public Transform leftVisual,rightVisual;
        [Range(1,60)] public float sampleHz=20;
        public string logRoot="";
        public string executionEnvironment="Editor";
        [Tooltip("Editor test attribution only; ordinary interaction uses User")] public string actorSource="User";
        public HandsLabLogger Logger {get;private set;}
        public bool SystemChanging {get;private set;}
        readonly Dictionary<HandSide,ControllerSourceAttribution> controllerSources=new Dictionary<HandSide,ControllerSourceAttribution>();
        readonly Dictionary<int,HandsLabInputGuard> pointerGuards=new Dictionary<int,HandsLabInputGuard>();
        readonly Dictionary<string,Episode> episodes=new Dictionary<string,Episode>();
        readonly Dictionary<HandSide,ReleaseRequest> releases=new Dictionary<HandSide,ReleaseRequest>();
        readonly Dictionary<HandSide,GuardState> previousGuard=new Dictionary<HandSide,GuardState>();
        readonly Dictionary<int,UiPress> uiPresses=new Dictionary<int,UiPress>();
        readonly Dictionary<Button,Queue<int>> clicked=new Dictionary<Button,Queue<int>>();
        readonly List<XRHandSubsystem> handSubsystems=new List<XRHandSubsystem>();
        readonly Dictionary<int,DeviceObservation> deviceObservations=new Dictionary<int,DeviceObservation>();
        XRHandSubsystem handSubsystem;
        PoseSample leftHandProvider,rightHandProvider;
        double nextSample,nextSubsystemScan;
        bool started,wired,handlingWriteError;
        sealed class Episode {public string actionId;public HandsLabInputGuard guard;public object before;}
        sealed class ReleaseRequest {public string reason,actionId;}
        sealed class UiPress {public Button button;public HandsLabInputGuard guard;public string actionId;}
        sealed class GuardState {public string mode,reason;public bool tracking_valid,allowed,waiting;}
        sealed class DeviceObservation {public double providerTime,loggerTime;}

        void Start(){started=true;Initialize();}
        void OnEnable(){if(started)Initialize();}
        void Initialize()
        {
            if(wired)return;
            if(!origin||!head||!panel||guards.Length!=2||objects.Length==0)throw new InvalidOperationException("HandsLabSession requires existing rig, panel, two guards and objects");
            if(objects.Select(o=>o.stableId).Distinct().Count()!=objects.Length)throw new InvalidOperationException("HandsLab object IDs must be unique");
            foreach(var item in objects)item.Initialize(this);
            foreach(var guard in guards){guard.SystemRelease+=GuardSystemRelease;guard.StateChanged+=GuardStateChanged;}
            panel.TestButton.onClick.AddListener(ClickedTest);panel.ResetButton.onClick.AddListener(ClickedReset);panel.NewRunButton.onClick.AddListener(ClickedNewRun);
            PointableCanvasModule.WhenSelected+=UiSelected;PointableCanvasModule.WhenUnselected+=UiUnselected;
            wired=true;RefreshPointers();BeginLogger();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(!GetComponent<HandsLabGrabProbe>())gameObject.AddComponent<HandsLabGrabProbe>().Initialize(this);
#endif
            if(Logger.State==HandsLabLogState.Active){SystemChanging=false;foreach(var guard in guards)guard.Resume();}
        }
        void BeginLogger()
        {
            leftHandProvider=null;rightHandProvider=null;nextSample=0;nextSubsystemScan=0;
            episodes.Clear();releases.Clear();previousGuard.Clear();uiPresses.Clear();clicked.Clear();deviceObservations.Clear();
            Logger=new HandsLabLogger(string.IsNullOrWhiteSpace(logRoot)?Path.Combine(Application.persistentDataPath,"HandsLabQuest3_TechnicalTests"):logRoot,
                "None",Application.isEditor?"Unity Editor / UnityXR":"UnityXR / OpenXR",sampleHz,
                Application.platform==RuntimePlatform.Android?"QuestStandalone":executionEnvironment);
            Logger.WriteFailed+=WriteError;
            panel.SetInteractable(true);panel.SetFeedback("พร้อมทดสอบการจับ หมุน และกดปุ่ม");
            if(Logger.State==HandsLabLogState.WriteError){WriteError(Logger.LastError);return;}
            Logger.Record("LAB_READY","handslab","Ready","Active","Both",null,CaptureState(),"System",inputSourceOverride:CurrentSource());
            foreach(var guard in guards)GuardStateChanged(guard.side,"run_started");
        }
        object CaptureState()=>new { objects=objects.Select(o=>o.CaptureSnapshot()).ToArray(),guards=guards.Select(CaptureGuard).ToArray() };
        static GuardState CaptureGuard(HandsLabInputGuard g)=>new GuardState {mode=g.Mode.ToString(),tracking_valid=g.TrackingValid,allowed=g.InteractionsAllowed,waiting=g.WaitingForNeutral,reason=g.LastReason};
        void RefreshPointers()
        {
            // SDK identifiers are assigned in Awake. Inactive modality roots may not have
            // awakened yet; discover actual active pointers lazily when their callbacks arrive.
            // Keep previous mappings through deactivation so cancellation retains its hand.
            foreach(var guard in guards)if(guard.interactionsRoot)
                foreach(var behaviour in guard.interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true))
                    if(behaviour.isActiveAndEnabled && behaviour is IInteractor interactor && !(behaviour is InteractorGroup))
                        pointerGuards[interactor.Identifier]=guard;
        }
        HandsLabInputGuard GuardFor(int pointer)
        {
            if(!pointerGuards.TryGetValue(pointer,out var guard)){RefreshPointers();pointerGuards.TryGetValue(pointer,out guard);}return guard;
        }
        string Source(HandsLabInputGuard guard)
        {
            if(!guard)return "None";
            if(guard.Mode==HandsLabInputGuard.InputMode.TrackedHand)return "HandTracking";
            if(guard.Mode!=HandsLabInputGuard.InputMode.Controller)return "None";
            if(!controllerSources.TryGetValue(guard.side,out var attribution))
                controllerSources[guard.side]=attribution=new ControllerSourceAttribution();
            var node=guard.side==HandSide.Left?XRNode.LeftHand:XRNode.RightHand;
            bool simulated=SimulatedDevice(node)!=null;
            bool physical=InputDevices.GetDeviceAtXRNode(node).isValid;
            return attribution.Observe(simulated,physical);
        }
        static bool IsSimulated(UnityEngine.InputSystem.InputDevice d)=>d.layout.StartsWith("XRSimulated",StringComparison.Ordinal)||d.layout.StartsWith("HandsLabSimulated",StringComparison.Ordinal)||d.name.StartsWith("HandsLabSimulated",StringComparison.Ordinal);
        static UnityEngine.InputSystem.InputDevice SimulatedDevice(XRNode node)=>UnityEngine.InputSystem.InputSystem.devices.FirstOrDefault(d=>IsSimulated(d)&&
            (node==XRNode.Head?d is UnityEngine.InputSystem.XR.XRHMD:d.usages.Any(u=>u.ToString()==(node==XRNode.LeftHand?"LeftHand":"RightHand"))));
        string CurrentSource()
        {
            var sources=guards.Select(Source).Where(s=>s!="None").Distinct().ToArray();
            return sources.Length==1?sources[0]:"None"; // Per-hand events/poses retain exact mode when mixed.
        }
        static string Hand(HandsLabInputGuard guard)=>guard?guard.side.ToString():"None";
        static string EpisodeKey(HandsLabObject item,int pointer)=>item.stableId+":"+pointer;

        internal bool IsSystemRelease(HandsLabObject item,PointerEvent evt)
        {
            var guard=GuardFor(evt.Identifier);
            return evt.Type==PointerEventType.Cancel||SystemChanging||!string.IsNullOrEmpty(item.SystemReason)||(guard&&guard.SystemCancelling);
        }

        public void ObjectPointer(HandsLabObject item,PointerEvent evt,object before,object after)
        {
            if(Logger==null)return;
            string key=EpisodeKey(item,evt.Identifier);var guard=GuardFor(evt.Identifier);
            if(evt.Type==PointerEventType.Select)
            {
                if(episodes.ContainsKey(key))return;
                var episode=new Episode{actionId=Logger.NewActionId(),guard=guard,before=before};episodes[key]=episode;
                if(guard)guard.RegisterHeldBody(item.body);
                Logger.Record("GRAB",item.stableId,"Selected","Active",Hand(guard),before,after,guard?actorSource:"System",
                    guard?"":"pointer_identity_unresolved",episode.actionId,Source(guard),guard?(bool?)guard.TrackingValid:null);
                return;
            }
            if(!episodes.TryGetValue(key,out var held))return;
            episodes.Remove(key);guard=held.guard?held.guard:guard;
            if(guard)guard.UnregisterHeldBody(item.body);
            bool system=IsSystemRelease(item,evt);
            releases.TryGetValue(guard?guard.side:default,out var request);
            string reason=system?(item.SystemReason??request?.reason??"sdk_pointer_cancel"):"participant_unselect";
            string action=item.SystemActionId??(system?request?.actionId:null)??held.actionId;
            Logger.Record(system?"SYSTEM_RELEASE":"RELEASE",item.stableId,"Released","Active",Hand(guard),before,after,system?"System":actorSource,reason,action,Source(guard),guard?(bool?)guard.TrackingValid:null);
        }
        public void ResidualSystemRelease(HandsLabObject item,int pointer,object before,object after,string reason,string actionId)
        {
            string key=EpisodeKey(item,pointer);var guard=GuardFor(pointer);episodes.Remove(key);if(guard)guard.UnregisterHeldBody(item.body);
            Logger?.Record("SYSTEM_RELEASE",item.stableId,"Released","Active",Hand(guard),before,after,"System",reason+":residual_sdk_cleanup",actionId,Source(guard),guard?(bool?)guard.TrackingValid:null);
        }
        public void ObjectRotated(HandsLabObject item,int pointer,object after)
        {
            if(Logger==null||!episodes.TryGetValue(EpisodeKey(item,pointer),out var episode))return;
            Logger.Record("ROTATE",item.stableId,"RotationChanged","Active",Hand(episode.guard),episode.before,after,actorSource,"threshold_degrees="+item.rotationEventDegrees,
                episode.actionId,Source(episode.guard),episode.guard?(bool?)episode.guard.TrackingValid:null);
        }
        void GuardSystemRelease(HandSide side,string reason)
        {
            if(Logger==null)return;
            releases[side]=new ReleaseRequest{reason=reason,actionId=Logger.NewActionId()};
        }
        void GuardStateChanged(HandSide side,string reason)
        {
            if(Logger==null)return;var guard=guards.First(g=>g.side==side);var after=CaptureGuard(guard);previousGuard.TryGetValue(side,out var before);
            string evt=before==null||before.mode!=after.mode?"MODE_CHANGED":before.tracking_valid!=after.tracking_valid?"TRACKING_CHANGED":"INPUT_GATE_CHANGED";
            Logger.Record(evt,"handslab."+side.ToString().ToLowerInvariant(),reason,"Active",side.ToString(),before,after,"System",reason,
                releases.TryGetValue(side,out var request)?request.actionId:null,Source(guard),guard.TrackingValid);
            previousGuard[side]=after;
            panel.SetMode(guards.All(g=>g.Mode==HandsLabInputGuard.InputMode.Controller)?"\u0e43\u0e0a\u0e49\u0e08\u0e2d\u0e22":guards.All(g=>g.Mode==HandsLabInputGuard.InputMode.TrackedHand)?"\u0e43\u0e0a\u0e49\u0e21\u0e37\u0e2d":guards.Any(g=>g.TrackingValid)?"\u0e23\u0e2d\u0e2d\u0e35\u0e01\u0e02\u0e49\u0e32\u0e07":"\u0e23\u0e2d\u0e01\u0e32\u0e23\u0e15\u0e34\u0e14\u0e15\u0e32\u0e21");
            if(evt=="MODE_CHANGED")Logger.Record("DEVICE_CAPABILITIES","handslab."+side.ToString().ToLowerInvariant(),"Observed","Active",side.ToString(),null,Capabilities(side),"System",
                "raw availability/value only; generic thumbTouch is not a thumbrest-specific measurement",inputSourceOverride:Source(guard),trackingValid:guard.TrackingValid);
            if(guard.InteractionsAllowed)releases.Remove(side);
        }
        object Capabilities(HandSide side)
        {
            var node=side==HandSide.Left?XRNode.LeftHand:XRNode.RightHand;var device=InputDevices.GetDeviceAtXRNode(node);var simulated=SimulatedDevice(node);
            var hands=new List<XRHandSubsystem>();SubsystemManager.GetSubsystems(hands);
            object Feature(InputFeatureUsage<bool> usage){bool available=device.isValid&&device.TryGetFeatureValue(usage,out _);bool value=false;if(available)device.TryGetFeatureValue(usage,out value);return new{available,value=available?(bool?)value:null};}
            object FloatFeature(string usageName){float value=0;bool available=device.isValid&&device.TryGetFeatureValue(new InputFeatureUsage<float>(usageName),out value);return new{available,value=available?(float?)value:null};}
            object Control(string name){var c=simulated?.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(name);return new{available=c!=null,value=c!=null?(bool?)c.isPressed:null};}
            return new {provider_device_valid=device.isValid,inputsystem_simulated_layout=simulated?.layout,
                provider_name=device.isValid?device.name:null,provider_characteristics=device.isValid?device.characteristics.ToString():null,
                isTracked=Feature(CommonUsages.isTracked),tracking_state=device.TryGetFeatureValue(CommonUsages.trackingState,out var trackingState)?trackingState.ToString():null,
                grip=FloatFeature(CommonUsages.grip.name),trigger=FloatFeature(CommonUsages.trigger.name),
                hand_subsystems=hands.Select(h=>new{id=h.subsystemDescriptor.id,h.running,left_tracked=h.leftHand.isTracked,right_tracked=h.rightHand.isTracked}).ToArray(),
                primaryTouch=Feature(CommonUsages.primaryTouch),secondaryTouch=Feature(CommonUsages.secondaryTouch),thumbTouch=FloatFeature("ThumbTouch"),indexTouch=FloatFeature("IndexTouch"),
                simulatedPrimaryTouched=Control("primaryTouched"),simulatedSecondaryTouched=Control("secondaryTouched"),simulatedThumbrestTouched=Control("thumbrestTouched"),simulatedTriggerTouched=Control("triggerTouched"),
                default_visual_index_driver="analog_trigger",system_capsense_supported=false};
        }

        void ClickedTest()=>QueueClick(panel.TestButton);
        void ClickedReset()=>QueueClick(panel.ResetButton);
        void ClickedNewRun()=>QueueClick(panel.NewRunButton);
        void QueueClick(Button button)
        {
#if UNITY_EDITOR
            Debug.Log("HANDSLAB_UI_CALLBACK "+button.name+" frame="+Time.frameCount);
#endif
            if(!clicked.TryGetValue(button,out var queue)){queue=new Queue<int>();clicked[button]=queue;}queue.Enqueue(Time.frameCount);
        }
        void UiSelected(PointableCanvasEventArgs args)
        {
            if(Logger==null||args.Canvas!=panel.Canvas||!args.PointerId.HasValue||!args.Hovered)return;
            var button=args.Hovered.GetComponentInParent<Button>();if(!button)return;var guard=GuardFor(args.PointerId.Value);
            uiPresses[args.PointerId.Value]=new UiPress{button=button,guard=guard,actionId=Logger.NewActionId()};
            Logger.Record("UI_SELECT","handslab.ui."+button.name,"Down","Active",Hand(guard),null,null,actorSource,actionId:uiPresses[args.PointerId.Value].actionId,inputSourceOverride:Source(guard),trackingValid:guard?(bool?)guard.TrackingValid:null);
        }
        void UiUnselected(PointableCanvasEventArgs args)
        {
            if(Logger==null||args.Canvas!=panel.Canvas||!args.PointerId.HasValue||!uiPresses.TryGetValue(args.PointerId.Value,out var press))return;
            uiPresses.Remove(args.PointerId.Value);
            // Navigation.None leaves SDK release Hovered null; actual Button.onClick was dispatched synchronously for this pointer before WhenUnselected.
            bool actualClick=clicked.TryGetValue(press.button,out var queue)&&queue.Count>0&&queue.Peek()==Time.frameCount;
            #if UNITY_EDITOR
            Debug.Log("HANDSLAB_UI_RELEASE "+press.button.name+" actualClick="+actualClick+" frame="+Time.frameCount+" pending="+(clicked.TryGetValue(press.button,out var diagnosticQueue)?string.Join(",",diagnosticQueue):"none"));
#endif
            if(actualClick)clicked[press.button].Dequeue();
            if(!Logger.Record("UI_UNSELECT","handslab.ui."+press.button.name,actualClick?"Clicked":"ReleasedWithoutClick","Active",Hand(press.guard),null,null,
                actorSource,actionId:press.actionId,inputSourceOverride:Source(press.guard),trackingValid:press.guard?(bool?)press.guard.TrackingValid:null))return;
            if(!actualClick)return;
            if(!press.guard||!press.guard.InteractionsAllowed||press.guard.SystemCancelling)return;
            if(!Logger.Record("PRESS","handslab.ui."+press.button.name,"Clicked","Active",Hand(press.guard),null,null,actorSource,
                actionId:press.actionId,inputSourceOverride:Source(press.guard),trackingValid:press.guard.TrackingValid))return;
            if(press.button==panel.TestButton)panel.SetFeedback("รับคำสั่งกดปุ่มแล้ว");
            else if(press.button==panel.ResetButton)ResetObjects();
            else if(press.button==panel.NewRunButton)NewRun();
        }

        void SuspendAll(string reason,string action)
        {
            SystemChanging=true;
            foreach(var guard in guards)guard.Suspend(reason);
            foreach(var item in objects)item.DetachForSystem(reason,action);
        }
        public void ResetObjects()
        {
            if(Logger==null||Logger.State!=HandsLabLogState.Active)return;
            string action=Logger.NewActionId();SuspendAll("explicit_reset",action);
            if(Logger.State!=HandsLabLogState.Active)return;
            foreach(var item in objects)
            {
                var before=item.CaptureSnapshot();item.ResetToAuthored();
                if(!Logger.Record("RESET",item.stableId,"AuthoredPose","Active","None",before,item.CaptureSnapshot(),"System","explicit_reset",action,"None"))return;
            }
            Physics.SyncTransforms();SystemChanging=false;foreach(var guard in guards)guard.Resume();panel.SetFeedback("คืนตำแหน่งแล้ว ปล่อยปุ่มหรือคลายนิ้วก่อนเริ่มใหม่");
        }
        public bool CompleteRun()
        {
            if(Logger==null||Logger.State!=HandsLabLogState.Active)return false;
            SuspendAll("run_completed",Logger.NewActionId());
            return Logger.Complete("explicit_new_run_boundary","System",CurrentSource());
        }
        public void NewRun()
        {
            if(!CompleteRun())return;
            Logger.Dispose();BeginLogger();if(Logger.State!=HandsLabLogState.Active)return;
            ResetObjects();
        }
        void WriteError(string message)
        {
            if(handlingWriteError)return;handlingWriteError=true;
            try
            {
                Debug.LogError("HANDSLAB_LOG_ERROR: "+message);
                SuspendAll("logging_error",Logger?.NewActionId()??Guid.NewGuid().ToString("N"));
                panel.SetFeedback("การบันทึกขัดข้อง หยุดการทดสอบและแจ้งผู้ดูแล");panel.SetInteractable(false);
            }
            finally{handlingWriteError=false;}
        }

        void Update()
        {
            if(Logger==null||Logger.State!=HandsLabLogState.Active)return;
            foreach(var queue in clicked.Values)while(queue.Count>0&&queue.Peek()<Time.frameCount)queue.Dequeue();
            if(Logger.Timestamp>=nextSubsystemScan){nextSubsystemScan=Logger.Timestamp+1;FindHandSubsystem();}
            if(Logger.Timestamp<nextSample)return;nextSample=Logger.Timestamp+1.0/sampleHz;
            var left=guards.First(g=>g.side==HandSide.Left);var right=guards.First(g=>g.side==HandSide.Right);
            var frame=new HandsLabFrame {taskState=SystemChanging?"Suspended":"Active",inputSource=CurrentSource(),head=ReadDevice(XRNode.Head),origin=VisualPose(origin,null,"XR Origin authored reference"),
                leftProvider=Provider(left),rightProvider=Provider(right),leftVisual=VisualPose(leftVisual,left,"SyntheticVisual"),rightVisual=VisualPose(rightVisual,right,"SyntheticVisual")};
            Logger.Sample(frame,actorSource=="Tester"?"Tester":"System");
        }
        PoseSample Provider(HandsLabInputGuard guard)
        {
            string source=Source(guard);
            if(!guard.TrackingValid)return new PoseSample{reason=guard.LastReason,provider="provider unavailable",inputSource=source};
            var sample=guard.Mode==HandsLabInputGuard.InputMode.TrackedHand?(guard.side==HandSide.Left?leftHandProvider:rightHandProvider):
                guard.Mode==HandsLabInputGuard.InputMode.Controller?ReadDevice(guard.side==HandSide.Left?XRNode.LeftHand:XRNode.RightHand):null;
            if(sample==null)return new PoseSample{inputSource=source,reason="provider_not_updated"};sample.inputSource=source;return sample;
        }
        PoseSample ReadDevice(XRNode node)
        {
            var simulated=SimulatedDevice(node);if(simulated!=null)return ReadSimulatedDevice(simulated);
            var device=InputDevices.GetDeviceAtXRNode(node);var sample=new PoseSample{provider="UnityXR.InputDevices."+node,reason="provider_tracking_invalid"};
            if(!device.isValid||!device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)||!tracked||
                !device.TryGetFeatureValue(CommonUsages.trackingState,out InputTrackingState state)||(state&(InputTrackingState.Position|InputTrackingState.Rotation))!=(InputTrackingState.Position|InputTrackingState.Rotation)||
                !device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 position)||!device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion rotation))return sample;
            sample.position=V(ProviderSpace.TransformPoint(position));sample.rotation=Q(ProviderSpace.rotation*rotation);sample.trackingValid=true;sample.sampleTimestamp=Logger.Timestamp;sample.reason="provider_query_no_native_sample_timestamp";return sample;
        }
        PoseSample ReadSimulatedDevice(UnityEngine.InputSystem.InputDevice device)
        {
            var sample=new PoseSample{provider="InputSystem."+device.layout,inputSource="SimulatedController",reason="simulated_provider_tracking_invalid"};
            Vector3 position;Quaternion rotation;var aggregate=device.TryGetChildControl<UnityEngine.InputSystem.XR.PoseControl>("devicePose");
            if(aggregate!=null)
            {
                var pose=aggregate.ReadValue();
                if(!pose.isTracked||(pose.trackingState&(InputTrackingState.Position|InputTrackingState.Rotation))!=(InputTrackingState.Position|InputTrackingState.Rotation))return sample;
                position=pose.position;rotation=pose.rotation;
            }
            else
            {
                var tracked=device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("isTracked");var state=device.TryGetChildControl<UnityEngine.InputSystem.Controls.IntegerControl>("trackingState");
                var pos=device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("devicePosition");var rot=device.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("deviceRotation");
                if(tracked==null||!tracked.isPressed||state==null||(state.ReadValue()&3)!=3||pos==null||rot==null)return sample;
                position=pos.ReadValue();rotation=rot.ReadValue();
            }
            if(!deviceObservations.TryGetValue(device.deviceId,out var observation)||observation.providerTime!=device.lastUpdateTime)
            {observation=new DeviceObservation{providerTime=device.lastUpdateTime,loggerTime=Logger.Timestamp};deviceObservations[device.deviceId]=observation;}
            sample.position=V(ProviderSpace.TransformPoint(position));sample.rotation=Q(ProviderSpace.rotation*rotation);sample.trackingValid=true;sample.sampleTimestamp=observation.loggerTime;sample.reason="simulated_input_state_event";return sample;
        }
        PoseSample VisualPose(Transform value,HandsLabInputGuard guard,string provider)
        {
            if(!value||!value.gameObject.activeInHierarchy||(guard&&!guard.TrackingValid))return new PoseSample{provider=provider,reason="visual_not_current",inputSource=Source(guard)};
            return new PoseSample{provider=provider,position=V(value.position),rotation=Q(value.rotation),trackingValid=true,sampleTimestamp=Logger.Timestamp,inputSource=Source(guard)};
        }
        void FindHandSubsystem()
        {
            if(handSubsystem!=null&&handSubsystem.running)return;
            if(handSubsystem!=null)handSubsystem.updatedHands-=HandsUpdated;
            handSubsystem=null;leftHandProvider=null;rightHandProvider=null;SubsystemManager.GetSubsystems(handSubsystems);
            handSubsystem=handSubsystems.FirstOrDefault(s=>s.running);if(handSubsystem!=null)handSubsystem.updatedHands+=HandsUpdated;
        }
        void HandsUpdated(XRHandSubsystem subsystem,XRHandSubsystem.UpdateSuccessFlags flags,XRHandSubsystem.UpdateType updateType)
        {
            if(Logger==null)return;
            leftHandProvider=ReadHand(subsystem.leftHand,(flags&XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints)!=0);
            rightHandProvider=ReadHand(subsystem.rightHand,(flags&XRHandSubsystem.UpdateSuccessFlags.RightHandJoints)!=0);
        }
        PoseSample ReadHand(XRHand hand,bool updated)
        {
            var sample=new PoseSample{provider="XRHandSubsystem.Wrist",reason="hand_update_invalid"};
            if(!updated||!hand.isTracked||!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))return sample;
            sample.position=V(ProviderSpace.TransformPoint(wrist.position));sample.rotation=Q(ProviderSpace.rotation*wrist.rotation);sample.trackingValid=true;sample.sampleTimestamp=Logger.Timestamp;sample.reason="";return sample;
        }
        void OnDisable(){Shutdown();}
        void OnDestroy(){Shutdown();}
        void Shutdown()
        {
            if(!wired)return;
            if(Logger!=null&&Logger.State==HandsLabLogState.Active)SuspendAll("lab_disabled",Logger.NewActionId());
            foreach(var guard in guards){guard.SystemRelease-=GuardSystemRelease;guard.StateChanged-=GuardStateChanged;}
            foreach(var item in objects)if(item)item.Unbind();
            panel.TestButton.onClick.RemoveListener(ClickedTest);panel.ResetButton.onClick.RemoveListener(ClickedReset);panel.NewRunButton.onClick.RemoveListener(ClickedNewRun);
            PointableCanvasModule.WhenSelected-=UiSelected;PointableCanvasModule.WhenUnselected-=UiUnselected;
            if(handSubsystem!=null)handSubsystem.updatedHands-=HandsUpdated;handSubsystem=null;
            Logger?.Dispose();Logger=null;wired=false;
        }
        static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
    }
}
