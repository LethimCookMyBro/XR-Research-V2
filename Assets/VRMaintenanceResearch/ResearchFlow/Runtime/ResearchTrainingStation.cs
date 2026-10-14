using System;
using System.Linq;
using Oculus.Interaction;
using UnityEngine;
using VRMaintenanceResearch.AssetLibraryQA;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Neutral Trainer_FX1 practice for S1 §4 mechanisms (keyed insert, latch, screw, cable end, service turn, Examine, Inspect).
    // Credits only the armed instruction from the participant's own physical state change; it never moves a part for them.
    [DefaultExecutionOrder(12500)]
    public sealed class ResearchTrainingStation : MonoBehaviour
    {
        public static readonly string[] Facts = { "examine", "insert_wrong", "insert", "lock", "unlock", "rotate_access",
            "fasten", "loosen", "cable_connect", "cable_disconnect", "inspect_fail" };

        public ResearchSessionFlowController controller;
        public Transform turntable;
        public ResearchTrainingTurntable turnHandle;
        public QAPlug module;
        public QASocket moduleSocket;
        public Transform latch;
        public MonoBehaviour latchInteractable; // HandGrabInteractable without a pointable: Select toggles, nothing moves
        public GameObject latchAnimationRoot;
        public AnimationClip latchOpenClip;
        public ResearchTrainingScrew screw;
        public QAPlug leadHead;
        public QASocket leadSocket;
        public Transform leadAnchor;
        public Transform[] leadBones = Array.Empty<Transform>();
        public float leadReach = .2f, accessYaw = 120;
        public GameObject[] interactables = Array.Empty<GameObject>();

        public bool LatchClosed { get; private set; }
        public bool ModuleSeated => moduleSocket.Occupant == module && (moduleSocket.State == SocketState.Seated || moduleSocket.State == SocketState.Latched) && !module.IsHeld;
        public bool LeadConnected => leadSocket.Occupant == leadHead && (leadSocket.State == SocketState.Seated || leadSocket.State == SocketState.Latched) && !leadHead.IsHeld;
        public bool Ready => ModuleSeated && LatchClosed && screw.Tight;
        public bool Active => controller && controller.Flow?.IsTutorial == true && controller.Flow.TutorialCompleted == (int)TutorialStep.Inspect
            && controller.Flow.CanInteract && !controller.Transitioning && string.IsNullOrEmpty(controller.Error);

        Pose moduleHome, leadRest;
        float startYaw;
        bool wasSeated, wasConnected, wasBlocked, wasTight, wasLoose, unlockedWhileSeated;
        string lastInput = "None";

        void Awake()
        {
            moduleHome = new Pose(module.transform.position, module.transform.rotation);
            leadRest = new Pose(leadHead.transform.localPosition, leadHead.transform.localRotation);
            startYaw = turntable.localEulerAngles.y;
            ApplyLatch(false);
            module.PoseAllowed = ModulePoseAllowed;
            leadHead.PoseAllowed = pose => Vector3.Distance(pose.GetColumn(3), leadAnchor.position) <= leadReach;
            foreach (var grabbable in new[] { module.grabbable, screw.grabbable, leadHead.grabbable, turnHandle.GetComponent<Grabbable>() })
                if (grabbable) grabbable.WhenPointerEventRaised += Pointer;
            if (latchInteractable is IInteractableView view) view.WhenStateChanged += LatchState;
        }

        void OnDestroy()
        {
            foreach (var grabbable in new[] { module ? module.grabbable : null, screw ? screw.grabbable : null, leadHead ? leadHead.grabbable : null, turnHandle ? turnHandle.GetComponent<Grabbable>() : null })
                if (grabbable) grabbable.WhenPointerEventRaised -= Pointer;
            if (latchInteractable is IInteractableView view) view.WhenStateChanged -= LatchState;
        }

        // A latched module cannot leave its seat; any held pose other than the seated one is refused.
        bool ModulePoseAllowed(Matrix4x4 pose)
        {
            if (!LatchClosed || moduleSocket.Occupant != module) return true;
            return Vector3.Distance(pose.GetColumn(3), module.transform.position) < .0005f;
        }

        void Pointer(PointerEvent e)
        {
            if (!controller) return;
            if (e.Type == PointerEventType.Select) lastInput = controller.InputSourceForPointer(e.Identifier);
        }

        void LatchState(InteractableStateChangeArgs args)
        {
            if (args.NewState != InteractableState.Select || !Active) return;
            var interactor = (latchInteractable as IInteractableView)?.SelectingInteractorViews.FirstOrDefault();
            if (interactor != null) lastInput = controller.InputSourceForPointer(interactor.Identifier);
            bool close = !LatchClosed;
            if (close && !ModuleSeated && moduleSocket.Occupant == module)
            { Log("TRAINING_LATCH", "Blocked", "module_not_seated"); return; }
            ApplyLatch(close);
            Log("TRAINING_LATCH", close ? "Closed" : "Opened");
            if (!close && ModuleSeated) unlockedWhileSeated = true;
        }

        void ApplyLatch(bool closed)
        {
            LatchClosed = closed;
            if (latchOpenClip && latchAnimationRoot) latchOpenClip.SampleAnimation(latchAnimationRoot, closed ? 0 : latchOpenClip.length);
        }

        void Update()
        {
            bool active = Active;
            foreach (var root in interactables) if (root && root.activeSelf != active) root.SetActive(active);
            turnHandle.Allowed = screw.Allowed = active;
            if (!active) return;

            bool blocked = module.IsHeld && moduleSocket.Occupant == module && moduleSocket.Last.captured && moduleSocket.Last.stop == Stop.Block;
            bool seated = ModuleSeated, connected = LeadConnected;
            if (blocked && !wasBlocked) Log("TRAINING_MODULE", "BlockedWrongWay");
            if (seated != wasSeated) Log("TRAINING_MODULE", seated ? "Seated" : "Removed");
            if (connected != wasConnected) Log("TRAINING_LEAD", connected ? "Connected" : "Disconnected");
            if (screw.Tight != wasTight || screw.Loose != wasLoose) Log("TRAINING_SCREW", screw.Tight ? "Tight" : screw.Loose ? "Loose" : "Partial");
            wasBlocked = blocked; wasSeated = seated; wasConnected = connected; wasTight = screw.Tight; wasLoose = screw.Loose;

            switch (controller.TutorialArmedFact)
            {
                case "insert_wrong": if (blocked) Credit("insert_wrong"); break;
                case "insert": if (seated) Credit("insert"); break;
                case "lock": if (seated && LatchClosed) Credit("lock"); break;
                case "unlock": if (unlockedWhileSeated && moduleSocket.Occupant == null && !LatchClosed) Credit("unlock"); break;
                case "rotate_access": if (Mathf.Abs(Mathf.DeltaAngle(startYaw, turntable.localEulerAngles.y)) >= accessYaw) Credit("rotate_access"); break;
                case "fasten": if (screw.Tight && !screw.IsHeld) Credit("fasten"); break;
                case "loosen": if (screw.Loose && !screw.IsHeld) Credit("loosen"); break;
                case "cable_connect": if (connected) Credit("cable_connect"); break;
                case "cable_disconnect": if (leadSocket.Occupant == null && !leadHead.IsHeld) Credit("cable_disconnect"); break;
            }
            if (controller.TutorialArmedFact != "unlock" && !ModuleSeated) unlockedWhileSeated = false;
            Recover();
        }

        void LateUpdate()
        {
            if (leadBones.Length == 0 || !leadAnchor) return;
            // Bones between the fixed end and the held plug follow a sagging curve; the lead has no physics of its own.
            Vector3 a = leadAnchor.position, b = leadHead.transform.position;
            float sag = Mathf.Max(.01f, leadReach - Vector3.Distance(a, b)) * .5f;
            for (int i = 0; i < leadBones.Length; i++)
            {
                float t = (i + 1f) / (leadBones.Length + 1);
                leadBones[i].position = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4 * t * (1 - t));
            }
        }

        // Dropped parts return to their authored pose; logged as System, never credited.
        void Recover()
        {
            // The kinematic plug head hangs from its fixed end: released unplugged, it returns to its rest pose.
            if (!leadHead.IsHeld && leadSocket.Occupant != leadHead && (Vector3.Distance(leadHead.transform.localPosition, leadRest.position) > .001f))
            {
                leadHead.transform.SetLocalPositionAndRotation(leadRest.position, leadRest.rotation);
                leadHead.body.position = leadHead.transform.position; leadHead.body.rotation = leadHead.transform.rotation;
                Log("TRAINING_LEAD", "ReturnedToRest");
            }
            if (!module.IsHeld && moduleSocket.Occupant != module && module.transform.position.y < moduleHome.position.y - .3f)
            {
                module.body.linearVelocity = Vector3.zero; module.body.angularVelocity = Vector3.zero;
                module.transform.SetPositionAndRotation(moduleHome.position, moduleHome.rotation);
                module.body.position = moduleHome.position; module.body.rotation = moduleHome.rotation;
                controller.Log("SYSTEM_OBJECT_RECOVERY", "training.trainer.module", "ResetToAuthored", source: "System", reason: "dropped_below_table");
            }
        }

        void Credit(string fact) =>
            controller.TutorialActionCommitted(fact, controller.TutorialActionEpoch(fact), controller.Logger?.NewActionId(), lastInput);

        void Log(string evt, string result, string reason = null) =>
            controller.Log(evt, "training.trainer", result, after: new { module_seated = ModuleSeated, latch_closed = LatchClosed, screw_degrees = screw.Progress,
                lead_connected = LeadConnected, turntable_yaw = turntable.localEulerAngles.y }, inputSource: lastInput, reason: reason);

        // Tutorial Inspect on the trainer: which sample fact the press can credit, and the shown result key.
        public string InspectFact => Ready ? "inspect" : "inspect_fail";
        public string InspectResultKey => Ready ? "tutorial.result.trainer.pass" : "tutorial.result.trainer.fail";

        public string ExamineInfo(string target, string language) => ResearchStrings.Get("examine.info." + target, language);
        public void Examine(string target, string input)
        {
            controller.Log("EXAMINE", target, "Observed", source: "User", inputSource: input);
            lastInput = input;
            if (controller.TutorialArmedFact == "examine") Credit("examine");
        }
    }
}
