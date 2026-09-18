#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRMaintenanceResearch.TrainingV2;

namespace VRTrainingV2.Editor
{
    /// <summary>Editor test driver: keyboard/mouse -> real simulator -> simulated XR device -> XRI.
    /// Never changes tracked/workpiece poses or invokes interaction/UI commands directly.
    /// The separately labeled panel-front fixture temporarily repositions only the panel, then restores it.</summary>
    public static class TrainingSimulatorDriver
    {
        static TrainingPrototype p;
        static XRInteractionSimulator simulator;
        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static readonly List<object> evidence = new List<object>();
        static readonly List<object> inputs = new List<object>();
        static string previousActor, stage;
        static bool holdingGrip, leftQuickGripOn, fixtureOnly;
        static int previousFrame;
        static double started;
        static Vector3 uiHandPosition;
        public static bool IsRunning { get; private set; }

        public static void Begin() => BeginRun(false);
        public static void BeginFixtureOnly() => BeginRun(true);

        static string OutputPrefix => fixtureOnly ? "input_fixture" : "input_driver";

        static void BeginRun(bool onlyFixture)
        {
            if (IsRunning) throw new InvalidOperationException("Simulator driver already running");
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Begin requires existing Play Mode");
            p = UnityEngine.Object.FindFirstObjectByType<TrainingPrototype>();
            simulator = UnityEngine.Object.FindFirstObjectByType<XRInteractionSimulator>();
            if (!p || p.Session == null || !simulator || Keyboard.current == null || Mouse.current == null)
                throw new InvalidOperationException("Training, live simulator, Keyboard and Mouse required");
            if (!onlyFixture && p.Session.State != TrainingState.Ready)
                throw new InvalidOperationException("Fresh Ready session required; leave/re-enter Play Mode intentionally");
            if (onlyFixture && (p.block.isSelected || p.Session.State == TrainingState.Paused || p.Session.State == TrainingState.WriteError))
                throw new InvalidOperationException("Fixture-only requires unheld Ready/Running/Passed/Aborted state");
            fixtureOnly = onlyFixture;
            var gameView = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            previousActor = p.actorSource;
            p.actorSource = "Tester";
            started = EditorApplication.timeSinceStartup;
            previousFrame = -1;
            holdingGrip = false;
            leftQuickGripOn = false;
            inputs.Clear(); evidence.Clear(); stack.Clear();
            IsRunning = true;
            SetStage("initializing");
            SendKeys(); SendMouse(Vector2.zero, false);
            stack.Push(onlyFixture ? FixtureRun() : Run());
            EditorApplication.update += Tick;
        }

        public static void Stop() => Finish(false, "Stopped explicitly by test operator");

        static IEnumerator FixtureRun()
        {
            yield return Frames(12);
            uiHandPosition = p.right.position;
            yield return FrontPanelFixture();
        }

