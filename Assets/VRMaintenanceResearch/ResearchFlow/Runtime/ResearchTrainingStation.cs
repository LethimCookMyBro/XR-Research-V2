using System;
using System.Linq;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
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
        // Fixed-UI training (S1_TRAINING_FIXED_UI_20261015): same skills, each credited once from real state changes, ordered so the
        // trainee builds toward 'ready' instead of undoing and repeating it. The old order ended with module out / screw loose and
        // then asked for an uncredited second insert + latch + tighten before the 'ready' Inspect. Inspect is placed in sequence.
        public static readonly string[] FixedSequence20261015 = { "examine", "inspect_fail", "insert_wrong", "insert", "lock", "rotate_access",
            "fasten", "cable_connect", "inspect", "cable_disconnect", "loosen", "unlock" };
        // TrainingTrainerV2: Quest 28 Oct, a trainee seated the module the fitting way 7 times in 134 s while the step waited for a
        // blocked attempt. The fitting insert comes first; the blocked try then reverses what the trainee has just done.
        public static readonly string[] FixedSequence = { "examine", "inspect_fail", "insert", "insert_wrong", "lock", "rotate_access",
            "fasten", "cable_connect", "inspect", "cable_disconnect", "loosen", "unlock" };
        // Owner 29 Oct: correct insertion goes straight to the latch; wrong-way contact is feedback, not a required exercise.
        public static readonly string[] TrainerV3Sequence = { "examine", "inspect_fail", "insert", "lock", "rotate_access",
            "fasten", "cable_connect", "inspect", "cable_disconnect", "loosen", "unlock" };
        public static string[] SequenceFor(string trainingVersion) =>
            trainingVersion == ResearchUIPolicy.TrainingTrainerV3 ? TrainerV3Sequence :
            trainingVersion == ResearchUIPolicy.TrainingTrainerV2 ? FixedSequence : FixedSequence20261015;

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
            // The lead is drawn like the Task A cables. Moving only bone positions left each segment in its modelled zigzag
            // orientation: joints bent up to 168 deg off the next bone (measured 20 Oct), which read as a stiff kinked lead.
            var leadSkin = GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "VIS_TRAINER_FX1_LEAD_LOD0");
            Transform LeadEnd(string id) => leadSkin.bones.First(b => b.name == "B_TRAINER_FX1_LEAD_" + id);
            var lead = gameObject.AddComponent<ResearchS1Cable>();
            lead.assetId = "TRAINER_FX1_LEAD"; lead.skin = leadSkin; lead.sideways = true;
            lead.tubeMaterial = leadSkin.sharedMaterials.First(m => m.name == "M_PVC_Blue"); lead.tubeRadius = .003f; // its straight J02/J03 segments sit within 3.5 mm of their chord
            lead.spans = new[] { ResearchS1Cable.BindSpan(transform, leadSkin, LeadEnd("B1"), leadBones, LeadEnd("B2")) };
            ApplyLatch(false);
            BuildGripZones(lead.tubeMaterial);
            module.PoseAllowed = ModulePoseAllowed;
            leadHead.PoseAllowed = pose => Vector3.Distance(pose.GetColumn(3), leadAnchor.position) <= leadReach;
            foreach (var grabbable in new[] { module.grabbable, screw.grabbable, leadHead.grabbable, turnHandle.GetComponent<Grabbable>() })
                if (grabbable) grabbable.WhenPointerEventRaised += Pointer;
            if (latchInteractable is IInteractableView view) view.WhenStateChanged += LatchState;
        }

        void OnDestroy()
        {
            if (latchGraph.IsValid()) latchGraph.Destroy();
            foreach (var grabbable in new[] { module ? module.grabbable : null, screw ? screw.grabbable : null, leadHead ? leadHead.grabbable : null, turnHandle ? turnHandle.GetComponent<Grabbable>() : null })
                if (grabbable) grabbable.WhenPointerEventRaised -= Pointer;
            if (latchInteractable is IInteractableView view) view.WhenStateChanged -= LatchState;
        }

        // Quest 16 Oct 18:02: the lead and screw sit within reach of the module and took 9 grabs meant for it. Only the current
        // step's parts are grabbable; TutorialFact stays on the next uncredited step, so a held part never drops between credits.
        bool StepUses(GameObject root, string step)
        {
            var part = root.transform.parent;
            bool Module() => part == module.transform;
            bool Handle() => part == turnHandle.transform;
            switch (step)
            {
                case "insert_wrong": case "insert": return Module();
                case "lock": return root == latchInteractable.gameObject || Module(); // the blocked try leaves the module out
                case "unlock": return root == latchInteractable.gameObject || Module();
                case "rotate_access": return Handle();
                case "fasten": case "loosen": return part == screw.transform || Handle();
                case "cable_connect": case "cable_disconnect": return part == leadHead.transform;
                default: return false;
            }
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

        // AnimationClip.SampleAnimation only works in the Editor for non-legacy clips; Quest logged an error and the latch never moved.
        PlayableGraph latchGraph;
        AnimationClipPlayable latchPlayable;
        void ApplyLatch(bool closed)
        {
            LatchClosed = closed;
            if (!latchOpenClip || !latchAnimationRoot) return;
            if (!latchGraph.IsValid())
            {
                var animator = latchAnimationRoot.GetComponent<Animator>();
                if (!animator) animator = latchAnimationRoot.AddComponent<Animator>();
                latchPlayable = AnimationPlayableUtilities.PlayClip(animator, latchOpenClip, out latchGraph);
                latchGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                latchPlayable.Pause();
            }
            latchPlayable.SetTime(closed ? 0 : latchOpenClip.length);
            latchGraph.Evaluate();
        }

        void Update()
        {
            bool active = Active;
            string step = active ? controller.TutorialFact : null;
            foreach (var root in interactables)
            {
                bool on = active && StepUses(root, step);
                if (root && root.activeSelf != on) root.SetActive(on);
            }
            turnHandle.Allowed = screw.Allowed = active;
            foreach (var cue in gripCues) if (cue.activeSelf != (step == "rotate_access")) cue.SetActive(step == "rotate_access");
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

        // Quest 16 Oct 18:02: 97 s of module handling left no trace and no response. Each hold now logs grab, the first
        // measured reason it is not entering the slot, and the release outcome; the reason is shown as a neutral hint.
        // Runs after QAPlug.LateUpdate (order 10000) so a release reads the socket's settled state.
        const float NearSlotMm = 60;
        bool wasModuleHeld;
        string moduleHint;

        void ModuleAttempt()
        {
            string fact = controller.TutorialArmedFact;
            bool held = module.IsHeld;
            if (held && !wasModuleHeld) Log("TRAINING_MODULE", "Grabbed");
            if (held && (fact == "insert_wrong" || fact == "insert"))
            {
                string hint = ModuleHint(fact, out var measure);
                if (hint != null && hint != moduleHint)
                {
                    moduleHint = hint;
                    Log("TRAINING_MODULE", "Attempt", hint, measure);
                    controller.view?.TrainingHint("training.hint.module." + hint, fact);
                }
            }
            if (!held && wasModuleHeld)
            {
                bool inSlot = moduleSocket.Occupant == module;
                Log("TRAINING_MODULE", "Released", inSlot ? moduleSocket.State.ToString() : moduleHint ?? "away_from_slot");
                if (fact == "insert" && inSlot && moduleSocket.State == SocketState.Partial)
                    controller.view?.TrainingHint("training.hint.module.push", fact);
                moduleHint = null;
            }
            wasModuleHeld = held;
        }

        string ModuleHint(string fact, out MatePose hand)
        {
            hand = SocketRules.Measure(moduleSocket.seat.localToWorldMatrix, module.LastFreePose * module.MateInRoot(module.mates[0]));
            if (moduleSocket.Occupant == module && moduleSocket.Last.captured)
                return moduleSocket.Last.stop == Stop.Entrance ? "turned"
                    : moduleSocket.Last.stop == Stop.Seat ? (fact == "insert_wrong" ? "right_way" : null)
                    : fact == "insert" ? "wrong_way" : null;
            if (hand.lateralMm > NearSlotMm || Mathf.Abs(hand.depthMm) > NearSlotMm) return null;
            if (hand.tiltDeg > moduleSocket.spec.angTolDeg) return "tilted";
            return hand.lateralMm > moduleSocket.spec.posTolMm ? "off_center" : null;
        }

        void LateUpdate()
        {
            bool active = Active;
            if (active) { ModuleAttempt(); TurntableAttempt(); }
            ShowStatus(active ? controller.TutorialFact : null);
        }

        // Quest 28 Oct: the only turntable grab zone was an invisible 14x4x5 cm box beside the module. Two more zones at the front
        // corners (on the handle's rigidbody, so HandGrabInteractable.Start collects them) and a visible blue edge bar on all three.
        // They sit 16 cm from the back-right screw; the module is never grabbable in the steps that enable the handle.
        GameObject[] gripCues = Array.Empty<GameObject>();
        public (Bounds[] zones, float surfaceY) TurntableGripLayout()
        {
            Bounds InPivot(Bounds bounds, Transform space)
            {
                var matrix = turntable.worldToLocalMatrix * space.localToWorldMatrix;
                var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
                foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                    result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
                return result;
            }
            var body = turntable.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "VIS_TRAINER_FX1_BASE_LOD0");
            var station = InPivot(body.localBounds, body.transform);
            var box = turnHandle.GetComponent<BoxCollider>();
            var front = InPivot(new Bounds(box.center, box.size), box.transform);
            var scale = turntable.lossyScale;
            float inset = .035f / scale.x;
            var size = Divide(new Vector3(.07f, .05f, .06f), scale);
            var zones = new[] { front,
                new Bounds(new Vector3(station.min.x + inset, front.center.y, front.center.z), size),
                new Bounds(new Vector3(station.max.x - inset, front.center.y, front.center.z), size) };
            return (zones, station.max.y + .004f / scale.y);
        }

        void BuildGripZones(Material cueMaterial)
        {
            var layout = TurntableGripLayout();
            var handle = turnHandle.transform;
            foreach (var bounds in layout.zones.Skip(1))
            {
                var zone = new GameObject("TurntableRimGrip").transform;
                zone.SetParent(handle, false);
                zone.SetPositionAndRotation(turntable.TransformPoint(bounds.center), turntable.rotation);
                var box = zone.gameObject.AddComponent<BoxCollider>();
                box.size = Divide(Vector3.Scale(bounds.size, turntable.lossyScale), zone.lossyScale);
                box.isTrigger = true;
                if (box.bounds.Intersects(PartBounds(module.transform))) Debug.LogWarning("TRAINING_GRIP_ZONE overlaps module");
            }
            var cues = new System.Collections.Generic.List<GameObject>();
            foreach (var bounds in layout.zones)
            {
                var cue = GameObject.CreatePrimitive(PrimitiveType.Cube); cue.name = "TurntableGripCue";
                Destroy(cue.GetComponent<Collider>());
                cue.transform.SetParent(turntable, false);
                cue.transform.localPosition = new Vector3(bounds.center.x, layout.surfaceY, bounds.center.z);
                cue.transform.localScale = new Vector3(bounds.size.x, .008f / turntable.lossyScale.y, .008f / turntable.lossyScale.z);
                cue.GetComponent<Renderer>().sharedMaterial = cueMaterial;
                cue.SetActive(false); cues.Add(cue);
            }
            gripCues = cues.ToArray();
        }
        static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        // Quest 28 Oct: 147 s on rotate_access left no turntable trace at all.
        bool wasHandleHeld;
        void TurntableAttempt()
        {
            bool held = turnHandle.GetComponent<Grabbable>().SelectingPointsCount > 0;
            if (held != wasHandleHeld) controller.Log("TRAINING_TURNTABLE", "training.trainer", held ? "Grabbed" : "Released",
                after: new { turntable_yaw = turntable.localEulerAngles.y, turned_deg = TurnedDegrees }, inputSource: lastInput);
            wasHandleHeld = held;
        }
        float TurnedDegrees => Mathf.Abs(Mathf.DeltaAngle(startYaw, turntable.localEulerAngles.y));

        // Quest 28 Oct: the only feedback was on the instruction panel above the table while the trainee looked down at the part.
        // One short label beside the part in use states its measured condition (module stop, screw progress, turntable angle).
        TextMeshPro statusLabel;
        string statusKey, statusLanguage;

        void ShowStatus(string fact)
        {
            Transform anchor = null;
            string key = StatusKey(fact, ref anchor), text = null;
            if (key != null)
            {
                string language = controller.Language;
                text = ResearchStrings.Get("training.status." + key, language);
                if (key == "screw.fasten" || key == "screw.loosen") text = string.Format(text, Mathf.RoundToInt(screw.Fraction * 100));
                if (key == "turn.turning") text = string.Format(text, Mathf.RoundToInt(TurnedDegrees), Mathf.RoundToInt(accessYaw));
                if (!statusLabel) statusLabel = CreateStatusLabel();
                if (language != statusLanguage && controller.view)
                {
                    statusLanguage = language; statusLabel.font = controller.view.Font;
                    statusLabel.outlineColor = Color.white; statusLabel.outlineWidth = .18f;
                }
                statusLabel.text = text;
                var camera = controller.origin && controller.origin.Camera ? controller.origin.Camera.transform : null;
                var bounds = PartBounds(anchor);
                statusLabel.transform.position = new Vector3(bounds.center.x, bounds.max.y + .06f, bounds.center.z);
                if (camera) statusLabel.transform.rotation = Quaternion.LookRotation(statusLabel.transform.position - camera.position);
            }
            if (statusLabel && statusLabel.gameObject.activeSelf != (key != null)) statusLabel.gameObject.SetActive(key != null);
            if (key != statusKey)
            {
                statusKey = key;
                if (key != null) controller.Log("TRAINING_STATUS_SHOWN", "training." + fact, key, after: new { text }, source: "System");
            }
        }

        string StatusKey(string fact, ref Transform anchor)
        {
            switch (fact)
            {
                case "insert": case "insert_wrong": case "lock":
                    anchor = module.transform;
                    if (!module.IsHeld) return fact == "lock" || ModuleSeated ? null : "module.grab";
                    if (moduleSocket.Occupant == module && moduleSocket.Last.captured)
                        return moduleSocket.Last.stop == Stop.Block ? (fact == "insert_wrong" ? "module.blocked" : "module.wrong_way")
                            : moduleSocket.Last.stop == Stop.Entrance ? "module.turned"
                            : fact == "insert_wrong" ? "module.fits_reverse" : "module.right_way";
                    return moduleHint == "tilted" || moduleHint == "off_center" ? "module." + moduleHint : null;
                case "fasten": case "loosen":
                    anchor = screw.transform;
                    if (!screw.IsHeld) return "screw.grab." + fact;
                    if (fact == "fasten" && screw.Tight) return "screw.tight_held";
                    if (fact == "loosen" && screw.Loose) return "screw.loose_held";
                    // The direction shown is the measured motion, so twisting the wrong way reads as such.
                    return screw.Direction < 0 ? "screw.loosen" : screw.Direction > 0 ? "screw.fasten" : "screw.grab." + fact;
                case "rotate_access":
                    anchor = turnHandle.transform;
                    return wasHandleHeld ? "turn.turning" : "turn.grab";
                default: return null;
            }
        }

        static Bounds PartBounds(Transform part)
        {
            var renderers = part.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(part.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        TextMeshPro CreateStatusLabel()
        {
            var label = new GameObject("TrainingPartStatus").AddComponent<TextMeshPro>();
            label.transform.SetParent(transform, true);
            label.fontSize = .26f; // about 2.6 cm cap height, legible at the 0.5-0.9 m working distance
            label.color = new Color(.10f, .23f, .34f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(.6f, .08f);
            label.raycastTarget = false;
            return label;
        }

        // Dropped parts return to their authored pose; logged as System, never credited.
        float moduleIdleSince;
        void Recover()
        {
            // The kinematic plug head hangs from its fixed end: released unplugged, it returns to its rest pose.
            if (!leadHead.IsHeld && leadSocket.Occupant != leadHead && (Vector3.Distance(leadHead.transform.localPosition, leadRest.position) > .001f))
            {
                leadHead.transform.SetLocalPositionAndRotation(leadRest.position, leadRest.rotation);
                leadHead.body.position = leadHead.transform.position; leadHead.body.rotation = leadHead.transform.rotation;
                Log("TRAINING_LEAD", "ReturnedToRest");
            }
            if (!module.IsHeld && moduleSocket.Occupant != module)
            {
                bool fallen = module.transform.position.y < moduleHome.position.y - .25f;
                bool far = Vector3.Distance(module.transform.position, moduleHome.position) > 0.75f;
                bool slow = module.body && module.body.linearVelocity.sqrMagnitude < 0.04f;
                if (module.body && (fallen || (slow && far)))
                {
                    if (moduleIdleSince == 0) moduleIdleSince = Time.time;
                    else if (Time.time - moduleIdleSince > 3.0f || fallen)
                    {
                        moduleIdleSince = 0;
                        module.body.linearVelocity = Vector3.zero; module.body.angularVelocity = Vector3.zero;
                        module.transform.SetPositionAndRotation(moduleHome.position, moduleHome.rotation);
                        module.body.position = moduleHome.position; module.body.rotation = moduleHome.rotation;
                        controller?.Log("SYSTEM_OBJECT_RECOVERY", "training.trainer.module", "ResetToAuthored", source: "System", reason: fallen ? "dropped_below_table" : "idle_far_from_station");
                    }
                }
                else moduleIdleSince = 0;
            }
            else moduleIdleSince = 0;
        }

        void Credit(string fact) =>
            controller.TutorialActionCommitted(fact, controller.TutorialActionEpoch(fact), controller.Logger?.NewActionId(), lastInput);

        void Log(string evt, string result, string reason = null, MatePose? measure = null) =>
            controller.Log(evt, "training.trainer", result, after: new { module_seated = ModuleSeated, latch_closed = LatchClosed, screw_degrees = screw.Progress,
                lead_connected = LeadConnected, turntable_yaw = turntable.localEulerAngles.y, module_socket = moduleSocket.State.ToString(),
                module_stop = moduleSocket.Occupant == module ? moduleSocket.Last.stop.ToString() : null,
                module_slot_mm_deg = measure is MatePose m ? new[] { m.depthMm, m.lateralMm, m.tiltDeg, m.turnDeg } : null },
                inputSource: lastInput, reason: reason);

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