        static void Tick()
        {
            if (!IsRunning) return;
            try
            {
                if (!EditorApplication.isPlaying || !p || !simulator)
                    throw new InvalidOperationException("Play Mode / test objects ended during input run");
                if (EditorApplication.timeSinceStartup - started > 420)
                    throw new TimeoutException("420 second global input-driver timeout");
                if (previousFrame == Time.frameCount) return;
                previousFrame = Time.frameCount;
                while (stack.Count > 0)
                {
                    var next = stack.Peek();
                    if (!next.MoveNext()) { stack.Pop(); continue; }
                    if (next.Current is IEnumerator child) { stack.Push(child); continue; }
                    return;
                }
                Finish(true, "Input-driven cycle finished; see assertions and disk log, not hardware validation");
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        static IEnumerator Run()
        {
            yield return Frames(90);
            // Camera pose is changed only by the simulator's HMD input route, never its Transform.
            SendKeys(Key.H); yield return Frames(2); SendKeys(); yield return Frames(3);
            yield return Rotate(p.head.rotation * Quaternion.Euler(14f, 0f, 0f), false, p.head);
            SendKeys(Key.H); yield return Frames(2); SendKeys(); yield return Frames(3);
            uiHandPosition = p.right.position;
            yield return SelectRight();
            yield return Mark("initial_hands", true);
            yield return Click("start");
            Require(p.Session.State == TrainingState.Running, "Start UI enters Running");
            yield return Mark("main", true);
            yield return Click("inspect");
            Require(p.Session.State == TrainingState.Running && p.Session.InspectCount == 1,
                "Empty-target Inspect fails once and keeps trial Running");
            yield return Mark("fail_empty", true);
            yield return Click("back");
            var before = p.Snapshot();
            yield return Click("manual_open");
            yield return Mark("manual", true);
            yield return Click("manual_next");
            Require(p.panel.ManualPage == 1, "Manual next page");
            yield return Click("manual_previous");
            Require(p.panel.ManualPage == 0, "Manual previous page");
            yield return Click("manual_close");
            yield return Click("examine");
            Require(p.panel.CurrentPage == "examine", "Examine UI opened");
            var after = p.Snapshot();
            Require(Vector3.Distance(V(before.position), V(after.position)) < .004f &&
                Quaternion.Angle(Q(before.rotation), Q(after.rotation)) < .5f,
                "Manual and Examine do not alter settled object pose");
            yield return Mark("examine", true);
            yield return Click("back");

            // Wrong orientation is a real physical placement, not a scripted validator input.
            yield return Grab();
            yield return Move(() => p.block.transform.position, p.block.transform.position + Vector3.up * .2f, .035f);
            yield return OrientHeld(p.targetPad.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
            yield return PlaceHeld();
            Require(!p.block.isSelected, "Participant-input release ends hold");
            yield return Move(() => p.right.position, uiHandPosition, .04f);
            yield return Click("inspect");
            Require(p.Session.State == TrainingState.Running && p.Session.InspectCount == 2,
                "Wrong orientation Inspect fails once and keeps same trial");
            yield return Mark("fail_wrong_orientation", true);
            yield return Click("back");
            yield return Grab();
            yield return Move(() => p.block.transform.position, p.block.transform.position + Vector3.up * .2f, .035f);
            yield return OrientHeld(p.targetPad.transform.rotation);
            yield return PlaceHeld();
            var ready = p.Snapshot();
            Require(!ready.held && ready.insideTarget && ready.supported &&
                ready.angleErrorDeg <= p.prototypeRules.angleToleranceDeg,
                "Correct placement is released, fully contained, supported and aligned");
            yield return Move(() => p.right.position, uiHandPosition, .04f);
            yield return Click("inspect");
            Require(p.Session.State == TrainingState.Passed && p.Session.InspectCount == 3 && p.Session.RetryCount == 2,
                "Fail Fail Pass = 3 Inspect, 2 retry, one Passed trial");
            yield return Mark("pass", true);
            string previousTrial = p.Session.TrialId;
            yield return Frames(30);
            Require(p.Session.InspectCount == 3 && p.Session.State == TrainingState.Passed, "No double completion while result visible");
            yield return Click("new_trial");
            Require(p.Session.TrialId != previousTrial && p.Session.InspectCount == 0 && p.Session.State == TrainingState.Running,
                "New round is explicit and has isolated counters");
            yield return Click("pause");
            Require(p.Session.State == TrainingState.Paused, "Pause UI enters Paused");
            yield return Mark("pause", true);
            yield return Frames(60);
            yield return Click("resume");
            Require(p.Session.State == TrainingState.Running && p.Session.PausedSeconds > 0, "Resume retains separate pause time");
            yield return Click("pause");
            yield return Click("abort");
            Require(p.Session.State == TrainingState.Aborted, "Abort is distinct from Inspect failure");
            yield return Mark("aborted", true);
            yield return ExtraCases();
        }

        static IEnumerator ExtraCases()
        {
            yield return Click("new_trial");
            Require(p.Session.State == TrainingState.Running && p.Session.InspectCount == 0,
                "Extra dual-controller cases use a new isolated trial");
            string trial = p.Session.TrialId;
            SetStage("left_hand_animation");
            yield return SelectLeft();
            yield return Rotate(p.origin.rotation, true);
            var leftHand = p.left.GetComponentInChildren<ControllerDrivenTrainingHand>();
            var rightHand = p.right.GetComponentInChildren<ControllerDrivenTrainingHand>();
            Require(leftHand && rightHand, "Both articulated controller hand components exist");
            SendKeys(Key.LeftShift, Key.T); yield return Seconds(.3f);
            Require(leftHand.IndexCurl > .8f && rightHand.IndexCurl < .2f,
                "Shift T input curls left index independently");
            yield return Mark("left_trigger", true);
            SendKeys(Key.LeftShift); yield return Seconds(.3f); SendKeys(); yield return Frames(3);
            Require(leftHand.IndexCurl < .15f, "Left index restores after trigger release");

            yield return GrabLeft();
            yield return Move(() => p.block.transform.position, p.block.transform.position + Vector3.up * .18f, .035f);
            int releasesBefore = CountEvent("RELEASE", trial);
            yield return LeftGrip(false);
            yield return Seconds(.4f);
            Require(!p.block.isSelected && leftHand.GripCurl < .15f && CountEvent("RELEASE", trial) == releasesBefore + 1,
                "Left grip release logs one Tester release and restores hand pose");
            yield return Seconds(.4f);
            yield return GrabLeft();
            yield return Move(() => p.block.transform.position, p.block.transform.position + Vector3.up * .12f, .035f);
            Require(leftHand.GripCurl > .8f, "Left held pose follows simulated grip");

            SetStage("holding_hand_ui_block");
            var inspect = ActiveButton("inspect");
            yield return Aim(inspect, true);
            int inspectBefore = p.Session.InspectCount;
            SendKeys(Key.LeftShift, Key.T); yield return Seconds(.25f);
            SendKeys(Key.LeftShift); yield return Frames(4); SendKeys(); yield return Frames(8);
            Require(p.block.isSelected && p.Session.InspectCount == inspectBefore && p.panel.CurrentPage == "training",
                "Holding left hand trigger does not click UI or change task");
            Require(!p.leftInteractor.TryGetCurrentUIRaycastResult(out var heldHit) || !heldHit.isValid,
                "Holding hand tracked UI model has no active hit");
            yield return Click("inspect");
            Require(p.block.isSelected && p.Session.State == TrainingState.Running && p.Session.InspectCount == 1 &&
                Events(trial).Any(e => (string)e["event"] == "FAILURE" && (string)e["result"] == "still_held"),
                "Free right controller clicks Inspect once while left holds; on-disk failure is still_held");
            yield return Mark("left_holds_right_inspects", true);
            yield return Click("back");
            releasesBefore = CountEvent("RELEASE", trial);
            int suspensionBefore = CountEvent("SYSTEM_HOLD_SUSPENDED", trial);
            RecordLeftGrip("before_pause");
            yield return Click("pause");
            yield return Frames(8);
            Require(p.Session.State == TrainingState.Paused && !p.block.isSelected &&
                CountEvent("RELEASE", trial) == releasesBefore && CountEvent("SYSTEM_HOLD_SUSPENDED", trial) == suspensionBefore + 1,
                "Pause cancels held selection as System suspension, without fake participant RELEASE");
            yield return Mark("pause_while_held", true);
            RecordLeftGrip("paused_before_resume_precondition");
            if (!IsLeftGripPressed()) yield return LeftGrip(true);
            Require(IsLeftGripPressed(), "Paused resume-test precondition has actual left Select or raw GripButton pressed");
            RecordLeftGrip("paused_with_pressed_precondition");
            yield return Click("resume");
            yield return Frames(15);
            RecordLeftGrip("after_resume");
            Require(!p.block.isSelected, "Resume does not regrab before explicit release and fresh selection");
            Require(IsLeftGripPressed(), "Held-control precondition remains pressed through resume (Select or raw GripButton)");
            yield return LeftGrip(false);
            yield return Seconds(.3f);
            RecordLeftGrip("after_explicit_left_release");
            Require(!IsLeftGripPressed() && !p.block.isSelected,
                "Explicit left release clears input after pause without phantom grab");
            yield return GrabLeft();
            Require(p.block.isSelected, "Fresh left grip works after pause and release gate");
            yield return LeftGrip(false);
            yield return Seconds(.5f);
            yield return Click("pause");
            yield return Click("abort");
            Require(p.Session.State == TrainingState.Aborted, "Extra trial ends explicitly, distinct from held Inspect failure");
            yield return ExistingTableOcclusion();
            yield return FrontPanelFixture();
            yield return Locomotion();
            yield return Mark("dual_controller_complete", true);
        }

        static IEnumerator FrontPanelFixture()
        {
            if (p.Session.State == TrainingState.Ready) yield return Click("start");
            else if (p.Session.State == TrainingState.Aborted || p.Session.State == TrainingState.Passed) yield return Click("new_trial");
            else if (p.Session.State == TrainingState.Running && (p.panel.CurrentPage == "inspect" || p.panel.CurrentPage == "examine")) yield return Click("back");
            else if (p.Session.State == TrainingState.Running && p.panel.CurrentPage == "manual") yield return Click("manual_close");
            Require(p.Session.State == TrainingState.Running && p.panel.CurrentPage == "training", "Fixture starts in Running through UI only");
            SetStage("fixture_panel_front_of_block");
            string trial = p.Session.TrialId;
            yield return SelectRight();
            var near = new List<Collider>();
            Vector3 towardHead = Vector3.ProjectOnPlane(p.head.position - p.block.transform.position, Vector3.up).normalized;
            Vector3 approach = (Vector3.up * .8f + towardHead * .6f).normalized;
            var referenceRay = new Ray(p.blockCollider.bounds.center + approach, -approach);
            Require(p.blockCollider.Raycast(referenceRay, out var referenceSurface, 2f), "Reference approach ray intersects block surface");
            Vector3 surface = referenceSurface.point;
            var initialRay = p.rightInteractor.farInteractionCaster.castOrigin;
            Quaternion rayRelativeRotation = Quaternion.Inverse(p.right.rotation) * initialRay.rotation;
            yield return Rotate(Quaternion.LookRotation(-approach, Vector3.up) * Quaternion.Inverse(rayRelativeRotation));
            yield return Seconds(.2f);
            RaycastHit blockSurface = default;
            bool validGeometry = false;
            foreach (float gap in new[] { .08f, .055f, .035f })
            {
                // The stabilized effective origin stops updating when XRI prioritizes a near candidate.
                // Follow the actual tracked cast origin; normal XRI refreshes its effective ray after UI owns it.
                yield return Move(() => p.rightInteractor.farInteractionCaster.castOrigin.position,
                    surface + approach * gap, .022f);
                yield return Seconds(.2f); // Allow the real stabilized cast origin to settle before measuring geometry.
                var source = p.rightInteractor.farInteractionCaster.castOrigin;
                var testRay = new Ray(source.position, source.forward);
                near.Clear();
                bool overlaps = p.rightInteractor.nearInteractionCaster.TryGetColliderTargets(p.rightInteractor.interactionManager, near) && near.Contains(p.blockCollider);
                bool outsideWithGap = p.blockCollider.Raycast(testRay, out blockSurface, 1f) && blockSurface.distance > .022f;
                if (overlaps && outsideWithGap) { validGeometry = true; break; }
            }
            if (!validGeometry)
            {
                RecordFixtureDiagnostic((RectTransform)ActiveButton("inspect").transform, p.panel.transform as RectTransform);
                yield return Mark("fixture_approach_diagnostic", true);
            }
            Require(validGeometry, "Panel-front fixture controller is outside block surface with measured gap and real near overlap");
            Transform panelTransform = p.panel.transform;
            Vector3 oldPosition = panelTransform.position;
            Quaternion oldRotation = panelTransform.rotation;
            var command = ActiveButton("inspect");
            var rect = (RectTransform)command.transform;
            try
            {
                var ray = p.rightInteractor.farInteractionCaster.castOrigin;
                Vector3 direction = ray.forward;
                Vector3 buttonPoint = ray.position + direction * (blockSurface.distance * .45f);
                panelTransform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                panelTransform.position += buttonPoint - rect.TransformPoint(rect.rect.center);
                evidence.Add(new { kind = "FIXTURE_SETUP", source = "Tester", fixture = "panel_in_front_of_near_block",
                    not_an_input_action = true, changed = "participant panel world pose ONLY; no block/rig/controller pose mutation",
                    originalPanelPosition = A(oldPosition), originalPanelEuler = A(oldRotation.eulerAngles),
                    fixturePanelPosition = A(panelTransform.position), fixturePanelEuler = A(panelTransform.eulerAngles) });
                // Already aligned to the ACTUAL measured ray. Re-aiming at a 3 cm panel would rotate
                // the controller's grip/aim offset and move the ray origin, invalidating this fixture.
                SendKeys(); SendMouse(Vector2.zero, false);
                yield return Seconds(.4f);
                bool hover = p.rightInteractor.TryGetCurrentUIRaycastResult(out var hit) && hit.gameObject &&
                    (hit.gameObject.transform == rect || hit.gameObject.transform.IsChildOf(rect));
                bool inFrontOfSurface = RecordFixtureDiagnostic(rect, panelTransform as RectTransform);
                yield return Mark("fixture_diagnostic", true);
                Require(inFrontOfSurface, "Fixture panel plane is outside and strictly before actual block collider surface");
                Require(hover, "Panel-front fixture receives an actual XRI UI hover before combined grip/trigger input");
                int beforeInspect = p.Session.InspectCount;
                int beforeGrab = CountEvent("GRAB", trial);
                SendKeys(Key.G, Key.T); yield return Frames(4); SendKeys(); yield return Frames(15);
                Require(p.Session.InspectCount == beforeInspect + 1 && CountEvent("GRAB", trial) == beforeGrab && !p.block.isSelected,
                    "One combined G+T input yields one Inspect and no grab through front UI");
                yield return Mark("panel_front_no_clickthrough_fixture", true);
            }
            finally
            {
                if (panelTransform) panelTransform.SetPositionAndRotation(oldPosition, oldRotation);
                evidence.Add(new { kind = "FIXTURE_RESTORE", source = "Tester", fixture = "panel_in_front_of_near_block",
                    not_an_input_action = true, panelPosition = panelTransform ? A(panelTransform.position) : null,
                    restored = panelTransform && Vector3.Distance(panelTransform.position, oldPosition) < .0001f &&
                        Quaternion.Angle(panelTransform.rotation, oldRotation) < .001f });
            }
            yield return Move(() => p.right.position, uiHandPosition, .045f);
            // Wall test uses the restored normal-distance panel, avoiding a second near-plane aim fixture.
            yield return TemporaryWallFixture(trial);
            yield return Click("back");
            yield return Click("pause");
            yield return Click("abort");
        }

        static IEnumerator TemporaryWallFixture(string trial)
        {
            SetStage("temporary_wall_ui_occlusion_fixture");
            var button = ActiveButton("back");
            var rect = (RectTransform)button.transform;
            yield return Aim(button, false);
            yield return Seconds(.2f);
            Require(p.rightInteractor.TryGetCurrentUIRaycastResult(out var beforeHit) && beforeHit.gameObject &&
                (beforeHit.gameObject.transform == rect || beforeHit.gameObject.transform.IsChildOf(rect)),
                "Temporary-wall baseline has valid XRI hover before wall setup");
            GameObject wall = null;
            try
            {
                var origin = p.rightInteractor.farInteractionCaster.effectiveCastOrigin;
                Vector3 point = rect.TransformPoint(rect.rect.center);
                Vector3 direction = (point - origin.position).normalized;
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Training_UI_Wall_TEST_FIXTURE";
                wall.tag = "EditorOnly";
                wall.hideFlags = HideFlags.DontSave;
                wall.transform.SetPositionAndRotation(Vector3.Lerp(origin.position, point, .5f), Quaternion.LookRotation(direction, Vector3.up));
                wall.transform.localScale = new Vector3(.18f, .18f, .004f);
                var collider = wall.GetComponent<BoxCollider>();
                evidence.Add(new { kind = "FIXTURE_SETUP", source = "Tester", fixture = "temporary_wall_box_collider",
                    not_an_input_action = true, not_authored_factory_wall = true,
                    changed = "new temporary wall cube only; existing environment/rig/workpiece untouched",
                    position = A(wall.transform.position), sizeMetres = A(wall.transform.localScale) });
                yield return Seconds(.25f);
                origin = p.rightInteractor.farInteractionCaster.effectiveCastOrigin;
                float panelDistance = Vector3.Distance(origin.position, point);
                Require(collider.Raycast(new Ray(origin.position, origin.forward), out var wallHit, panelDistance) && wallHit.distance < panelDistance,
                    "Temporary wall collider lies physically before the UI on actual controller ray");
                int inspectCount = p.Session.InspectCount;
                int grabs = CountEvent("GRAB", trial);
                int eventCount = Events(trial).Count();
                string page = p.panel.CurrentPage;
                SendKeys(Key.T); yield return Frames(4); SendKeys(); yield return Frames(15);
                Require(p.panel.CurrentPage == page && p.Session.InspectCount == inspectCount &&
                    CountEvent("GRAB", trial) == grabs && Events(trial).Count() == eventCount && !p.block.isSelected,
                    "Trigger through temporary wall produces no UI command, task event or block grab");
                Require(!p.rightInteractor.TryGetCurrentUIRaycastResult(out _), "Temporary wall blocks the actual XRI UI hit");
                yield return Mark("temporary_wall_blocks_ui_fixture", true);
            }
            finally
            {
                if (wall) UnityEngine.Object.DestroyImmediate(wall);
                evidence.Add(new { kind = "FIXTURE_RESTORE", source = "Tester", fixture = "temporary_wall_box_collider",
                    not_an_input_action = true, removed = wall == null });
            }
            yield return Seconds(.25f);
            Require(p.rightInteractor.TryGetCurrentUIRaycastResult(out var afterHit) && afterHit.gameObject &&
                (afterHit.gameObject.transform == rect || afterHit.gameObject.transform.IsChildOf(rect)),
                "Actual XRI UI hover returns after temporary wall is removed");
        }

        static IEnumerator AimAtPoint(Vector3 point)
        {
            var ray = p.rightInteractor.farInteractionCaster.effectiveCastOrigin;
            Quaternion relative = Quaternion.Inverse(p.right.rotation) * ray.rotation;
            Quaternion desired = Quaternion.LookRotation(point - ray.position, Vector3.up) * Quaternion.Inverse(relative);
            yield return Rotate(desired);
            yield return Frames(6);
        }

        static bool RecordFixtureDiagnostic(RectTransform button, RectTransform panel)
        {
            var interactor = p.rightInteractor;
            var origin = interactor.farInteractionCaster.effectiveCastOrigin;
            var ray = new Ray(origin.position, origin.forward);
            bool planeHit = new Plane(panel.forward, panel.position).Raycast(ray, out float distance);
            Vector3 planePoint = ray.GetPoint(distance);
            bool actualHit = interactor.TryGetCurrentUIRaycastResult(out var hit);
            var rawCastOrigin = interactor.farInteractionCaster.castOrigin;
            bool hasModel = interactor.TryGetUIModel(out var model);
            var group = interactor.containingGroup;
            var pokeDiagnostics = p.right.GetComponentsInChildren<XRPokeInteractor>(true).Select(poke =>
            {
                bool hasPokeModel = poke.TryGetUIModel(out var pokeModel);
                return new { poke.name, poke.enableUIInteraction, poke.isActiveAndEnabled, poke.hasHover, poke.hasSelection,
                    hasPokeModel, endpointIndex = hasPokeModel ? pokeModel.currentRaycastEndpointIndex : -1,
                    currentHit = hasPokeModel && pokeModel.currentRaycast.gameObject ? pokeModel.currentRaycast.gameObject.name : "none",
                    position = A(poke.transform.position), poke.pokeHoverRadius, poke.pokeInteractionOffset };
            }).ToArray();
            bool blockHit = p.blockCollider.Raycast(ray, out var surfaceHit, 2f);
            var occluders = planeHit && distance > 0 ? Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance).Select(h => new { name = h.collider.name, distance = h.distance, point = A(h.point) }).ToArray() : null;
            var canvasGroup = panel.GetComponent<CanvasGroup>();
            evidence.Add(new { kind = "FIXTURE_DIAGNOSTIC", source = "Tester", panelOwnsRay = TrainingUIArbitration.PanelOwnsRay(interactor, panel),
                interactor.enableNearCasting, interactor.enableFarCasting, interactor.enableUIInteraction, interactor.hasSelection,
                rayOrigin = A(origin.position), rayDirection = A(origin.forward), panelForward = A(panel.forward),
                rawCastOrigin = A(rawCastOrigin.position), rawCastDirection = A(rawCastOrigin.forward),
                rayPanelForwardDot = Vector3.Dot(origin.forward, panel.forward), planeHit, distanceToPanelPlane = distance,
                distanceToButtonCenter = Vector3.Distance(origin.position, button.TransformPoint(button.rect.center)),
                panelLocalHit = A(panel.InverseTransformPoint(planePoint)), buttonLocalHit = A(button.InverseTransformPoint(planePoint)),
                panelContainsHit = panel.rect.Contains((Vector2)panel.InverseTransformPoint(planePoint)),
                buttonContainsHit = button.rect.Contains((Vector2)button.InverseTransformPoint(planePoint)),
                blockHit, distanceToBlockSurface = blockHit ? surfaceHit.distance : -1f,
                panelBeforeBlockSurface = blockHit && planeHit && distance > .001f && distance < surfaceHit.distance - .002f,
                actualHit, actualXRIHitName = hit.gameObject ? hit.gameObject.name : "none", actualXRIHitDistance = hit.distance,
                hasModel, currentRaycastEndpointIndex = hasModel ? model.currentRaycastEndpointIndex : -1,
                rayPointCount = hasModel ? model.raycastPoints.Count : -1,
                blockedByInteractionGroup = interactor.IsBlockedByInteractionWithinGroup(),
                activeGroupInteractor = group?.activeInteractor?.transform.name,
                activeGroupInteractorType = group?.activeInteractor?.GetType().Name, pokeDiagnostics,
                alpha = canvasGroup ? canvasGroup.alpha : -1f, blocksRaycasts = canvasGroup && canvasGroup.blocksRaycasts, occluders });
            return blockHit && planeHit && distance > .001f && distance < surfaceHit.distance - .002f;
        }

        static IEnumerator ExistingTableOcclusion()
        {
            SetStage("existing_table_ui_occlusion");
            yield return SelectRight();
            var table = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)
                .Single(c => c.name == "Training_Practice_A");
            var button = ActiveButton("new_trial");
            var rect = (RectTransform)button.transform;
            Vector3 buttonPoint = rect.TransformPoint(rect.rect.center);
            // Read the real collider bounds; route the simulated controller behind it by keyboard input.
            // The line from this point to the button passes through the authored collider centre.
            Vector3 behind = table.bounds.center - (buttonPoint - table.bounds.center).normalized * (table.bounds.extents.magnitude + .3f);
            bool blocked = false;
            for (int i = 0; i < 3; i++)
            {
                yield return Move(() => p.rightInteractor.farInteractionCaster.effectiveCastOrigin.position, behind, .035f);
                yield return Aim(button, false);
                var ray = p.rightInteractor.farInteractionCaster.effectiveCastOrigin;
                Vector3 direction = buttonPoint - ray.position;
                blocked = Physics.RaycastAll(ray.position, direction.normalized, direction.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .Any(h => h.collider == table && h.distance < direction.magnitude);
                if (blocked) break;
            }
            Require(blocked, "Real Training_Practice_A collider lies before UI button on tested ray");
            string beforeTrial = p.Session.TrialId;
            string beforePage = p.panel.CurrentPage;
            SendKeys(Key.T); yield return Frames(4); SendKeys(); yield return Frames(15);
            Require(p.Session.TrialId == beforeTrial && p.panel.CurrentPage == beforePage && p.Session.State == TrainingState.Aborted,
                "Trigger through existing table does not click new-trial UI");
            yield return Mark("existing_table_occlusion", true);
            yield return Move(() => p.right.position, uiHandPosition, .045f);
        }

        static IEnumerator Locomotion()
        {
            SetStage("existing_locomotion");
            Vector3 before = p.origin.position;
            SendKeys(Key.LeftShift, Key.I); yield return Seconds(.22f);
            SendKeys(Key.LeftShift); yield return Frames(4); SendKeys(); yield return Frames(8);
            float displacement = Vector3.Distance(before, p.origin.position);
            Require(displacement > .01f, "Shift I -> simulated left thumbstick -> existing move changes XR Origin");
            SendKeys(Key.LeftShift, Key.K); yield return Seconds(.22f);
            SendKeys(Key.LeftShift); yield return Frames(4); SendKeys(); yield return Frames(8);
            Quaternion beforeTurn = p.origin.rotation;
            SendKeys(Key.J); yield return Frames(4); SendKeys(); yield return Frames(15);
            Require(Quaternion.Angle(beforeTurn, p.origin.rotation) > 10f,
                "J -> simulated right thumbstick -> existing snap turn changes XR Origin");
            yield return Seconds(.6f);
            SendKeys(Key.L); yield return Frames(4); SendKeys(); yield return Frames(15);
            evidence.Add(new { kind = "LOCOMOTION", tested = "left move / right snap; no teleport", displacement,
                finalOrigin = A(p.origin.position), finalYaw = p.origin.eulerAngles.y });
        }

        static IEnumerator GrabLeft()
        {
            SetStage("left_near_grab");
            yield return SelectLeft();
            yield return Rotate(p.origin.rotation, true);
            yield return Move(() => p.leftInteractor.nearInteractionCaster.effectiveCastOrigin.position, p.block.transform.position, .035f);
            yield return LeftGrip(true);
            yield return Frames(15);
            Require(p.block.isSelected && p.block.interactorsSelecting.Contains(p.leftInteractor),
                "Left controller quick Grip input selects through real near XRI");
            yield return Mark("left_held", true);
        }

        static IEnumerator LeftGrip(bool desired)
        {
            yield return SelectLeft();
            for (int i = 0; simulator.controllerInputMode.ToString() != "Grip" && i < 5; i++)
            {
                SendKeys(Key.Backquote); yield return Frames(2); SendKeys(); yield return Frames(3);
            }
            Require(simulator.controllerInputMode.ToString() == "Grip", "Simulator quick action selected Grip through backquote");
            if (IsLeftGripPressed() != desired)
            {
                SendKeys(Key.Space); yield return Frames(2); SendKeys(); yield return Frames(5);
            }
            leftQuickGripOn = desired;
        }

        static XRSimulatedController RawLeftController() => InputSystem.devices.OfType<XRSimulatedController>()
            .FirstOrDefault(d => d.usages.Any(u => u.ToString() == "LeftHand"));

        static bool IsLeftGripPressed()
        {
            var raw = RawLeftController();
            return p.leftInteractor.selectInput.ReadIsPerformed() || (raw != null && raw.gripButton.isPressed);
        }

        static void RecordLeftGrip(string label)
        {
            var action = p.leftGrip != null ? p.leftGrip.action : null;
            var raw = RawLeftController();
            evidence.Add(new { kind = "LEFT_GRIP_DIAGNOSTIC", label, frame = Time.frameCount, timestamp = p.Session.Timestamp,
                actionName = action?.name, actionId = action?.id.ToString(), actionEnabled = action != null && action.enabled,
                actionValue = action != null ? action.ReadValue<float>() : 0f,
                selectPerformed = p.leftInteractor.selectInput.ReadIsPerformed(), rawDeviceName = raw?.name,
                rawGrip = raw != null ? raw.grip.ReadValue() : 0f, rawGripPressed = raw != null && raw.gripButton.isPressed,
                selected = p.block.isSelected, simulatorTarget = simulator.targetedDeviceInput.ToString(), quickGripExpected = leftQuickGripOn });
        }

        static IEnumerable<JObject> Events(string trial)
        {
            // Reader permits the existing logger's write handle; logger sharing and flush policy stay unchanged.
            using (var file = new FileStream(p.Session.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(file))
                return reader.ReadToEnd().Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(JObject.Parse).Where(e => (string)e["trial_id"] == trial).ToArray();
        }
        static int CountEvent(string name, string trial) => Events(trial).Count(e => (string)e["event"] == name);

        static TrainingPanelCommand ActiveButton(string id) => p.panel.GetComponentsInChildren<TrainingPanelCommand>()
            .Single(c => c.Id == id && c.gameObject.activeInHierarchy && c.GetComponent<Button>().interactable);

        static IEnumerator SelectLeft()
        {
            if (simulator.manipulatingLeftController && !simulator.manipulatingRightController && !simulator.manipulatingFPS) yield break;
            SendKeys(Key.LeftBracket); yield return Frames(2); SendKeys(); yield return Frames(3);
            Require(simulator.manipulatingLeftController && !simulator.manipulatingRightController && !simulator.manipulatingFPS,
                "Left pose selected via bracket release, controller modality retained");
        }

        static IEnumerator Aim(TrainingPanelCommand command, bool left)
        {
            if (left) yield return SelectLeft(); else yield return SelectRight();
            var hand = left ? p.left : p.right;
            var interactor = left ? p.leftInteractor : p.rightInteractor;
            var rect = (RectTransform)command.transform;
            for (int i = 0; i < 3; i++)
            {
                var ray = interactor.farInteractionCaster.effectiveCastOrigin;
                Quaternion relative = Quaternion.Inverse(hand.rotation) * ray.rotation;
                Quaternion desired = Quaternion.LookRotation(rect.TransformPoint(rect.rect.center) - ray.position, Vector3.up) * Quaternion.Inverse(relative);
                yield return Rotate(desired, left);
                yield return Frames(8);
            }
        }

        static IEnumerator SelectRight()
        {
            if (simulator.manipulatingRightController && !simulator.manipulatingLeftController && !simulator.manipulatingFPS) yield break;
            SendKeys(Key.RightBracket); yield return Frames(2);
            SendKeys(); yield return Frames(3); // Simulator switches targeted controller on release edge.
            Require(simulator.manipulatingRightController && !simulator.manipulatingLeftController && !simulator.manipulatingFPS,
                "Right controller selected via bracket input, controller modality retained");
        }

        static IEnumerator Click(string id)
        {
            SetStage("UI:" + id);
            yield return SelectRight();
            yield return Frames(15); // Existing UI transition and tracked UI model must settle.
            var command = p.panel.GetComponentsInChildren<TrainingPanelCommand>()
                .SingleOrDefault(c => c.Id == id && c.gameObject.activeInHierarchy && c.GetComponent<Button>().interactable);
            if (!command) throw new InvalidOperationException("No active interactable button: " + id);
            var rect = (RectTransform)command.transform;
            bool hover = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 point = rect.TransformPoint(rect.rect.center);
                var ray = p.rightInteractor.farInteractionCaster.effectiveCastOrigin;
                Quaternion rayLocal = Quaternion.Inverse(p.right.rotation) * ray.rotation;
                Quaternion desired = Quaternion.LookRotation(point - ray.position, Vector3.up) * Quaternion.Inverse(rayLocal);
                yield return Rotate(desired);
                yield return Frames(8);
                hover = p.rightInteractor.TryGetCurrentUIRaycastResult(out var hit) && hit.gameObject &&
                    (hit.gameObject.transform == rect || hit.gameObject.transform.IsChildOf(rect));
                if (hover) break;
            }
            Require(hover, "Actual XRI ray hover on button " + id);
            string page = p.panel.CurrentPage;
            SendKeys(Key.T); yield return Frames(4);
            SendKeys(); yield return Frames(12);
            evidence.Add(new { kind = "UI_INPUT", command = id, beforePage = page, afterPage = p.panel.CurrentPage,
                frame = Time.frameCount, timestamp = p.Session.Timestamp, snapshot = p.Snapshot() });
        }

        static IEnumerator Grab()
        {
            SetStage("near_grab");
            yield return SelectRight();
            // Point away from UI so its actual raycast gate cannot suppress the near grab.
            yield return Rotate(p.origin.rotation);
            yield return Move(() => p.rightInteractor.nearInteractionCaster.effectiveCastOrigin.position,
                p.block.transform.position, .035f);
            SendKeys(Key.G); holdingGrip = true;
            yield return Frames(18);
            Require(p.block.isSelected && p.block.interactorsSelecting.Contains(p.rightInteractor), "G input grabs block through near XRI");
            yield return Mark("held", true);
        }

        static IEnumerator OrientHeld(Quaternion desired)
        {
            SetStage("rotate_held");
            for (int i = 0; i < 5; i++)
            {
                Quaternion relative = Quaternion.Inverse(p.right.rotation) * p.block.transform.rotation;
                yield return Rotate(desired * Quaternion.Inverse(relative));
                yield return Frames(15);
                if (Quaternion.Angle(desired, p.block.transform.rotation) < 3f) break;
            }
            Require(Quaternion.Angle(desired, p.block.transform.rotation) < 5f, "Held block follows controller rotation");
        }

        static IEnumerator PlaceHeld()
        {
            SetStage("place_release");
            Vector3 target = p.targetPad.bounds.center;
            target.y = p.targetPad.bounds.max.y + p.blockCollider.bounds.extents.y + .025f;
            yield return Move(() => p.block.transform.position, target + Vector3.up * .12f, .032f);
            yield return Move(() => p.block.transform.position, target, .03f);
            yield return Frames(18);
            holdingGrip = false; SendKeys();
            yield return Frames(80);
            yield return Mark("released_on_target", false);
        }

        static IEnumerator Move(Func<Vector3> observedPosition, Vector3 target, float tolerance)
        {
            double deadline = EditorApplication.timeSinceStartup + 30;
            Vector3 originalSpeeds = new Vector3(simulator.translateXSpeed, simulator.translateYSpeed, simulator.translateZSpeed);
            float minimumSpeedUsed = Mathf.Min(originalSpeeds.x, originalSpeeds.y, originalSpeeds.z);
            int steps = 0;
            var recentSamples = new Queue<object>();
            evidence.Add(new { kind = "TEST_INPUT_GAIN_SETUP", source = "Tester", changed = "simulator keyboard translation gains only",
                originalSpeeds = A(originalSpeeds), simulator.bodyTranslateMultiplier, tolerance,
                strategy = "adaptive fine keyboard pulses near target; no pose mutation" });
            try
            {
                while (Vector3.Distance(observedPosition(), target) > tolerance)
                {
                    if (EditorApplication.timeSinceStartup > deadline)
                        throw new TimeoutException("Pose translation timeout; error=" + (target - observedPosition()));
                    Vector3 up = p.head.parent ? p.head.parent.up : Vector3.up;
                    Vector3 forward = Vector3.ProjectOnPlane(p.head.forward, up).normalized;
                    Quaternion basis = Quaternion.LookRotation(forward, up);
                    Vector3 error = Quaternion.Inverse(basis) * (target - observedPosition());
                    float[] magnitude = { Mathf.Abs(error.x), Mathf.Abs(error.y), Mathf.Abs(error.z) };
                    int axis = magnitude[0] > magnitude[1] ? 0 : 1;
                    if (magnitude[2] > magnitude[axis]) axis = 2;
                    // Keep each real keyboard pulse below the remaining error even when Editor frame time varies.
                    float conservativeFrameTime = Mathf.Max(Time.deltaTime, .05f) * 2f;
                    float speedLimit = Mathf.Max(.001f, magnitude[axis] * .4f /
                        (conservativeFrameTime * Mathf.Max(.001f, simulator.bodyTranslateMultiplier)));
                    simulator.translateXSpeed = Mathf.Min(originalSpeeds.x, speedLimit);
                    simulator.translateYSpeed = Mathf.Min(originalSpeeds.y, speedLimit);
                    simulator.translateZSpeed = Mathf.Min(originalSpeeds.z, speedLimit);
                    minimumSpeedUsed = Mathf.Min(minimumSpeedUsed, speedLimit);
                    Key key = axis == 0 ? (error.x > 0 ? Key.D : Key.A) :
                        axis == 1 ? (error.y > 0 ? Key.E : Key.Q) : (error.z > 0 ? Key.W : Key.S);
                    SendKeys(key); yield return Frames(1);
                    SendKeys(); yield return Frames(2);
                    var rawRight = InputSystem.devices.OfType<XRSimulatedController>()
                        .FirstOrDefault(d => d.usages.Any(u => u.ToString() == "RightHand"));
                    var far = p.rightInteractor.farInteractionCaster;
                    recentSamples.Enqueue(new { step = ++steps, frame = Time.frameCount, key = key.ToString(),
                        observed = A(observedPosition()), target = A(target), error = A(target - observedPosition()),
                        observedFar = A(far.effectiveCastOrigin.position), rawCastOrigin = A(far.castOrigin.position),
                        trackedRight = A(p.right.position), simulatedRightLocal = rawRight != null ? A(rawRight.devicePosition.ReadValue()) : null,
                        selectedTarget = simulator.targetedDeviceInput.ToString(), p.rightInteractor.enableNearCasting,
                        p.rightInteractor.hasHover, p.rightInteractor.hasSelection,
                        speed = new[] { simulator.translateXSpeed, simulator.translateYSpeed, simulator.translateZSpeed } });
                    if (recentSamples.Count > 10) recentSamples.Dequeue();
                }
                SendKeys(); yield return Frames(6);
            }
            finally
            {
                evidence.Add(new { kind = "MOVE_TRACE", stage, steps, samples = recentSamples.ToArray() });
                if (simulator)
                {
                    simulator.translateXSpeed = originalSpeeds.x;
                    simulator.translateYSpeed = originalSpeeds.y;
                    simulator.translateZSpeed = originalSpeeds.z;
                }
                evidence.Add(new { kind = "TEST_INPUT_GAIN_RESTORE", source = "Tester", minimumSpeedUsed,
                    restoredSpeeds = A(originalSpeeds), restored = simulator && simulator.translateXSpeed == originalSpeeds.x &&
                        simulator.translateYSpeed == originalSpeeds.y && simulator.translateZSpeed == originalSpeeds.z });
            }
        }

        static IEnumerator Rotate(Quaternion worldRotation, bool left = false, Transform trackedOverride = null)
        {
            var tracked = trackedOverride ? trackedOverride : left ? p.left : p.right;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Quaternion parent = tracked.parent ? tracked.parent.rotation : Quaternion.identity;
                Vector3 desired = (Quaternion.Inverse(parent) * worldRotation).eulerAngles;
                Vector3 current = tracked.localRotation.eulerAngles;
                float pitch = Mathf.DeltaAngle(current.x, desired.x);
                float yaw = Mathf.DeltaAngle(current.y, desired.y);
                float roll = Mathf.DeltaAngle(current.z, desired.z);
                if (Quaternion.Angle(tracked.rotation, worldRotation) < .65f) yield break;
                float sx = Mathf.Max(.001f, simulator.rotateXSensitivity);
                float sy = Mathf.Max(.001f, simulator.rotateYSensitivity);
                if (Mathf.Abs(pitch) + Mathf.Abs(yaw) > .25f)
                {
                    SendKeys();
                    SendMouse(new Vector2(Mathf.Clamp(yaw, -70, 70) / sx,
                        Mathf.Clamp(pitch, -70, 70) / sy * (simulator.rotateYInvert ? 1f : -1f)), true);
                    yield return Frames(2); SendMouse(Vector2.zero, false); yield return Frames(2);
                }
                if (Mathf.Abs(roll) > .3f)
                {
                    SendKeys(Key.Z);
                    SendMouse(new Vector2(-Mathf.Clamp(roll, -70, 70) / sx, 0f), true);
                    yield return Frames(2); SendMouse(Vector2.zero, false); SendKeys(); yield return Frames(2);
                }
            }
            Require(Quaternion.Angle(tracked.rotation, worldRotation) < 2f, "Tracked pose rotation converges through simulator mouse input");
        }

        static IEnumerator Seconds(float duration)
        {
            double end = Time.realtimeSinceStartupAsDouble + duration;
            while (Time.realtimeSinceStartupAsDouble < end) yield return null;
        }

        static IEnumerator Frames(int count)
        {
            int end = Time.frameCount + count;
            while (Time.frameCount < end) yield return null;
        }

        static void SendKeys(params Key[] keys)
        {
            if (holdingGrip && !keys.Contains(Key.G)) keys = keys.Concat(new[] { Key.G }).ToArray();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
            inputs.Add(new { frame = Time.frameCount, kind = "KeyboardState", keys = keys.Select(k => k.ToString()).ToArray() });
        }

        static void SendMouse(Vector2 delta, bool rightButton)
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = delta }.WithButton(MouseButton.Right, rightButton));
            inputs.Add(new { frame = Time.frameCount, kind = "MouseState", dx = delta.x, dy = delta.y, rightButton });
        }

        static void Require(bool condition, string assertion)
        {
            evidence.Add(new { kind = "ASSERT", assertion, passed = condition, frame = Time.frameCount,
                timestamp = p.Session.Timestamp, snapshot = p.Snapshot() });
            if (!condition) throw new InvalidOperationException(assertion);
        }

        static void SetStage(string value)
        {
            stage = value;
            TrainingV2Editor.Write(OutputPrefix + "_progress", new { stage, frame = Time.frameCount,
                elapsed = EditorApplication.timeSinceStartup - started });
        }

        static IEnumerator Mark(string label, bool capture)
        {
            if (capture) yield return Seconds(.25f);
            evidence.Add(new { kind = "MILESTONE", label, frame = Time.frameCount, timestamp = p.Session.Timestamp,
                trial = p.Session.TrialId, state = p.Session.State.ToString(), page = p.panel.CurrentPage, snapshot = p.Snapshot() });
            if (capture) TrainingV2Editor.Capture("input_" + label);
            TrainingV2Editor.Write(OutputPrefix + "_progress", new { stage, label, evidence });
        }

        static void Finish(bool passed, string detail)
        {
            if (!IsRunning) return;
            IsRunning = false;
            EditorApplication.update -= Tick;
            holdingGrip = false;
            bool cleanupLeftGrip = leftQuickGripOn;
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current,
                cleanupLeftGrip ? new KeyboardState(Key.LeftShift, Key.G) : new KeyboardState());
            if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            // Dispose suspended nested iterators so fixture finally blocks restore their explicit setup.
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception cleanupError) { passed = false; detail += "\nFixture cleanup: " + cleanupError; }
            }
            string logDirectory = p && p.Session != null ? p.Session.DirectoryPath : "";
            string hover = "none";
            if (p && p.rightInteractor && p.rightInteractor.TryGetCurrentUIRaycastResult(out var hit) && hit.gameObject)
                hover = hit.gameObject.name;
            TrainingV2Editor.Write(OutputPrefix + "_result", new { passed, detail, stage, input_source = "Simulator", actor_source = "Tester",
                route = "Keyboard/Mouse InputSystem -> XRInteractionSimulator -> simulated XR device -> XRI -> UI/interaction -> state/log",
                headset_controller_hardware = "NOT_VERIFIED", elapsed = EditorApplication.timeSinceStartup - started,
                logDirectory, lastHover = hover, snapshot = p ? p.Snapshot() : null,
                rightPosition = p ? A(p.right.position) : null, rightEuler = p ? A(p.right.eulerAngles) : null,
                cleanup = cleanupLeftGrip ? "Tester Shift+G -> Shift (release left grip) -> neutral" : "Tester neutral keyboard/mouse", evidence });
            TrainingV2Editor.Write(OutputPrefix + "_events", inputs);
            // Queued cleanup key-up is also tester input; restore actor only after it traverses PlayerLoop.
            var actorOwner = p;
            string actorToRestore = previousActor;
            int cleanupStartFrame = Time.frameCount;
            int restoreAfterFrame = cleanupStartFrame + (cleanupLeftGrip ? 7 : 3);
            int cleanupPhase = 0;
            EditorApplication.CallbackFunction restoreActor = null;
            restoreActor = () =>
            {
                if (EditorApplication.isPlaying && Keyboard.current != null && cleanupLeftGrip)
                {
                    if (cleanupPhase == 0 && Time.frameCount >= cleanupStartFrame + 2)
                    {
                        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftShift));
                        cleanupPhase = 1;
                    }
                    if (cleanupPhase == 1 && Time.frameCount >= cleanupStartFrame + 4)
                    {
                        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                        cleanupPhase = 2;
                    }
                }
                if (EditorApplication.isPlaying && actorOwner && Time.frameCount < restoreAfterFrame) return;
                if (actorOwner) actorOwner.actorSource = actorToRestore;
                EditorApplication.update -= restoreActor;
            };
            EditorApplication.update += restoreActor;
            stack.Clear();
            Debug.Log("TRAINING_INPUT_DRIVER_" + (passed ? "PASS" : "FAIL") + " stage=" + stage + " " + detail);
        }

        static Vector3 V(float[] v) => new Vector3(v[0], v[1], v[2]);
        static Quaternion Q(float[] q) => new Quaternion(q[0], q[1], q[2], q[3]);
        static float[] A(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
#endif
