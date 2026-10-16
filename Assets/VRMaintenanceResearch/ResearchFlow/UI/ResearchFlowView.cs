using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRMaintenanceResearch.TrainingV2;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchFlowView : MonoBehaviour
    {
        public ResearchSessionFlowController controller;
        public Transform Head;
        public Canvas[] ParticipantCanvases;
        public TMP_FontAsset thaiFont, japaneseFont, englishFont;
        public ResearchPanelMotion mainPanel, referencePanel;
        public Canvas helperCanvas;
        public Canvas RecoveryCanvas;
        public ResearchReferenceViewer ReferenceViewer { get; private set; }
        public string ReferenceMediaToken => (controller?.Language ?? "") + "/" + (ReferenceViewer?.SemanticToken ?? "");
        const float ReaderWidth = 1180, ReaderHeight = 800;
        string feedbackApplied;
        Coroutine inspectionVisibility;
        struct InspectionRenderReceipt
        {
            public int frame, cameraId;
            public string inspectionId, text;
            public bool Matches(int expectedFrame, int expectedCamera, string expectedId, string expectedText) =>
                !string.IsNullOrEmpty(inspectionId) && frame == expectedFrame && cameraId == expectedCamera
                && inspectionId == expectedId && text == expectedText;
        }
        InspectionRenderReceipt inspectionRenderStarted, inspectionRendered;
        bool FeedbackSurfaceReady => controller && controller.Flow?.IsTaskActive == true
            && controller.Flow.InspectPassed.HasValue && !controller.Flow.Paused && controller.FloorReady
            && string.IsNullOrEmpty(controller.Error) && !controller.Transitioning
            && mainPanel && mainPanel.gameObject.activeInHierarchy && mainPanel.GetComponent<Canvas>().enabled
            && !mainPanel.PlacementBlocked && mainPanel.IsReadableFromHead && mainContent.activeInHierarchy
            && mainGroup.alpha >= .99f && body && body.gameObject.activeInHierarchy && body.enabled
            && !body.havePropertiesChanged && !body.canvasRenderer.cull && body.canvasRenderer.GetInheritedAlpha() >= .99f
            && body.canvasRenderer.GetAlpha() >= .99f && body.color.a >= .99f && body.faceColor.a >= 252
            && body.textInfo.characterCount > 0
            && body.text == InspectionFeedbackText(controller.Flow.InspectPassed == true);
        // Bound S1 tasks show the spec outcome sentence; Pass keeps the stay-in-headset handover line beneath it.
        string InspectionFeedbackText(bool passed)
        {
            string outcome = controller.InspectionFeedbackKey;
            if (outcome == null) return Text(passed ? "inspect.pass.body" : "inspect.fail.body");
            return passed ? Text(outcome) + "\n" + Text("inspect.pass.body") : Text(outcome);
        }
        internal bool IsInspectionFeedbackVisible => Head && FeedbackSurfaceReady
            && FeedbackCameraReady(Head.GetComponent<Camera>())
            && inspectionRendered.Matches(Time.frameCount, Head.GetComponent<Camera>().GetInstanceID(), controller.InspectionId, body.text);
        internal bool InspectionFeedbackMatches(string id, string text, int frame) => IsInspectionFeedbackVisible
            && inspectionRendered.Matches(frame, Head.GetComponent<Camera>().GetInstanceID(), id, text);

        bool FeedbackCameraReady(Camera camera)
        {
            if (!Application.isPlaying || Application.isBatchMode || !Head || !camera || camera.transform != Head
                || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game || camera.targetTexture
                || camera.targetDisplay != 0 || camera.pixelWidth <= 0 || camera.pixelHeight <= 0
                || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            if ((camera.cullingMask & (1 << body.gameObject.layer)) == 0
                || (camera.cullingMask & (1 << mainPanel.gameObject.layer)) == 0) return false;
            foreach (var canvas in body.GetComponentsInParent<Canvas>())
                if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace) return false;
            if (!mainPanel.PlacementClear(mainPanel.transform.position, mainPanel.transform.rotation)) return false;
            return true;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginInspectionCamera;
            RenderPipelineManager.endCameraRendering += EndInspectionCamera;
        }

        void BeginInspectionCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Head) return;
            if (camera != Head.GetComponent<Camera>())
            {
                if (camera.cameraType == CameraType.Game && !camera.targetTexture && camera.targetDisplay == 0)
                { inspectionRenderStarted = default; inspectionRendered = default; }
                return;
            }
            inspectionRenderStarted = default; inspectionRendered = default;
            if (FeedbackSurfaceReady && FeedbackCameraReady(camera))
                inspectionRenderStarted = new InspectionRenderReceipt { frame = Time.frameCount, cameraId = camera.GetInstanceID(),
                    inspectionId = controller.InspectionId, text = body.text };
        }

        void EndInspectionCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Head || camera != Head.GetComponent<Camera>()) return;
            if (FeedbackSurfaceReady && FeedbackCameraReady(camera)
                && inspectionRenderStarted.Matches(Time.frameCount, camera.GetInstanceID(), controller.InspectionId, body.text))
                inspectionRendered = inspectionRenderStarted;
            inspectionRenderStarted = default;
        }

        IEnumerator ObserveInspectionFeedback(string id)
        {
            while (controller && controller.InspectionId == id && controller.Flow?.IsTaskActive == true
                && !controller.InspectionFeedbackVisible && string.IsNullOrEmpty(controller.Error))
            {
                yield return new WaitForEndOfFrame();
                if (IsInspectionFeedbackVisible && controller.InspectionFeedbackRendered(id, body.text, Time.frameCount)) break;
            }
            inspectionVisibility = null;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginInspectionCamera;
            RenderPipelineManager.endCameraRendering -= EndInspectionCamera;
            inspectionRenderStarted = default; inspectionRendered = default;
            if (inspectionVisibility != null) StopCoroutine(inspectionVisibility);
            inspectionVisibility = null;
        }
        void PrepareReferenceViewer()
        {
            if (ReferenceViewer) return;
            ReferenceViewer = referencePanel.gameObject.AddComponent<ResearchReferenceViewer>();
            ReferenceViewer.Initialize(scroll, referenceBody, this);
        }
        void SyncReaderVisibility()
        {
            if (!ReferenceViewer) return;
            ReferenceViewer.SetVisible(controller && controller.ReferenceOpen && !controller.ReferenceMinimized
                && controller.Flow?.Paused == false && controller.FloorReady && string.IsNullOrEmpty(controller.Error)
                && referencePanel.gameObject.activeInHierarchy && referencePanel.GetComponent<Canvas>().enabled
                && referenceGroup.alpha > .01f && !referencePanel.PlacementBlocked && referencePanel.IsReadableFromHead);
        }
        ResearchPanelMotion recoveryPanel;
        bool recoveryWasNeeded, recoveryPlaced;
        float recoveryTransitionSince = float.NegativeInfinity, fallbackSince = float.NegativeInfinity;
        const float FallbackDwellSeconds = .75f;
        string placedInstruction;
        bool recoverTaskOnResume;
        readonly List<ResearchUIAction> placementActions = new List<ResearchUIAction>();
        readonly List<ResearchSlateFocus> placementFocus = new List<ResearchSlateFocus>();
        public bool PanelPlacementBusy
        {
            get
            {
                if ((mainPanel && mainPanel.IsDragging) || (referencePanel && referencePanel.IsDragging) || (ReferenceViewer && ReferenceViewer.ControlHeld) || (referenceTitleScroll && referenceTitleScroll.Held)) return true;
                GetComponentsInChildren(false, placementActions);
                foreach (var action in placementActions) if (action.PointerHeld) return true;
                GetComponentsInChildren(false, placementFocus);
                foreach (var focus in placementFocus) if (focus.PointerHeld) return true;
                return controller && controller.PanelPlacementBusy;
            }
        }
        public event Action<bool, string> ReferenceVisibilityChanged;
        bool observedReferenceVisible;
        public bool IsReferenceActuallyVisible => controller && controller.ReferenceOpen && !controller.ReferenceMinimized
            && controller.Flow?.Paused == false && controller.FloorReady && string.IsNullOrEmpty(controller.Error)
            && referencePanel && referencePanel.gameObject.activeInHierarchy && !referencePanel.PlacementBlocked
            && referencePanel.IsReadableFromHead
            && !string.IsNullOrEmpty(controller.ReferenceSection) && controller.ReferenceAvailable(controller.ReferenceSection)
            && ReferenceViewer && ReferenceViewer.IsReady && ReferenceViewer.Status != "Blocked"
            && referencePanel.GetComponent<Canvas>().enabled && referenceExpanded.activeInHierarchy && referenceGroup.alpha > .01f;
        public void NotifyReferenceVisibility(string reason)
        {
            bool visible = IsReferenceActuallyVisible;
            if (visible == observedReferenceVisible) return;
            observedReferenceVisible = visible; ReferenceVisibilityChanged?.Invoke(visible, reason);
        }
        public void OnSpatialDiscontinuity(string reason)
        {
            if (mainPanel && mainPanel.gameObject.activeInHierarchy) mainPanel.Recenter(false, false, onlyIfNeeded: true, reason: reason);
            if (controller && controller.ReferenceOpen && referencePanel && referencePanel.gameObject.activeInHierarchy)
                referencePanel.Recenter(false, false, onlyIfNeeded: true, reason: reason);
            NotifyReferenceVisibility(reason);
        }
        [SerializeField] TMP_Text title, body, helperText, referenceTitle, referenceBody, pillText;
        [SerializeField] RectTransform actionsRoot, checklist, referenceContent;
        [SerializeField] GameObject mainContent, mainPill, referenceExpanded, referencePill, mainToolbar;
        [SerializeField] TMP_Text[] stepLabels, stepDetails, tabLabels;
        [SerializeField] RectTransform[] stepRows;
        [SerializeField] TrainingRoundedGraphic[] stepBackgrounds, tabBackgrounds;
        [SerializeField] GameObject[] stepChecks;
        [SerializeField] Button[] actions;
        [SerializeField] TMP_Text[] actionLabels;
        [SerializeField] ScrollRect scroll;
        ResearchReaderTitleScroll referenceTitleScroll;
        TMP_Text referenceMetadata;
        string referenceTitleIdentity;
        [SerializeField] CanvasGroup transitionShade;
        [SerializeField] CanvasGroup mainGroup, referenceGroup;
        [SerializeField] GameObject positiveIcon, negativeIcon;
        readonly Dictionary<string, float> scrollPositions = new Dictionary<string, float>();
        static readonly string[] Steps = { "movement", "select", "handling", "reference", "inspect" };
        static readonly string[] WelcomeKeys = { "welcome.item.practice", "welcome.item.tasks", "welcome.safety" };
        static string[] Sections => ResearchReferenceScheme.Active;
        static readonly Color Background = ResearchUITheme.Glass, Card = ResearchUITheme.Elevated, Accent = ResearchUITheme.Blue;
        static readonly Color Ink = ResearchUITheme.Ink, Muted = ResearchUITheme.Muted, Positive = ResearchUITheme.Success;
        // Minimized panels collapse to a compact pill at the panel's own position, never a
        // large card floating detached across the FOV. Sized once here so the four call
        // sites that build/resize the pills cannot drift apart again.
        static readonly Vector2 MainPillSize = new Vector2(260, 76);
        // Below-content band holding the move bar. The panel root (= Meta ray surface via BoundsClipper) must
        // contain every toolbar control, so content sits ToolbarBand/2 above the root pivot.
        const float ToolbarBand = 96, HandleWidth = 320, HandleHeight = 80;
        const float TutorialDemoBand = 324; // Includes the rotated artwork plus a stable gap before the instruction.
        static readonly Vector2 ReferencePillSize = new Vector2(300, 76);
        static readonly Vector2 RecoveryButtonSize = new Vector2(360, 80);
        TMP_Text sectionHeading, waitingLabel, arrivalLabel, pageLabel, safetyLabel, tutorialProgress;
        Button helpUtility, stopUtility;
        bool mainPauseEligible;
        // Tutorial coach: one current action, animated control demo, reserved success line, practice-object rings.
        RectTransform tutorialDemo, tutorialSuccess, illustratedInstruction;
        TMP_Text successLabel, retainedLabel, referenceTutorialProgress;
        TMP_Text referenceTutorialCue;
        float tutorialCueHeight = 52;
        Vector2 previousReferenceLayoutSize;
        bool referenceLayoutNeedsRevalidation;
        string previousReferenceKey;
        string ReferenceKey => (controller?.Record?.session_id ?? "fixture") + ":" + controller?.Record?.run_id + ":" + controller?.Language + ":" + (controller?.Flow?.IsTutorial == true ? "Tutorial" : controller?.Flow?.CurrentTask) + ":" + controller?.ReferenceSection;
        bool TutorialReader => controller && controller.Flow?.IsTutorial == true && controller.ReferenceOpen;
        bool SessionEnded => controller && (controller.Record?.session_state == RegistryState.Aborted || controller.Record?.session_state == RegistryState.Invalid);
        bool SessionWriterUnavailable => controller && controller.Record?.session_state == RegistryState.InProgress
            && controller.Logger?.State != VRMaintenanceResearch.HandsLabQuest3.Logging.HandsLabLogState.Active;
        float TutorialReaderBand => TutorialReader ? tutorialCueHeight + 152 : 0;
        CanvasGroup instructionGroup;
        Coroutine confirmationAnimation;
        float successUntil = float.NegativeInfinity;
        string shownFact, demoKind, confirmingFact;
        bool highlightPractice;
        ResearchTutorialSequenceGraphic tutorialSequence;
        readonly List<RectTransform> practiceRings = new List<RectTransform>();
        const float Pad = ResearchUITheme.PanelPadding, DotCheckScale = .6f;
        static readonly Color DemoIdle = new Color(.27f, .38f, .51f), DemoHot = ResearchUITheme.Warning;
        Button previousPage, nextPage;
        RectTransform technicalOptions, technicalRows;
        ResearcherSetupView questSetup;
        Button operatorEntry, technicalDetails;
        bool setupDetailsRequested, setupDetailsVisible;
        TMP_Text setupBrand;
        TrainingRoundedGraphic taskToolbarSurface;
        CanvasGroup arrivalGroup;
        Coroutine arrivalAnimation, referenceClose;
        Coroutine helperDismiss;
        RectTransform welcomeItems, checkpointSteps;
        bool previousMinimized;
        Vector3 savedTaskPosition;
        Quaternion savedTaskRotation;
        bool hasTaskPose;
        bool savedTaskMinimized;
        // Fixed-UI policy: the Reader keeps the pose it was first given in the current task/tutorial and reopens there.
        Vector3 readerAnchorPosition;
        Quaternion readerAnchorRotation;
        string readerAnchorKey;
        // Reader distance preset (ResearchUIPolicy.ReaderDistanceVersion): closer toggles to a validated nearer pose; the
        // anchored pose is kept so the same control always returns to it. Reset when a new anchor is established.
        bool readerCloser;
        Pose readerDefaultPose;
        int readerDistanceChanges;
        static float[] Floats(Vector3 v) => new[] { v.x, v.y, v.z };
        bool ReaderDistanceControl => controller && ResearchUIPolicy.ReaderDistanceAllowed(controller.IsTechnical) && !controller.ReferenceMinimized;
        string ReaderAnchorKey => controller?.Flow == null ? null : controller.Flow.IsTaskActive ? "task:" + controller.Flow.CurrentTask
            : controller.Flow.IsTutorial ? "tutorial" : controller.Flow.State.ToString();
        Vector2 expandedMainSize = new Vector2(1000, 620);
        void Awake()
        {
            ApplyVisualTheme();
            // Meta's ray cursor shows its hover colour over any panel surface, so two idle rays read as two bright white
            // dots. Quiet the hover ring only; select colour/size and button hover tint keep Meta's feedback intact.
            foreach (var cursor in FindObjectsByType<Oculus.Interaction.RayInteractorCursorVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cursor.HoverColor = new Color(1, 1, 1, .3f);
        }

        public void ApplyVisualTheme()
        {
            if (!mainContent || !referenceExpanded) return;
            foreach (var graphic in GetComponentsInChildren<TrainingRoundedGraphic>(true))
            {
                if (graphic.name == "ContentSurface") { graphic.color = ResearchUITheme.Content; ResearchUITheme.Surface(graphic, false, 28); }
                if (graphic.name == "Surface" || graphic.name == "Kiosk") { graphic.color = Background; ResearchUITheme.Surface(graphic, true, 30); }
                else if (graphic.name == "Face") { graphic.color = ResearchUITheme.Elevated; ResearchUITheme.Surface(graphic); }
            }
            foreach (var button in GetComponentsInChildren<Button>(true)) ResearchUITheme.Button(button, ResearchButtonVariant.Secondary);
            foreach (var text in GetComponentsInChildren<TMP_Text>(true)) { text.color = Ink; text.lineSpacing = 7; }
            title.fontStyle = referenceTitle.fontStyle = FontStyles.Bold;
            foreach (var detail in stepDetails) detail.color = Muted;
            if (!sectionHeading) sectionHeading = referenceExpanded.transform.Find("SectionHeading")?.GetComponent<TMP_Text>();
            if (!sectionHeading) sectionHeading = Label(referenceExpanded.transform, "SectionHeading", new Vector2(168, 151), new Vector2(680, 62), "", 34);
            sectionHeading.fontStyle = FontStyles.Bold;
            if (!waitingLabel) waitingLabel = mainContent.transform.Find("WaitingStatus")?.GetComponent<TMP_Text>();
            if (!waitingLabel) waitingLabel = Label(mainContent.transform, "WaitingStatus", new Vector2(0, -225), new Vector2(820, 60), "", 27);
            waitingLabel.alignment = TextAlignmentOptions.Center; waitingLabel.color = Muted;
            waitingLabel.gameObject.SetActive(false);
            foreach (var item in new[] { mainContent.transform, referenceExpanded.transform })
            {
                var divider = item.Find("HeaderRule");
                if (!divider) divider = Round(item, "HeaderRule", new Vector2(0, 184), new Vector2(item == mainContent.transform ? 872 : 1060, 1.5f), new Color(.7f, .86f, 1, .26f), 0).transform;
            }
            referenceExpanded.GetComponent<RectTransform>().sizeDelta = new Vector2(1180, 710);
            referenceExpanded.transform.Find("Surface").GetComponent<RectTransform>().sizeDelta = new Vector2(1180, 710);
            referenceExpanded.transform.Find("Toolbar").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 308);
            if (!referenceTitleScroll)
            {
                referenceTitle.rectTransform.anchoredPosition = new Vector2(0, 218);
                referenceTitle.fontSize = 44;
            }
            for (int i = 0; i < tabLabels.Length; i++)
            {
                var rt = tabLabels[i].transform.parent.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(275, 86); rt.anchoredPosition = new Vector2(-403, 110 - i * 106);
            }
            var sr = scroll.GetComponent<RectTransform>(); sr.anchoredPosition = new Vector2(166, -97); sr.sizeDelta = new Vector2(720, 410);
            scroll.viewport.sizeDelta = new Vector2(680, 410);
            scroll.verticalScrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(24, 410);
            scroll.verticalScrollbar.transform.Find("Track").GetComponent<RectTransform>().sizeDelta = new Vector2(12, 410);
            referenceBody.fontSize = 32; referenceBody.lineSpacing = 13;
            helperText.fontSize = 24; helperText.rectTransform.sizeDelta = new Vector2(274, 156); helperText.color = Muted;
            var arrival = transform.Find("ArrivalCue");
            if (!arrival)
            {
                var rt = Rect(transform, "ArrivalCue", Vector2.zero, new Vector2(620, 84)); rt.localScale = Vector3.one * .001f;
                var canvas = rt.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 8;
                var glass = Round(rt, "Surface", Vector2.zero, rt.sizeDelta, Background, 40); ResearchUITheme.Surface(glass, false, 40);
                arrivalLabel = Label(rt, "Title", Vector2.zero, new Vector2(570, 65), "", 32); arrivalLabel.alignment = TextAlignmentOptions.Center;
                arrivalGroup = rt.gameObject.AddComponent<CanvasGroup>(); arrival = rt;
            }
            else { arrivalGroup = arrival.GetComponent<CanvasGroup>(); arrivalLabel = arrival.GetComponentInChildren<TMP_Text>(); }
            arrivalGroup.alpha = 0; arrivalGroup.blocksRaycasts = false;
            PrepareSecondPassSurfaces();
            PrepareSpatialSlates();
            PrepareSimplifiedSurfaces();
            PrepareStoryboardSurfaces();
            PrepareTutorialCoach();
            PrepareIllustratedReference();
            PrepareRecoveryUtility();
            checkpointSteps = mainContent.transform.Find("CheckpointSteps") as RectTransform;
            if (!checkpointSteps)
            {
                checkpointSteps = Rect(mainContent.transform, "CheckpointSteps", Vector2.zero, new Vector2(632, 240));
                string[] kinds = { "headset", "logger", "researcher" };
                for (int i = 0; i < 3; i++)
                {
                    var row = Rect(checkpointSteps, "CheckpointStep_" + i, Vector2.zero, new Vector2(632, 64));
                    DrawIcon(row, kinds[i], Vector2.zero);
                    Label(row, "Label", Vector2.zero, new Vector2(540, 64), "", 28).alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
            if (!mainContent.transform.Find("WaitingResearcherIcon")) DrawIcon(mainContent.transform, "researcher", Vector2.zero, "WaitingResearcherIcon");
        }

        void PrepareIllustratedReference()
        {
            illustratedInstruction = referenceExpanded.transform.Find("IllustratedInstruction") as RectTransform;
            if (illustratedInstruction) { LayoutIllustratedReference(); return; }
            illustratedInstruction = Rect(referenceExpanded.transform, "IllustratedInstruction", new Vector2(0, -112), new Vector2(704, 452));
            void Stroke(Transform parent, string name, Vector2 from, Vector2 to, Color color, float thickness = 4)
            {
                var delta = to - from;
                Round(parent, name, (from + to) / 2, new Vector2(delta.magnitude, thickness), color, 2).transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            for (int i = 0; i < 4; i++)
            {
                var frame = Rect(illustratedInstruction, "Frame_" + (i + 1), new Vector2(i % 2 == 0 ? -178 : 178, i < 2 ? 116 : -116), new Vector2(344, 220));
                var panel = Rect(frame, "Panel", new Vector2(i == 2 ? -14 : -44, 24), new Vector2(150, 80));
                Round(panel, "Surface", Vector2.zero, panel.sizeDelta, ResearchUITheme.Elevated, 10);
                Round(panel, "Heading", new Vector2(0, 22), new Vector2(112, 6), Ink, 3);
                Round(panel, "Text", new Vector2(-12, 6), new Vector2(88, 5), Muted, 2);
                Round(panel, "Handle", new Vector2(0, -28), new Vector2(88, 12), DemoHot, 6);
                var pad = Rect(frame, "Controller", new Vector2(103, 22), new Vector2(58, 88));
                Round(pad, "Body", new Vector2(0, -10), new Vector2(32, 58), Ink, 16).transform.localRotation = Quaternion.Euler(0, 0, -12);
                Round(pad, "Head", new Vector2(0, 20), new Vector2(52, 38), Ink, 19);
                Round(pad, "Trigger", new Vector2(-25, 20), new Vector2(12, 22), i == 1 || i == 2 ? DemoHot : Muted, 6);
                var hand = Rect(frame, "Hand", new Vector2(103, 22), new Vector2(58, 88));
                float separation = i == 1 || i == 2 ? 8 : 22;
                Round(hand, "Thumb", new Vector2(-separation / 2, 0), new Vector2(26, 26), Ink, 13);
                Round(hand, "Index", new Vector2(separation / 2, 22), new Vector2(26, 26), i == 1 || i == 2 ? DemoHot : Ink, 13);
                if (i < 3) Stroke(frame, "SelectionRay", new Vector2(75, 22), new Vector2(panel.anchoredPosition.x, -4), i == 0 ? Ink : DemoHot);
                if (i == 2)
                {
                    Stroke(frame, "MoveDirection", new Vector2(-72, 78), new Vector2(40, 78), DemoHot, 6);
                    Stroke(frame, "ArrowA", new Vector2(24, 88), new Vector2(40, 78), DemoHot, 6);
                    Stroke(frame, "ArrowB", new Vector2(24, 68), new Vector2(40, 78), DemoHot, 6);
                }
                Label(frame, "Caption", new Vector2(0, -70), new Vector2(308, 64), "", 26).alignment = TextAlignmentOptions.TopLeft;
            }
            illustratedInstruction.gameObject.SetActive(false);
            LayoutIllustratedReference();
        }

        void LayoutIllustratedReference()
        {
            illustratedInstruction.sizeDelta = new Vector2(704, 290); illustratedInstruction.anchoredPosition = new Vector2(0, -68);
            for (int i = 0; i < 4; i++)
            {
                var frame = (RectTransform)illustratedInstruction.Find("Frame_" + (i + 1));
                frame.anchoredPosition = new Vector2(-264 + i * 176, 0); frame.sizeDelta = new Vector2(164, 270);
                foreach (var key in new[] { "Card", "Number" }) frame.Find(key)?.gameObject.SetActive(false);
                var panel = (RectTransform)frame.Find("Panel"); panel.anchoredPosition = new Vector2(i >= 2 ? -14 : -35, 34); panel.localScale = Vector3.one * .58f;
                foreach (var key in new[] { "Controller", "Hand" })
                { var control = (RectTransform)frame.Find(key); control.anchoredPosition = new Vector2(55, 34); control.localScale = Vector3.one * .64f; }
                var ray = frame.Find("SelectionRay") as RectTransform;
                if (ray)
                { var from = new Vector2(38, 34); var to = new Vector2(panel.anchoredPosition.x, 18); var delta = to - from; ray.anchoredPosition = (from + to) / 2; ray.sizeDelta = new Vector2(delta.magnitude, 3); ray.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg); }
                var caption = frame.Find("Caption").GetComponent<TMP_Text>(); caption.fontSize = 24; caption.rectTransform.sizeDelta = new Vector2(164, 92); caption.rectTransform.anchoredPosition = new Vector2(0, -65);
            }
            for (int i = 0; i < 3; i++)
            {
                if (illustratedInstruction.Find("Order_" + (i + 1))) continue;
                var order = Rect(illustratedInstruction, "Order_" + (i + 1), new Vector2(-176 + i * 176, 108), new Vector2(104, 20));
                Round(order, "Shaft", Vector2.zero, new Vector2(104, 4), Muted, 2);
                Round(order, "TipA", new Vector2(44, 5), new Vector2(18, 4), Muted, 2).transform.localRotation = Quaternion.Euler(0, 0, -35);
                Round(order, "TipB", new Vector2(44, -5), new Vector2(18, 4), Muted, 2).transform.localRotation = Quaternion.Euler(0, 0, 35);
            }
        }

        void PrepareTutorialCoach()
        {
            tutorialDemo = mainContent.transform.Find("TutorialDemo") as RectTransform;
            if (!tutorialDemo)
            {
                tutorialDemo = Rect(mainContent.transform, "TutorialDemo", Vector2.zero, new Vector2(200, 200));
                // Own canvas: the looping demo rebuilds only this small batch, never the whole slate.
                tutorialDemo.gameObject.AddComponent<Canvas>();
                var card = Round(tutorialDemo, "Card", Vector2.zero, new Vector2(200, 200), ResearchUITheme.Elevated, 24);
                ResearchUITheme.Surface(card, false, 24).edgeAlpha = .12f;
                Label(tutorialDemo, "ControlName", new Vector2(0, -80), new Vector2(184, 32), "", 20).alignment = TextAlignmentOptions.Center;
                var pad = Rect(tutorialDemo, "Controller", new Vector2(0, 14), new Vector2(160, 180));
                pad.localScale = Vector3.one * .8f;
                Round(pad, "Handle", new Vector2(10, -46), new Vector2(56, 92), DemoIdle, 26).transform.localRotation = Quaternion.Euler(0, 0, -12);
                Round(pad, "Grip", new Vector2(36, -40), new Vector2(14, 52), DemoIdle, 7).transform.localRotation = Quaternion.Euler(0, 0, -12);
                Round(pad, "Head", new Vector2(0, 30), new Vector2(112, 112), DemoIdle, 56);
                Round(pad, "Trigger", new Vector2(-62, 40), new Vector2(18, 40), DemoIdle, 9);
                Round(pad, "StickBase", new Vector2(0, 36), new Vector2(48, 48), ResearchUITheme.Content, 24);
                Round(pad, "StickCap", new Vector2(0, 36), new Vector2(28, 28), DemoIdle, 14);
                var hand = Rect(tutorialDemo, "Hand", Vector2.zero, new Vector2(160, 160));
                Round(hand, "Thumb", new Vector2(-30, -10), new Vector2(34, 34), DemoIdle, 17);
                Round(hand, "Index", new Vector2(30, 10), new Vector2(34, 34), DemoIdle, 17);
                var person = Rect(tutorialDemo, "Person", Vector2.zero, new Vector2(160, 160));
                Round(person, "Skull", new Vector2(0, 26), new Vector2(64, 64), DemoIdle, 32);
                Round(person, "Shoulders", new Vector2(0, -44), new Vector2(110, 44), DemoIdle, 22);
                var arrow = Rect(tutorialDemo, "Arrow", Vector2.zero, new Vector2(60, 60));
                Round(arrow, "Shaft", new Vector2(0, -6), new Vector2(6, 38), DemoHot, 3);
                Round(arrow, "HeadA", new Vector2(-7, 10), new Vector2(6, 22), DemoHot, 3).transform.localRotation = Quaternion.Euler(0, 0, -40);
                Round(arrow, "HeadB", new Vector2(7, 10), new Vector2(6, 22), DemoHot, 3).transform.localRotation = Quaternion.Euler(0, 0, 40);
            }
            tutorialSuccess = mainContent.transform.Find("TutorialSuccess") as RectTransform;
            if (!tutorialSuccess)
            {
                tutorialSuccess = Rect(mainContent.transform, "TutorialSuccess", Vector2.zero, new Vector2(690, 48));
                var chip = Round(tutorialSuccess, "Chip", Vector2.zero, new Vector2(690, 48), ResearchUITheme.Glass, 24);
                ResearchUITheme.Surface(chip, false, 24).edgeAlpha = .3f;
                StatusIcon(tutorialSuccess, true, Vector2.zero, .34f);
                Label(tutorialSuccess, "Label", Vector2.zero, new Vector2(600, 46), "", 25).alignment = TextAlignmentOptions.Left;
            }
            successLabel = tutorialSuccess.Find("Label").GetComponent<TMP_Text>();
            retainedLabel = mainContent.transform.Find("TutorialRetained")?.GetComponent<TMP_Text>();
            if (!retainedLabel) retainedLabel = Label(mainContent.transform, "TutorialRetained", Vector2.zero, new Vector2(690, 48), "", 24);
            retainedLabel.gameObject.SetActive(false);
            tutorialDemo.Find("InstructionArt")?.gameObject.SetActive(false);
            tutorialSequence = tutorialDemo.Find("Sequence")?.GetComponent<ResearchTutorialSequenceGraphic>();
            if (!tutorialSequence)
                tutorialSequence = Rect(tutorialDemo, "Sequence", Vector2.zero, new Vector2(280, 270)).gameObject.AddComponent<ResearchTutorialSequenceGraphic>();
            tutorialSequence.raycastTarget = false;
            foreach (var key in new[] { "Controller", "Hand", "Person", "Arrow" }) tutorialDemo.Find(key).gameObject.SetActive(false);
            instructionGroup = body.GetComponent<CanvasGroup>();
            if (!instructionGroup) instructionGroup = body.gameObject.AddComponent<CanvasGroup>();
            instructionGroup.blocksRaycasts = false;
            tutorialDemo.gameObject.SetActive(false); tutorialSuccess.gameObject.SetActive(false);
        }

        // Trainer Inspect samples show their result sentence in the feedback line instead of the generic completed-fact text.
        string trainerResultKey;
        float trainerResultUntil = float.NegativeInfinity;
        // Quest 16 Oct 18:02: the 'not ready' sample stayed under the next instruction (insert wrong way) for its whole 97 s.
        // The result gets a reading window, then the line clears so it never reads as a verdict on the new step.
        const float TrainerResultSeconds = 8f;
        string TrainerResult => trainerResultKey != null && Time.unscaledTime < trainerResultUntil ? trainerResultKey : null;
        public void ShowTrainerResult(string key) { trainerResultKey = key; trainerResultUntil = Time.unscaledTime + TrainerResultSeconds; ShowTutorialConfirmation(); }
        string CompletionText() => NoticeActive ? noticeText : TrainerResult != null ? Text(TrainerResult)
            : string.Format(Text("tutorial.completed_fact"), Text("tutorial.fact." + CompletedTutorialFact));

        // Quest 16 Oct: rejections reached the log with no visible response, so a not-yet attempt at a skill of the current group
        // gets a notice. Quest 16 Oct 18:02: grab/release are part of every later physical skill, and "already done, no need to
        // repeat" during the module insert read as "do not grip". Duplicates and other groups' skills stay in the log only.
        const float NoticeSeconds = 3f;
        string noticeKey, noticeText;
        float noticeUntil = float.NegativeInfinity;
        bool NoticeActive => noticeKey != null && TrainerResult == null && Time.unscaledTime < noticeUntil;
        Color FeedbackColor => NoticeActive ? Ink : Positive;
        bool FeedbackCheckmark => !NoticeActive && (TrainerResult == null || TrainerResult.EndsWith(".pass"));

        public void TutorialActionNotCounted(string fact, string reason)
        {
            if (reason == "safety_or_tracking_invalid" || reason == "already_credited" || !controller.TutorialGroupRequires(fact)
                || fact == "teleport" || fact == "snap" || fact == "roomscale") return;
            ShowNotice("tutorial.notice.not_yet", "TUTORIAL_NOT_COUNTED_FEEDBACK", fact, reason);
        }

        // A neutral mechanical hint for the armed trainer skill, measured from the part's pose; never a credit.
        public void TrainingHint(string key, string fact) => ShowNotice(key, "TRAINING_HINT_FEEDBACK", fact, key);

        void ShowNotice(string key, string evt, string fact, string reason)
        {
            if (!tutorialSuccess) return;
            // The Inspect sample result belongs to the previous step; left in place it hid every notice for the next one.
            trainerResultKey = null;
            if (key == noticeKey && Time.unscaledTime < noticeUntil) { noticeUntil = Time.unscaledTime + NoticeSeconds; return; }
            noticeKey = key; noticeUntil = Time.unscaledTime + NoticeSeconds;
            noticeText = Text(key);
            successLabel.font = Font; successLabel.color = FeedbackColor; successLabel.text = noticeText;
            tutorialSuccess.gameObject.SetActive(true); tutorialSuccess.Find("Checkmark").gameObject.SetActive(false);
            controller.Log(evt, "training." + fact, "RendererTextApplied",
                after: new { fact, reason, text = noticeText, frame = Time.frameCount, human_seen = (bool?)null }, source: "System");
        }

        public void TutorialFactCompleted(string fact)
        {
            noticeKey = null;
            if (fact != "inspect" && fact != "inspect_fail") trainerResultKey = null;
            successUntil = Time.unscaledTime + .25f;
            factFeedbackUntil = Time.unscaledTime + FactFeedbackSeconds;
            confirmingFact = fact;
            ShowTutorialConfirmation();
            var group = tutorialSuccess.GetComponent<CanvasGroup>();
            if (!group) group = tutorialSuccess.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            FadeIn(group, ref confirmationAnimation);
        }

        public bool TutorialPromptPresented(string fact) => TutorialActionReady(fact)
            && (TutorialReader ? referencePanel.IsReadableFromHead : mainPanel.IsReadableFromHead);

        public bool TutorialActionReady(string fact) => shownFact == fact &&
            (TutorialReader ? referencePanel && referencePanel.gameObject.activeSelf && !referencePanel.PlacementBlocked && referencePanel.GetComponent<Canvas>().enabled
                && referenceGroup.alpha > .01f
                && (controller.ReferenceMinimized ? fact == "restore" && referencePill.activeSelf : referenceTutorialCue && referenceTutorialCue.gameObject.activeSelf)
            : body && body.gameObject.activeSelf && mainContent.activeSelf && mainPanel.gameObject.activeSelf && !mainPanel.PlacementBlocked && mainPanel.GetComponent<Canvas>().enabled
                && mainGroup.alpha > .01f && instructionGroup.alpha > .01f);
        public void CancelTutorialConfirmation() { successUntil = float.NegativeInfinity; tutorialSuccess.gameObject.SetActive(false); }

        string CompletedTutorialFact => controller && controller.TutorialCompletedFactCount > 0
            ? controller.TutorialLastCompletedFact ?? confirmingFact : null;
        // ADVISOR_UI_AMENDMENT_20261014 / capture C06: "done" feedback is brief so it never competes with the next instruction.
        const float FactFeedbackSeconds = 3.5f;
        float factFeedbackUntil = float.NegativeInfinity;
        bool TutorialFeedbackVisible => NoticeActive || TrainerResult != null || (!string.IsNullOrEmpty(CompletedTutorialFact) && Time.unscaledTime < factFeedbackUntil);

        void ShowTutorialConfirmation()
        {
            // Completed-action feedback stays in its reserved secondary line; the next instruction remains visible.
            successLabel.font = Font; successLabel.fontSize = 25; successLabel.color = FeedbackColor;
            successLabel.text = CompletionText();
            tutorialSuccess.gameObject.SetActive(TutorialFeedbackVisible); tutorialSuccess.Find("Checkmark").gameObject.SetActive(FeedbackCheckmark);
            RecordTutorialFeedbackApplied();
        }

        void RecordTutorialFeedbackApplied()
        {
            string action = controller?.TutorialLastCreditActionId;
            if (NoticeActive || string.IsNullOrEmpty(CompletedTutorialFact) || string.IsNullOrEmpty(action) || feedbackApplied == action) return;
            if (controller.Log("TUTORIAL_FEEDBACK_APPLIED", "tutorial." + CompletedTutorialFact, "RendererTextApplied",
                after: new { fact = CompletedTutorialFact, instruction_epoch = controller.TutorialLastCreditEpoch,
                    count = controller.TutorialCompletedFactCount, text = successLabel.text, frame = Time.frameCount,
                    unscaled_seconds = Time.unscaledTime, human_seen = (bool?)null }, source: "System", actionId: action)) feedbackApplied = action;
        }

        void Update()
        {
            if (Application.isPlaying && inspectionVisibility == null && controller && controller.Flow?.InspectPassed.HasValue == true
                && !controller.InspectionFeedbackVisible && !string.IsNullOrEmpty(controller.InspectionId))
                inspectionVisibility = StartCoroutine(ObserveInspectionFeedback(controller.InspectionId));
            SyncReaderVisibility();
            RevalidateReferenceLayoutIfReady();
            if (noticeKey != null && !NoticeActive) { noticeKey = null; if (tutorialSuccess && tutorialSuccess.gameObject.activeSelf) ShowTutorialConfirmation(); }
            if (tutorialSuccess && tutorialSuccess.gameObject.activeSelf && !TutorialFeedbackVisible) tutorialSuccess.gameObject.SetActive(false);
            RevalidateMainLayoutIfReady();
            NotifyReferenceVisibility("render_visibility");
            RefreshRecoveryUtility();
            if (!float.IsNegativeInfinity(successUntil) && Time.unscaledTime >= successUntil)
            { successUntil = float.NegativeInfinity; Refresh(); }
            UpdatePracticeHighlight();
        }

        static string DemoKind(string fact, bool hands) =>
            fact == "roomscale" ? "head" : hands ? (fact == "move" ? "pinch_up" : fact == "rotate" ? "pinch_rotate" : "pinch")
            : fact == "teleport" ? "stick_forward" : fact == "snap" ? "stick_side" : fact == "grab" || fact == "release" || ResearchTutorialSequenceGraphic.GripFact(fact) ? "grip"
            : fact == "move" ? "grip_up" : fact == "rotate" ? "grip_rotate" : fact == "reposition" ? "trigger_drag" : "trigger";

        void UpdateTutorialSequence()
        {
            tutorialSequence?.SetStep(controller ? controller.TutorialInstructionFact : null, controller && controller.TutorialUsesHands);
        }
        void UpdatePracticeHighlight()
        {
            bool show = highlightPractice && controller && controller.Flow?.IsTutorial == true && !controller.Flow.Paused && string.IsNullOrEmpty(controller.Error);
            var items = controller ? controller.trainingObjects : null;
            for (int i = 0; i < (items?.Length ?? 0); i++)
            {
                if (practiceRings.Count <= i)
                {
                    var rt = Rect(transform, "PracticeHighlight_" + i, Vector2.zero, new Vector2(200, 200)); rt.localScale = Vector3.one * .001f;
                    var canvas = rt.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                    var ring = Round(rt, "Ring", Vector2.zero, new Vector2(200, 200), DemoHot, 100); ResearchUITheme.Surface(ring, false, 100).outlineOnly = true;
                    practiceRings.Add(rt);
                }
                var item = items[i]; var marker = practiceRings[i];
                var renderer = item ? item.GetComponentInChildren<Renderer>() : null;
                bool visible = show && renderer && !item.IsHeld;
                if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
                if (!visible) continue;
                var bounds = renderer.bounds;
                float size = (Mathf.Max(bounds.size.x, bounds.size.z) + .1f) * 1000;
                marker.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y + .004f, bounds.center.z), Quaternion.Euler(90, 0, 0));
                marker.sizeDelta = new Vector2(size, size);
                var graphic = marker.GetChild(0).GetComponent<Graphic>();
                graphic.rectTransform.sizeDelta = marker.sizeDelta;
                graphic.color = new Color(DemoHot.r, DemoHot.g, DemoHot.b, .45f + .45f * ResearchMotion.Ease(Mathf.PingPong(Time.unscaledTime / .8f, 1)));
            }
        }

        void PrepareSecondPassSurfaces()
        {
            foreach (var expanded in new[] { mainContent.transform, referenceExpanded.transform })
            {
                if (!expanded.Find("ContentSurface"))
                {
                    var content = Round(expanded, "ContentSurface", Vector2.zero, Vector2.one, ResearchUITheme.Content, ResearchUITheme.RadiusMedium);
                    content.transform.SetSiblingIndex(1);
                    ResearchUITheme.Surface(content, false, ResearchUITheme.RadiusMedium).edgeAlpha = .08f;
                }
                if (!expanded.Find("ActionRule")) Round(expanded, "ActionRule", Vector2.zero, Vector2.one, new Color(.6f, .75f, .9f, .18f), 0);
            }
            var referenceContentSurface = referenceExpanded.transform.Find("ContentSurface").GetComponent<RectTransform>();
            mainToolbar.transform.Find("Minimize").GetComponent<ResearchUIAction>().labelKey = "action.minimize";
            var referenceMinimize = referenceExpanded.transform.Find("Toolbar/Minimize") ?? referenceExpanded.transform.Find("Minimize");
            referenceMinimize.GetComponent<ResearchUIAction>().labelKey = "action.minimize";
            referenceContentSurface.anchoredPosition = new Vector2(0, -52);
            referenceContentSurface.sizeDelta = new Vector2(1140, 574);
            referenceExpanded.transform.Find("ActionRule").gameObject.SetActive(false);
            if (!welcomeItems) welcomeItems = mainContent.transform.Find("WelcomeItems") as RectTransform;
            if (!welcomeItems)
            {
                welcomeItems = Rect(mainContent.transform, "WelcomeItems", new Vector2(0, -15), new Vector2(744, 260));
                for (int i = 0; i < 5; i++)
                {
                    var row = Rect(welcomeItems, "Learning_" + i, new Vector2(0, 104 - i * 52), new Vector2(744, 48));
                    Round(row, "Icon", new Vector2(-344, 0), new Vector2(12, 12), ResearchUITheme.Blue, 6);
                    Label(row, "Label", new Vector2(10, 0), new Vector2(650, 48), "", ResearchUITheme.Body);
                }
            }
            var kiosk = helperCanvas.transform.Find("Kiosk").GetComponent<RectTransform>();
            kiosk.sizeDelta = new Vector2(300, 230); kiosk.anchoredPosition = new Vector2(0, 64);
            var help = helperCanvas.transform.Find("Help").GetComponent<RectTransform>();
            help.anchoredPosition = new Vector2(0, -8); help.sizeDelta = new Vector2(280, 72);
            help.GetComponent<ResearchUIAction>().labelKey = "helper.action";
            helperText.rectTransform.anchoredPosition = new Vector2(0, -136);
            helperText.rectTransform.sizeDelta = new Vector2(390, 140);
            if (!helperCanvas.transform.Find("Bubble"))
            {
                var bubble = Round(helperCanvas.transform, "Bubble", new Vector2(0, -136), new Vector2(420, 156), ResearchUITheme.Content, ResearchUITheme.RadiusMedium);
                bubble.transform.SetSiblingIndex(helperText.transform.GetSiblingIndex());
                ResearchUITheme.Surface(bubble, true, ResearchUITheme.RadiusMedium);
            }
            helperText.gameObject.SetActive(false); helperCanvas.transform.Find("Bubble").gameObject.SetActive(false);
        }
        void PrepareSpatialSlates()
        {
            foreach (var expanded in new[] { mainContent.transform, referenceExpanded.transform })
            {
                var panel = expanded.parent.GetComponent<ResearchPanelMotion>();
                var toolbar = expanded.Find("Toolbar");
                var focus = expanded.parent.GetComponent<ResearchSlateFocus>();
                if (!focus) focus = expanded.parent.gameObject.AddComponent<ResearchSlateFocus>();
                focus.view = this;
                focus.toolbar = toolbar.GetComponent<CanvasGroup>();
                if (!focus.toolbar) focus.toolbar = toolbar.gameObject.AddComponent<CanvasGroup>();
                if (panel == referencePanel)
                {
                    focus.scroll = scroll;
                    if (!scroll.GetComponent<ResearchSlateFocus>()) scroll.gameObject.AddComponent<ResearchSlateFocus>();
                }
                foreach (var key in new[] { "DragHandle", "Minimize", "Recenter", "Close" })
                {
                    var control = toolbar.Find(key); if (!control) continue;
                    if (panel == mainPanel && key != "DragHandle") control.gameObject.SetActive(key != "Close");
                    var action = control.GetComponent<ResearchUIAction>();
                    if (action) action.labelKey = "";
                    var hint = control.GetComponent<ResearchControlHint>();
                    if (!hint) hint = control.gameObject.AddComponent<ResearchControlHint>();
                    hint.view = this;
                    hint.key = key == "DragHandle" ? "panel.handle" : "action." + key.ToLowerInvariant();
                    hint.label = control.Find("FocusLabel")?.GetComponent<TMP_Text>();
                    if (!hint.label) hint.label = Label(control, "FocusLabel", new Vector2(0, -60), new Vector2(290, 48), "", 24);
                    hint.label.alignment = TextAlignmentOptions.Center;
                    hint.label.gameObject.SetActive(false);
                }
                LayoutToolbar(toolbar, 0);
            }
            // Keep labeled Minimize/Close controls below the reader, beside the grab bar.
            var referenceToolbar = referenceExpanded.transform.Find("Toolbar");
            foreach (var key in new[] { "Minimize", "Close" })
            {
                var control = referenceToolbar.Find(key);
                if (!control) continue;
                control.SetParent(referenceExpanded.transform, false);
                var group = control.GetComponent<CanvasGroup>();
                if (group) { group.alpha = 1; group.interactable = group.blocksRaycasts = true; }
                var hint = control.GetComponent<ResearchControlHint>();
                if (hint) { hint.label.gameObject.SetActive(false); hint.enabled = false; }
            }
            var distance = referenceToolbar.Find("Recenter");
            if (distance) { distance.GetComponent<ResearchUIAction>().command = "reference_distance"; distance.gameObject.SetActive(ReaderDistanceControl); }
            referenceExpanded.GetComponent<RectTransform>().sizeDelta = new Vector2(840, 630);
            referenceExpanded.transform.Find("Surface").GetComponent<RectTransform>().sizeDelta = new Vector2(840, 630);
            var content = referenceExpanded.transform.Find("ContentSurface").GetComponent<RectTransform>();
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(816, 606);
            referenceExpanded.transform.Find("Toolbar").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -367);
            referenceExpanded.transform.Find("HeaderRule").gameObject.SetActive(false);
            if (!referenceTitleScroll)
            {
                referenceTitle.rectTransform.anchoredPosition = new Vector2(0, 255);
                referenceTitle.rectTransform.sizeDelta = new Vector2(756, 64); referenceTitle.fontSize = 36;
            }
            for (int i = 0; i < 4; i++)
            {
                var tab = tabLabels[i].transform.parent.GetComponent<RectTransform>();
                tab.anchoredPosition = new Vector2((i - 1.5f) * 196, 177); tab.sizeDelta = new Vector2(180, 80);
                tabLabels[i].fontSize = 22; tabLabels[i].lineSpacing = 0;
            }
            sectionHeading.rectTransform.anchoredPosition = new Vector2(0, 102);
            sectionHeading.rectTransform.sizeDelta = new Vector2(750, 54); sectionHeading.fontSize = 28;
            scroll.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -65);
            scroll.GetComponent<RectTransform>().sizeDelta = new Vector2(750, 250);
            scroll.viewport.anchoredPosition = Vector2.zero; scroll.viewport.sizeDelta = new Vector2(740, 250);
            referenceBody.fontSize = 32; referenceBody.lineSpacing = 8;
            var bar = scroll.verticalScrollbar;
            bar.interactable = true; bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(387, 0);
            bar.GetComponent<RectTransform>().sizeDelta = new Vector2(6, 250);
            foreach (var g in bar.GetComponentsInChildren<Graphic>()) g.raycastTarget = g.gameObject.name == "Track" || g.transform == bar.handleRect;
            bar.transform.Find("Track").GetComponent<RectTransform>().sizeDelta = new Vector2(4, 250);
            previousPage = referenceExpanded.transform.Find("PreviousPage")?.GetComponent<Button>();
            nextPage = referenceExpanded.transform.Find("NextPage")?.GetComponent<Button>();
            pageLabel = referenceExpanded.transform.Find("PagePosition")?.GetComponent<TMP_Text>();
            if (!previousPage)
            {
                previousPage = Button(referenceExpanded.transform, "PreviousPage", new Vector2(-250, -252), new Vector2(240, 64), "", "reference_previous", false);
                nextPage = Button(referenceExpanded.transform, "NextPage", new Vector2(250, -252), new Vector2(240, 64), "", "reference_next", false);
                pageLabel = Label(referenceExpanded.transform, "PagePosition", new Vector2(0, -252), new Vector2(160, 60), "", 26);
                pageLabel.alignment = TextAlignmentOptions.Center;
            }
            scroll.onValueChanged.RemoveListener(OnReferenceScroll);
            previousPage.GetComponent<RectTransform>().anchoredPosition = new Vector2(-250, -252);
            nextPage.GetComponent<RectTransform>().anchoredPosition = new Vector2(250, -252);
            pageLabel.rectTransform.anchoredPosition = new Vector2(0, -252);
            scroll.onValueChanged.AddListener(OnReferenceScroll);
            safetyLabel = mainContent.transform.Find("SafetyLine")?.GetComponent<TMP_Text>();
            if (!safetyLabel) safetyLabel = Label(mainContent.transform, "SafetyLine", Vector2.zero, new Vector2(680, 62), "", 24);
            safetyLabel.alignment = TextAlignmentOptions.Center;
            if (!mainContent.transform.Find("WaitingChip"))
            {
                var chip = Round(mainContent.transform, "WaitingChip", new Vector2(0, -200), new Vector2(400, 64), ResearchUITheme.Elevated, 30);
                chip.transform.SetSiblingIndex(waitingLabel.transform.GetSiblingIndex());
            }
            mainContent.transform.Find("WaitingChip").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -200);
            if (!mainContent.transform.Find("PauseIcon"))
            {
                var icon = Rect(mainContent.transform, "PauseIcon", new Vector2(0, 183), new Vector2(72, 72));
                Round(icon, "Left", new Vector2(-13, 0), new Vector2(9, 40), Muted, 4);
                Round(icon, "Right", new Vector2(13, 0), new Vector2(9, 40), Muted, 4);
            }
        }
        void PrepareSimplifiedSurfaces()
        {
            tutorialProgress = mainContent.transform.Find("TutorialProgress")?.GetComponent<TMP_Text>();
            if (!tutorialProgress) tutorialProgress = Label(mainContent.transform, "TutorialProgress", Vector2.zero, new Vector2(580, 42), "", 24);
            helpUtility = mainContent.transform.Find("HelpUtility")?.GetComponent<UnityEngine.UI.Button>();
            if (!helpUtility) helpUtility = Button(mainContent.transform, "HelpUtility", Vector2.zero, new Vector2(64, 64), "?", "pause", false);
            UtilityIcon(helpUtility, "pause", "action.pause_short");
            stopUtility = mainContent.transform.Find("StopUtility")?.GetComponent<UnityEngine.UI.Button>();
            if (!stopUtility) stopUtility = Button(mainContent.transform, "StopUtility", Vector2.zero, new Vector2(80, 80), "", "stop", false);
            UtilityIcon(stopUtility, "stop", "action.stop_short");
            // Existing kiosk becomes a small tabletop device, never a head-following card.
            helperCanvas.transform.localScale = Vector3.one * .0005f;
            foreach (var key in new[] { "Kiosk", "Face", "EyeLeft", "EyeRight" })
                helperCanvas.transform.Find(key).GetComponent<RectTransform>().anchoredPosition += new Vector2(0, -24);
            helperCanvas.transform.Find("Kiosk").GetComponent<RectTransform>().sizeDelta = new Vector2(300, 210);
            var bubble = helperCanvas.transform.Find("Bubble").GetComponent<RectTransform>();
            bubble.GetComponent<TrainingRoundedGraphic>().color = ResearchUITheme.Content;
            bubble.anchoredPosition = helperText.rectTransform.anchoredPosition = new Vector2(0, 290);
            bubble.sizeDelta = new Vector2(660, 210);
            helperText.rectTransform.sizeDelta = new Vector2(600, 180);
            helperText.fontSize = 42;
            if (!helperCanvas.transform.Find("DeviceHousing"))
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = new Color(.22f, .32f, .36f);
                foreach (var part in new[] { "DeviceHousing", "DeviceStand", "DeviceBase" })
                {
                    var housing = new GameObject(part, typeof(MeshFilter), typeof(MeshRenderer));
                    housing.transform.SetParent(helperCanvas.transform, false);
                    housing.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    housing.GetComponent<MeshRenderer>().sharedMaterial = material;
                    housing.transform.localPosition = part == "DeviceHousing" ? new Vector3(0, 20, 60) : part == "DeviceStand" ? new Vector3(0, -145, 60) : new Vector3(0, -245, 60);
                    housing.transform.localScale = part == "DeviceHousing" ? new Vector3(320, 230, 90) : part == "DeviceStand" ? new Vector3(70, 150, 70) : new Vector3(330, 30, 170);
                }
            }
            // Headset setup uses controllers; diagnostic hand modes remain on the researcher display.
            technicalOptions = mainContent.transform.Find("TechnicalOptions") as RectTransform;
            if (!technicalOptions)
            {
                technicalOptions = Rect(mainContent.transform, "TechnicalOptions", Vector2.zero, new Vector2(588, 56));
                foreach (var option in new[] { "language", "order" })
                    Button(technicalOptions, "Option_" + option, Vector2.zero, new Vector2(186, 56), "", "technical_" + option, false);
            }
            var inputOption = technicalOptions.Find("Option_input");
            if (inputOption) inputOption.gameObject.SetActive(false); // Existing serialized scenes retain the retired control.
            technicalOptions.gameObject.SetActive(false);
            operatorEntry = technicalOptions.Find("Operator Setup")?.GetComponent<UnityEngine.UI.Button>();
            if (!operatorEntry)
                operatorEntry = Button(technicalOptions, "Operator Setup", Vector2.zero, new Vector2(360, 64), "", "", false);
            technicalDetails = technicalOptions.Find("Details")?.GetComponent<UnityEngine.UI.Button>();
            if (!technicalDetails) technicalDetails = Button(technicalOptions, "Details", Vector2.zero, new Vector2(660, 64), "", "technical_details", false);
            operatorEntry.GetComponent<ResearchUIAction>().enabled = false;
            operatorEntry.onClick.RemoveAllListeners(); operatorEntry.onClick.AddListener(() => OpenResearcherSetup());
            scroll.verticalScrollbar.gameObject.SetActive(false);
            // ScrollRect remains the drag/thumbstick input surface; TMP renders whole pages.
            referenceBody.rectTransform.SetParent(scroll.viewport, false);
            referenceBody.rectTransform.anchorMin = Vector2.zero;
            referenceBody.rectTransform.anchorMax = Vector2.one;
            referenceBody.rectTransform.offsetMin = new Vector2(14, 12);
            referenceBody.rectTransform.offsetMax = new Vector2(-14, -12);
            referenceBody.overflowMode = TextOverflowModes.Page;
        }

        void BodySpacing(TMP_Text text)
        {
            // TMP spacing is additive font units; measure font metrics rather than use a CSS ratio.
            float target = language == "Thai" ? 1.5f : language == "Japanese" ? 1.45f : 1.4f;
            var face = text.font.faceInfo;
            text.lineSpacing = Mathf.Max(0, target * face.pointSize - face.lineHeight);
        }

        void PrepareStoryboardSurfaces()
        {
            setupBrand = mainContent.transform.Find("SetupBrand")?.GetComponent<TMP_Text>();
            if (!setupBrand) setupBrand = Label(mainContent.transform, "SetupBrand", Vector2.zero, new Vector2(700, 58), "VR Maintenance Research", 38);
            setupBrand.fontStyle = FontStyles.Bold; setupBrand.font = englishFont;
            setupBrand.rectTransform.sizeDelta = new Vector2(700, 72);
            technicalRows = mainContent.transform.Find("TechnicalReadiness") as RectTransform;
            if (!technicalRows)
            {
                technicalRows = Rect(mainContent.transform, "TechnicalReadiness", Vector2.zero, new Vector2(690, 380));
                string[] keys = { "headset", "left", "right", "floor", "logger" };
                for (int i = 0; i < keys.Length; i++)
                {
                    var row = Rect(technicalRows, keys[i], new Vector2(0, 140 - i * 70), new Vector2(690, 66));
                    DrawIcon(row, keys[i], new Vector2(-314, 0));
                    Label(row, "Label", new Vector2(-65, 0), new Vector2(410, 58), "", 28);
                    var status = Round(row, "Status", new Vector2(242, 0), new Vector2(200, 48), ResearchUITheme.Elevated, 12);
                    Label(status.transform, "Label", new Vector2(16, 0), new Vector2(158, 46), "", 24).alignment = TextAlignmentOptions.Center;
                    StatusIcon(status.transform, true, new Vector2(-76, 0), .27f);
                    Round(row, "Rule", new Vector2(0, -34), new Vector2(690, 1), new Color(.5f, .65f, .85f, .2f), 0);
                }
            }
            taskToolbarSurface = actionsRoot.Find("TaskToolbarSurface")?.GetComponent<TrainingRoundedGraphic>();
            if (!taskToolbarSurface)
            {
                taskToolbarSurface = Round(actionsRoot, "TaskToolbarSurface", Vector2.zero, new Vector2(680, 104), Background, 24);
                taskToolbarSurface.transform.SetAsFirstSibling(); ResearchUITheme.Surface(taskToolbarSurface, true, 24);
            }
            for (int i = 0; i < 5; i++)
            {
                var row = welcomeItems.GetChild(i);
                row.Find("Icon").gameObject.SetActive(false);
                if (!row.Find("LearningIcon")) DrawIcon(row, Steps[i], new Vector2(-304, 0), "LearningIcon");
                if (!stepRows[i].Find("StepNumber"))
                {
                    var number = Label(stepRows[i], "StepNumber", new Vector2(-280, 0), new Vector2(54, 54), (i + 1).ToString(), 26);
                    number.alignment = TextAlignmentOptions.Center;
                }
            }
            foreach (var icon in new[] { positiveIcon.transform, negativeIcon.transform, mainContent.transform.Find("PauseIcon") })
            {
                if (icon.Find("StatusRing")) continue;
                var ring = Round(icon, "StatusRing", Vector2.zero, new Vector2(116, 116), Muted, 58);
                var outline = ResearchUITheme.Surface(ring, false, 58); outline.outlineOnly = true;
            }
        }

        static void DrawIcon(Transform parent, string kind, Vector2 position, string name = "RowIcon")
        {
            var root = Rect(parent, name, position, new Vector2(48, 48));
            bool readerIcon = name == "FormatIcon";
            var color = readerIcon ? Ink : new Color(.43f, .72f, 1);
            void Line(string key, Vector2 pos, Vector2 size, float angle = 0)
            { var stroke = Round(root, key, pos, size, color, 2); stroke.transform.localRotation = Quaternion.Euler(0, 0, angle); }
            bool document = kind == "logger" || kind == "reference" || kind == "user_manual";
            var border = Round(root, "Outline", Vector2.zero, document ? new Vector2(30, 40) : new Vector2(42, 34), color, document ? 3 : 12);
            var effect = ResearchUITheme.Surface(border, false, document ? 3 : readerIcon ? 3 : 12); effect.outlineOnly = true;
            if (kind == "headset") { Line("LensLeft", new Vector2(-9, 0), new Vector2(6, 6)); Line("LensRight", new Vector2(9, 0), new Vector2(6, 6)); }
            else if (document) for (int i = 0; i < 3; i++) Line("Text" + i, new Vector2(0, 9 - i * 9), new Vector2(17, 3));
            else if (kind == "step_by_step")
            {
                border.gameObject.SetActive(false);
                for (int i = 0; i < 3; i++)
                { Line("Dot" + i, new Vector2(-16, 12 - i * 12), new Vector2(5, 5)); Line("Line" + i, new Vector2(5, 12 - i * 12), new Vector2(27, 3)); }
            }
            else if (kind == "illustrated")
            {
                effect.radius = 3;
                if (readerIcon)
                { Line("Sun", new Vector2(10, 8), new Vector2(5, 5)); Line("MountainLeft", new Vector2(-9, -3), new Vector2(3, 20), -35); Line("MountainRight", new Vector2(1, -3), new Vector2(3, 20), 35); Line("Horizon", new Vector2(7, -7), new Vector2(17, 3)); }
                else { Line("NodeLeft", new Vector2(-11, 7), new Vector2(9, 9)); Line("NodeRight", new Vector2(11, -7), new Vector2(9, 9)); Line("Connection", Vector2.zero, new Vector2(19, 3), -35); }
            }
            else if (kind == "video")
            { effect.radius = 3; Line("PlayLeft", new Vector2(-4, 0), new Vector2(3, 20)); Line("PlayTop", new Vector2(3, 5), new Vector2(3, 16), 55); Line("PlayBottom", new Vector2(3, -5), new Vector2(3, 16), -55); }
            else if (kind == "left" || kind == "right" || kind == "select") { Line("Grip", new Vector2(0, -14), new Vector2(8, 26), kind == "left" ? -20 : 20); Line("Trigger", new Vector2(0, 2), new Vector2(6, 6)); }
            else if (kind == "floor") { border.gameObject.SetActive(false); Line("Floor", new Vector2(0, -13), new Vector2(44, 4)); Line("Axis", new Vector2(0, 5), new Vector2(4, 25)); Line("ArrowA", new Vector2(-5, -3), new Vector2(4, 15), 45); Line("ArrowB", new Vector2(5, -3), new Vector2(4, 15), -45); }
            else if (kind == "inspect") { border.rectTransform.sizeDelta = new Vector2(30, 30); effect.radius = 15; Line("Handle", new Vector2(17, -17), new Vector2(4, 19), 45); }
            else if (kind == "handling") { effect.radius = 3; Line("Middle", Vector2.zero, new Vector2(4, 28)); Line("Top", new Vector2(0, 4), new Vector2(31, 4)); }
            else { border.gameObject.SetActive(false); Line("Head", new Vector2(0, 18), new Vector2(9, 9)); Line("Body", new Vector2(0, 2), new Vector2(4, 23)); Line("LeftLeg", new Vector2(-7, -15), new Vector2(4, 20), -35); Line("RightLeg", new Vector2(7, -15), new Vector2(4, 20), 35); Line("Arms", new Vector2(0, 4), new Vector2(30, 4), -20); }
        }

        void OnReferenceScroll(Vector2 position)
        {
            if (ReferenceViewer) { RefreshPageControls(); return; }
            if (!string.IsNullOrEmpty(previousReferenceKey)) scrollPositions[previousReferenceKey] = scroll.verticalNormalizedPosition;
            if (referenceBody && controller && referenceBody.pageToDisplay != ReferencePage()) RefreshPageControls();
        }

        internal int ReferencePageIndex => ReferenceViewer ? ReferenceViewer.PageIndex : scroll && referenceContent && scroll.viewport ? ReferencePage() : 1;
        int ReferencePage()
        {
            int pages = ReferencePageCount();
            return Mathf.Clamp(Mathf.RoundToInt((1 - scroll.verticalNormalizedPosition) * (pages - 1)) + 1, 1, pages);
        }
        int ReferencePageCount() => ReferenceViewer ? ReferenceViewer.PageCount : Mathf.Max(1, Mathf.CeilToInt(referenceContent.rect.height / scroll.viewport.rect.height));
        string PaginateReference(string text, float availableHeight)
        {
            var output = new System.Text.StringBuilder();
            string page = "";
            foreach (string sourceLine in text.Split('\n'))
            {
                string line = controller.Language == "Japanese"
                    ? System.Text.RegularExpressions.Regex.Replace(sourceLine, @"((?:してください|ください|[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]{3})。)$", "<nobr>$1</nobr>") : sourceLine;
                string candidate = page.Length == 0 ? line : page + "\n" + line;
                if (page.Length > 0 && referenceBody.GetPreferredValues(candidate, 682, 10000).y > availableHeight)
                {
                    output.Append(page).Append("<page>");
                    page = line;
                }
                else page = candidate;
            }
            return output.Append(page).ToString();
        }
        public void RefreshPageControls()
        {
            if (!previousPage || !controller || !ReferenceViewer) return;
            LayoutReaderShell();
            int page = ReferenceViewer.PageIndex, pages = ReferenceViewer.PageCount;
            pageLabel.text = ReferenceViewer.NavigationLabel;
            previousPage.interactable = ReferenceViewer.CanPrevious; nextPage.interactable = ReferenceViewer.CanNext;
            previousPage.gameObject.SetActive(ReferenceViewer.NavigationVisible);
            nextPage.gameObject.SetActive(ReferenceViewer.NavigationVisible);
            pageLabel.gameObject.SetActive(ReferenceViewer.NavigationVisible);
        }

        void LayoutReaderShell()
        {
            referenceExpanded.GetComponent<RectTransform>().sizeDelta = new Vector2(ReaderWidth, ReaderHeight);
            referenceExpanded.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, ToolbarBand / 2);
            referencePanel.GetComponent<RectTransform>().sizeDelta = controller.ReferenceMinimized ? ReferencePillSize : new Vector2(ReaderWidth, ReaderHeight + ToolbarBand);
            referenceExpanded.transform.Find("Surface").GetComponent<RectTransform>().sizeDelta = new Vector2(ReaderWidth, ReaderHeight);
            referenceExpanded.transform.Find("ContentSurface").gameObject.SetActive(false);
            var toolbar = referenceExpanded.transform.Find("Toolbar");
            toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -ReaderHeight / 2 - ToolbarBand / 2);
            LayoutToolbar(toolbar, ReaderWidth);
            LayoutReferenceControls(ReaderHeight);
            string documentName = ReferenceViewer ? ReferenceViewer.DocumentName : Text("reference.title");
            if (!referenceTitleScroll)
            {
                var lane = referenceExpanded.transform.Find("HeaderTitleScroll") as RectTransform;
                if (!lane) lane = Rect(referenceExpanded.transform, "HeaderTitleScroll", new Vector2(-176, 344), new Vector2(740, 92));
                referenceTitleScroll = lane.GetComponent<ResearchReaderTitleScroll>();
                if (!referenceTitleScroll) referenceTitleScroll = lane.gameObject.AddComponent<ResearchReaderTitleScroll>();
                referenceTitleScroll.view = this; referenceTitleScroll.horizontal = false; referenceTitleScroll.vertical = true;
                referenceTitleScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
                referenceTitleScroll.inertia = false; referenceTitleScroll.scrollSensitivity = 24;
                var viewport = lane.Find("Viewport") as RectTransform;
                if (!viewport)
                {
                    viewport = Rect(lane, "Viewport", Vector2.zero, new Vector2(716, 64));
                    viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                    var hit = viewport.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = Color.clear;
                }
                viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(.5f, 1);
                viewport.anchoredPosition = new Vector2(-12, 0); viewport.sizeDelta = new Vector2(716, 64);
                referenceTitle.rectTransform.SetParent(viewport, false);
                referenceTitle.rectTransform.anchoredPosition = Vector2.zero;
                referenceTitleScroll.viewport = viewport; referenceTitleScroll.content = referenceTitle.rectTransform;
                var titleBar = lane.Find("TitleScrollbar") as RectTransform;
                if (!titleBar)
                {
                    titleBar = Rect(lane, "TitleScrollbar", new Vector2(358, 14), new Vector2(24, 64));
                    var track = Round(titleBar, "Track", Vector2.zero, new Vector2(4, 64), Muted, 2);
                    var handle = Round(track.transform, "Handle", Vector2.zero, Vector2.zero, Accent, 2);
                    var indicator = titleBar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
                    indicator.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
                    indicator.handleRect = handle.rectTransform; indicator.targetGraphic = handle; indicator.interactable = false;
                }
                referenceTitleScroll.verticalScrollbar = titleBar.GetComponent<UnityEngine.UI.Scrollbar>();
                referenceTitleScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
                referenceMetadata = referenceExpanded.transform.Find("HeaderMetadata")?.GetComponent<TMP_Text>();
                if (!referenceMetadata) referenceMetadata = Label(referenceExpanded.transform, "HeaderMetadata", new Vector2(-176, 314), new Vector2(740, 52), "", 21);
                referencePanel.GetComponent<ResearchSlateFocus>().titleScroll = referenceTitleScroll;
            }
            referenceTitle.rectTransform.anchorMin = referenceTitle.rectTransform.anchorMax = referenceTitle.rectTransform.pivot = new Vector2(.5f, 1);
            // Participants see a localized heading; the source PDF file name stays the document identity only.
            string heading = controller.Flow?.IsTaskActive == true ? Text("reference.title") + " · " + Text("task." + controller.Flow.CurrentTask + ".title") : Text("reference.title");
            referenceTitle.alignment = TextAlignmentOptions.TopLeft; referenceTitle.fontSize = 30; referenceTitle.font = Font;
            referenceTitle.enableAutoSizing = referenceTitle.richText = referenceTitle.raycastTarget = false;
            referenceTitle.textWrappingMode = TextWrappingModes.Normal; referenceTitle.overflowMode = TextOverflowModes.Overflow;
            referenceTitle.margin = new Vector4(4, 4, 4, 4); referenceTitle.text = heading;
            float titleHeight = Mathf.Max(64, referenceTitle.GetPreferredValues(heading, 708, 10000).y + 8);
            referenceTitleScroll.verticalScrollbar.gameObject.SetActive(titleHeight > 64);
            referenceTitle.rectTransform.sizeDelta = new Vector2(716, titleHeight);
            string identity = controller.Language + "/" + documentName;
            if (referenceTitleIdentity != identity)
            {
                referenceTitleScroll.Cancel(); referenceTitleScroll.verticalNormalizedPosition = 1; referenceTitleIdentity = identity;
            }
            referenceMetadata.alignment = TextAlignmentOptions.MidlineLeft; referenceMetadata.font = Font; referenceMetadata.fontSize = 21;
            referenceMetadata.enableAutoSizing = referenceMetadata.richText = referenceMetadata.raycastTarget = false;
            referenceMetadata.textWrappingMode = TextWrappingModes.NoWrap; referenceMetadata.overflowMode = TextOverflowModes.Overflow;
            referenceMetadata.margin = new Vector4(4, 0, 4, 0); referenceMetadata.color = Muted;
            referenceMetadata.text = ReferenceViewer ? ReferenceViewer.Title : "";
            var header = referenceExpanded.transform.Find("HeaderRule").GetComponent<RectTransform>();
            header.gameObject.SetActive(true); header.sizeDelta = new Vector2(1140, 1.5f); header.anchoredPosition = new Vector2(0, 296);
            var footer = referenceExpanded.transform.Find("ReaderFooter") as RectTransform;
            if (footer) footer.gameObject.SetActive(false);
            var divider = referenceExpanded.transform.Find("SidebarRule") as RectTransform;
            if (!divider) divider = Round(referenceExpanded.transform, "SidebarRule", new Vector2(-304, -10), new Vector2(1.5f, 580), ResearchUITheme.Muted, 0).rectTransform;
            for (int i = 0; i < 4; i++)
            {
                var tab = tabLabels[i].transform.parent.GetComponent<RectTransform>();
                tab.gameObject.SetActive(true); tab.sizeDelta = new Vector2(240, 126); tab.anchoredPosition = new Vector2(-438, 196 - i * 136);
                tab.GetComponent<ResearchUIAction>().command = "reference_tab." + Sections[i];
                bool available = controller.ReferenceAvailable(Sections[i]);
                tab.GetComponent<Button>().interactable = available;
                ResearchUITheme.Button(tab.GetComponent<Button>(), ResearchButtonVariant.Tab);
                var colors = tab.GetComponent<Button>().colors; colors.disabledColor = new Color(.92f, .95f, 1f, .85f); tab.GetComponent<Button>().colors = colors;
                tabBackgrounds[i].color = controller.ReferenceSection == Sections[i] ? ResearchUITheme.Elevated : ResearchUITheme.Glass;
                var edge = tab.GetComponent<ResearchGlassEffect>(); if (edge) edge.edgeAlpha = controller.ReferenceSection == Sections[i] ? .24f : 0;
                tabLabels[i].text = Text("reference." + Sections[i]) + (i == 0 ? " (PDF)" : "") + (available ? "" : "\n<size=65%>" + Text("reference.unavailable") + "</size>");
                tabLabels[i].font = Font; tabLabels[i].fontSize = 27; tabLabels[i].color = available ? Ink : Muted;
                tabLabels[i].rectTransform.offsetMin = new Vector2(8, 8); tabLabels[i].rectTransform.offsetMax = new Vector2(-8, -50);
                tabLabels[i].margin = Vector4.zero; tabLabels[i].lineSpacing = 0;
                var icon = tab.Find("FormatIcon") as RectTransform;
                if (!icon) { DrawIcon(tab, Sections[i], Vector2.zero, "FormatIcon"); icon = tab.Find("FormatIcon") as RectTransform; }
                icon.anchoredPosition = new Vector2(0, 32); icon.localScale = Vector3.one;
                foreach (var stroke in icon.GetComponentsInChildren<Graphic>()) stroke.color = available ? Ink : Muted;
            }
            sectionHeading.gameObject.SetActive(false); illustratedInstruction.gameObject.SetActive(false);
            scroll.gameObject.SetActive(true);
            scroll.GetComponent<RectTransform>().anchoredPosition = new Vector2(136, -100);
            scroll.GetComponent<RectTransform>().sizeDelta = new Vector2(840, 548);
            bool video = controller.ReferenceSection == "video";
            // The band under the header holds the Training cue; during a task the document uses it.
            float band = video || TutorialReader ? 0 : 96;
            float contentHeight = (video ? 548 : 500) + band, viewportY = (video ? 0 : 24) + band / 2;
            scroll.viewport.anchoredPosition = new Vector2(-12, viewportY); scroll.viewport.sizeDelta = new Vector2(810, contentHeight);
            scroll.viewport.GetComponent<RectMask2D>().padding = Vector4.zero;
            scroll.viewport.GetComponent<Image>().raycastPadding = Vector4.zero;
            var bar = scroll.verticalScrollbar.GetComponent<RectTransform>(); bar.anchoredPosition = new Vector2(416, viewportY); bar.sizeDelta = new Vector2(24, contentHeight);
            bar.Find("Track").GetComponent<RectTransform>().sizeDelta = new Vector2(4, contentHeight);
            foreach (var pager in new[] { previousPage, nextPage })
            {
                pager.GetComponent<RectTransform>().anchoredPosition = new Vector2(pager == previousPage ? 370 : 482, -350);
                pager.GetComponent<RectTransform>().sizeDelta = new Vector2(64, 40);
                ResearchUITheme.Button(pager, ResearchButtonVariant.Ghost);
                var label = pager.GetComponentInChildren<TMP_Text>(true); label.gameObject.SetActive(false);
                var arrow = pager.transform.Find("NavigationIcon")?.GetComponent<ResearchUtilityIcon>();
                if (!arrow) arrow = Rect(pager.transform, "NavigationIcon", Vector2.zero, new Vector2(28, 28)).gameObject.AddComponent<ResearchUtilityIcon>();
                arrow.kind = pager == previousPage ? "previous" : "next"; arrow.color = Ink; arrow.raycastTarget = false; arrow.SetVerticesDirty();
            }
            pageLabel.rectTransform.anchoredPosition = new Vector2(136, -350); pageLabel.rectTransform.sizeDelta = new Vector2(256, 48); pageLabel.font = Font; pageLabel.fontSize = 23;
            LayoutReferenceTutorialCue(ReaderHeight);
        }

        void RevalidateReferenceLayoutIfReady()
        {
            if (!referenceLayoutNeedsRevalidation || !controller || !referencePanel) return;
            if (!controller.ReferenceOpen) { referenceLayoutNeedsRevalidation = false; return; }
            if (controller.ReferenceMinimized || !referencePanel.gameObject.activeInHierarchy || referencePanel.IsDragging) return;
            referenceLayoutNeedsRevalidation = false;
            referencePanel.EnsureClear(requireView: true);
        }

        Vector2 previousMainLayoutSize;
        bool mainLayoutNeedsRevalidation;
        void RevalidateMainLayoutIfReady()
        {
            if (!mainLayoutNeedsRevalidation || !controller || !mainPanel) return;
            if (controller.Flow?.Paused != true && (controller.Flow?.IsTutorial != true || controller.ReferenceOpen))
            { mainLayoutNeedsRevalidation = false; return; }
            if (mainMinimized || !mainPanel.gameObject.activeInHierarchy || mainPanel.IsDragging) return;
            mainLayoutNeedsRevalidation = false;
            mainPanel.EnsureClear(requireView: true);
        }

        string previousState, previousSection, language = "Thai";
        bool mainMinimized;
        bool compactTask, compactTutorialReference;
        const float TaskEyebrow = 72;
        Coroutine mainAnimation, referenceAnimation, checklistAnimation, successAnimation;
        public string Text(string key) => ResearchStrings.Get(key, controller ? controller.Language : language);
        public TMP_FontAsset Font => (controller ? controller.Language : language) == "Japanese" ? japaneseFont : (controller ? controller.Language : language) == "English" ? englishFont : thaiFont;

        public static ResearchFlowView Build(Transform parent, TMP_FontAsset thai, TMP_FontAsset japanese, TMP_FontAsset english)
        {
            var root = new GameObject("ResearchFlowUI"); root.transform.SetParent(parent, false);
            var v = root.AddComponent<ResearchFlowView>(); v.thaiFont = thai; v.japaneseFont = japanese; v.englishFont = english;
            var main = CanvasRoot(root.transform, "ParticipantPanel", new Vector2(1000, 760));
            v.mainPanel = main.gameObject.AddComponent<ResearchPanelMotion>(); v.mainPanel.view = v; v.mainPanel.panelId = "task_information";
            v.mainGroup = main.gameObject.AddComponent<CanvasGroup>();
            v.mainContent = Rect(main.transform, "Expanded", Vector2.zero, new Vector2(1000, 760)).gameObject;
            Round(v.mainContent.transform, "Surface", Vector2.zero, new Vector2(1000, 760), Background, 26, true);
            v.mainToolbar = v.Toolbar(v.mainContent.transform, v.mainPanel, 1000, 760, false);
            v.title = v.Label(v.mainContent.transform, "Title", new Vector2(0, 245), new Vector2(872, 118), "", 46);
            v.body = v.Label(v.mainContent.transform, "Body", new Vector2(0, 42), new Vector2(872, 280), "", 32);
            v.body.verticalAlignment = VerticalAlignmentOptions.Top;
            v.checklist = Rect(v.mainContent.transform, "TutorialChecklist", new Vector2(0, -10), new Vector2(872, 440));
            v.stepRows = new RectTransform[5]; v.stepLabels = new TMP_Text[5]; v.stepDetails = new TMP_Text[5]; v.stepBackgrounds = new TrainingRoundedGraphic[5]; v.stepChecks = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                var row = Rect(v.checklist, "Step_" + i, new Vector2(0, 180 - i * 80), new Vector2(872, 72)); v.stepRows[i] = row;
                v.stepBackgrounds[i] = Round(row, "Background", Vector2.zero, new Vector2(872, 72), Card, 14);
                v.stepLabels[i] = v.Label(row, "Label", new Vector2(42, 0), new Vector2(704, 50), "", 31);
                v.stepDetails[i] = v.Label(row, "Instruction", new Vector2(42, -29), new Vector2(704, 58), "", 25);
                v.stepChecks[i] = StatusIcon(row, true, new Vector2(-292, 0), .42f);
                if (i < 4) Round(v.checklist, "ProgressLine_" + i, new Vector2(-292, 140 - i * 80), new Vector2(3, 14), Muted, 1);
            }
            v.actionsRoot = Rect(v.mainContent.transform, "Actions", new Vector2(0, -294), new Vector2(872, 94));
            v.actions = new Button[3]; v.actionLabels = new TMP_Text[3];
            for (int i = 0; i < 3; i++) { v.actions[i] = v.Button(v.actionsRoot, "Action_" + i, Vector2.zero, new Vector2(280, 94), "", "", i == 0); v.actionLabels[i] = v.actions[i].GetComponentInChildren<TMP_Text>(); }
            v.positiveIcon = StatusIcon(v.mainContent.transform, true, new Vector2(0, 225), 1);
            v.negativeIcon = StatusIcon(v.mainContent.transform, false, new Vector2(0, 225), 1);
            v.mainPill = v.Button(main.transform, "MinimizedTask", Vector2.zero, MainPillSize, "", "main_restore", true).gameObject;

            var reference = CanvasRoot(root.transform, "ReferencePanel", new Vector2(1180, 790));
            reference.sortingOrder = 5;
            v.referencePanel = reference.gameObject.AddComponent<ResearchPanelMotion>(); v.referencePanel.view = v; v.referencePanel.panelId = "reference";
            v.referenceGroup = reference.gameObject.AddComponent<CanvasGroup>();
            v.referenceExpanded = Rect(reference.transform, "Expanded", Vector2.zero, new Vector2(1180, 790)).gameObject;
            Round(v.referenceExpanded.transform, "Surface", Vector2.zero, new Vector2(1180, 790), Background, 26, true);
            v.Toolbar(v.referenceExpanded.transform, v.referencePanel, 1180, 790, true);
            v.referenceTitle = v.Label(v.referenceExpanded.transform, "Title", new Vector2(0, 253), new Vector2(1060, 82), "", 45);
            v.tabLabels = new TMP_Text[4]; v.tabBackgrounds = new TrainingRoundedGraphic[4];
            for (int i = 0; i < 4; i++)
            {
                var tab = v.Button(v.referenceExpanded.transform, "Tab_" + Sections[i], new Vector2(-403, 128 - 115 * i), new Vector2(275, 96), "", "reference_tab." + Sections[i], false);
                v.tabLabels[i] = tab.GetComponentInChildren<TMP_Text>(); v.tabLabels[i].fontSize = 27; v.tabBackgrounds[i] = tab.GetComponent<TrainingRoundedGraphic>();
            }
            var scrollRoot = Rect(v.referenceExpanded.transform, "ScrollView", new Vector2(166, -47), new Vector2(720, 508));
            v.scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); v.scroll.horizontal = false; v.scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect(scrollRoot, "Viewport", new Vector2(-12, 0), new Vector2(680, 508));
            viewport.gameObject.AddComponent<RectMask2D>(); var hitSurface = viewport.gameObject.AddComponent<Image>(); hitSurface.color = new Color(0, 0, 0, .001f);
            var content = Rect(viewport, "Content", Vector2.zero, new Vector2(668, 680));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(-20, 680);
            v.referenceContent = content;
            v.referenceBody = v.Label(content, "ContentText", Vector2.zero, new Vector2(648, 660), "", 31);
            v.referenceBody.rectTransform.anchorMin = Vector2.zero; v.referenceBody.rectTransform.anchorMax = Vector2.one; v.referenceBody.rectTransform.offsetMin = new Vector2(4, 10); v.referenceBody.rectTransform.offsetMax = new Vector2(-4, -10); v.referenceBody.verticalAlignment = VerticalAlignmentOptions.Top;
            v.scroll.viewport = viewport; v.scroll.content = content;
            var scrollbar = Rect(scrollRoot, "Scrollbar", new Vector2(352, 0), new Vector2(24, 508));
            Round(scrollbar, "Track", Vector2.zero, new Vector2(20, 508), Card, 10);
            var thumb = Round(scrollbar, "Handle", Vector2.zero, new Vector2(20, 100), Muted, 10, true);
            var bar = scrollbar.gameObject.AddComponent<Scrollbar>(); bar.handleRect = thumb.rectTransform; bar.targetGraphic = thumb; bar.direction = Scrollbar.Direction.BottomToTop; v.scroll.verticalScrollbar = bar;
            v.referencePill = v.Button(reference.transform, "MinimizedReference", Vector2.zero, ReferencePillSize, "", "reference_restore", true).gameObject;
            v.pillText = v.referencePill.GetComponentInChildren<TMP_Text>();

            v.helperCanvas = CanvasRoot(root.transform, "TutorialHelperKiosk", new Vector2(300, 390));
            Round(v.helperCanvas.transform, "Kiosk", Vector2.zero, new Vector2(300, 390), Card, 35);
            Round(v.helperCanvas.transform, "Face", new Vector2(0, 106), new Vector2(192, 114), Background, 26);
            Round(v.helperCanvas.transform, "EyeLeft", new Vector2(-42, 108), new Vector2(23, 30), Positive, 11);
            Round(v.helperCanvas.transform, "EyeRight", new Vector2(42, 108), new Vector2(23, 30), Positive, 11);
            v.helperText = v.Label(v.helperCanvas.transform, "Message", new Vector2(0, -38), new Vector2(260, 144), "", 25); v.helperText.alignment = TextAlignmentOptions.Center;
            v.Button(v.helperCanvas.transform, "Help", new Vector2(0, -151), new Vector2(250, 66), "?", "helper", false);
            v.ParticipantCanvases = new[] { main, reference, v.helperCanvas };
            var shade = CanvasRoot(root.transform, "TransitionFade", new Vector2(4000, 4000)); shade.sortingOrder = 100;
            v.transitionShade = shade.gameObject.AddComponent<CanvasGroup>(); v.transitionShade.alpha = 0; v.transitionShade.blocksRaycasts = false;
            Round(shade.transform, "Black", Vector2.zero, new Vector2(4000, 4000), Color.black, 0);
            v.mainPill.SetActive(false); v.referencePill.SetActive(false); reference.gameObject.SetActive(false); v.helperCanvas.gameObject.SetActive(false);
            v.ApplyVisualTheme(); v.Refresh(); return v;
        }

        GameObject Toolbar(Transform parent, ResearchPanelMotion panel, float width, float height, bool reference)
        {
            var bar = Rect(parent, "Toolbar", new Vector2(0, height / 2 - 48), new Vector2(width - 80, 72));
            var handle = Round(bar, "DragHandle", new Vector2(-100, 0), new Vector2(width - 480, 50), Card, 13, true);
            var drag = handle.gameObject.AddComponent<ResearchDragHandle>(); drag.panel = panel;
            Round(handle.transform, "GripLine", Vector2.zero, new Vector2(104, 6), Muted, 3);
            Button(bar, "Minimize", new Vector2(width / 2 - 328, 0), new Vector2(108, 60), "−", reference ? "reference_minimize" : "main_minimize", false).GetComponent<ResearchUIAction>().labelKey = "action.minimize";
            Button(bar, "Recenter", new Vector2(width / 2 - 183, 0), new Vector2(166, 60), "", reference ? "reference_recenter" : "main_recenter", false).GetComponent<ResearchUIAction>().labelKey = "action.recenter";
            if (reference) Button(bar, "Close", new Vector2(width / 2 - 65, 0), new Vector2(62, 60), "×", "reference_close", false);
            return bar.gameObject;
        }

        public void Refresh()
        {
            if (questSetup)
            {
                if (controller && controller.TechnicalSelfSetup && controller.Record == null && controller.Flow == null && string.IsNullOrEmpty(controller.Error))
                { questSetup.Refresh(); return; }
                var setup = questSetup; questSetup = null; setup.ReturnToDesktop();
            }
            if (!TutorialReader && tutorialDemo && tutorialDemo.parent != mainContent.transform)
            {
                tutorialDemo.SetParent(mainContent.transform, false); tutorialDemo.localScale = Vector3.one;
                tutorialDemo.Find("Card").gameObject.SetActive(true);
            }
            // Feedback can belong to the reader even when the demonstration stays in the coach.
            if (!TutorialReader && tutorialSuccess && tutorialSuccess.parent != mainContent.transform)
            {
                tutorialSuccess.SetParent(mainContent.transform, false); tutorialSuccess.Find("Chip").gameObject.SetActive(true);
            }
            var flow = controller ? controller.Flow : null;
            language = controller ? controller.Language : language;
            foreach (var text in GetComponentsInChildren<TMP_Text>(true)) text.font = text.text == "−" || text.text == "×" || text.text == "?" ? englishFont : Font;
            foreach (var button in GetComponentsInChildren<ResearchUIAction>(true)) if (!string.IsNullOrEmpty(button.labelKey))
            { var label = button.GetComponentInChildren<TMP_Text>(true); label.text = Text(button.labelKey); label.font = Font; }
            LayoutToolbar(mainToolbar.transform, 1000);
            LayoutToolbar(referenceExpanded.transform.Find("Toolbar"), 1180);
            scroll.verticalScrollbar.handleRect.sizeDelta = Vector2.zero;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            string state = flow == null ? "setup" : flow.Paused ? "pause" : flow.InspectPassed.HasValue ? (flow.InspectPassed.Value ? "pass" : "fail")
                : controller.InspectionChecking ? "checking" : flow.State.ToString();
            if (state.Contains("Checkpoint") && controller.TaskEndReason == "time_limit") state = "timeout";
            if (SessionEnded) state = "ended";
            if ((controller && !string.IsNullOrEmpty(controller.Error)) || SessionWriterUnavailable) state = "error";
            bool floorBlocked = flow != null && controller && !controller.FloorReady;
            if (floorBlocked && state != "error" && state != "pause" && state != "ended") state = "tracking";
            if ((state == "error" || state == "pause" || state == "tracking" || state == "ended") && arrivalGroup && arrivalGroup.alpha > 0)
            { if (arrivalAnimation != null) StopCoroutine(arrivalAnimation); arrivalGroup.alpha = 0; }
            if (flow == null || !flow.IsTutorial || state == "error" || state == "pause" || state == "tracking" || state == "ended" || controller.Transitioning)
            { successUntil = float.NegativeInfinity; tutorialSuccess.gameObject.SetActive(false); }
            bool confirming = Time.unscaledTime < successUntil;
            // After Task 2 the participant sees Final Completion at once (they take the headset off here); the researcher
            // still confirms the last questionnaire outside VR: PostTask2Checkpoint -> CHECKPOINT_COMPLETED -> ParticipantComplete.
            bool lastQuestionnairePending = state == "PostTask2Checkpoint";
            if (lastQuestionnairePending) state = "ParticipantComplete";
            bool selfResume = state == "pause" && controller && controller.ParticipantMayResume;
            bool testerResume = state == "pause" && controller && controller.TesterMayResumeStop;
            // Status modals open straight ahead; working slates keep their side placement.
            mainPanel.centered = state == "setup" || state == "TutorialWelcome" || state == "TutorialComplete" || state.Contains("Ready") || state == "pause" || state == "tracking" || state == "error" || state == "ended" || state == "pass" || state == "fail" || state == "checking" || state == "timeout" || state.Contains("Checkpoint") || state == "ParticipantComplete" || state == "ResearcherSummary";
            compactTask = state == "Task1Active" || state == "Task2Active";
            bool beforeMinimized = mainMinimized;
            if (compactTask && hasTaskPose && Interrupting(previousState)) mainMinimized = savedTaskMinimized;
            bool tutorialChecklist = flow != null && (flow.State == ResearchFlowState.TutorialInteractions || flow.State == ResearchFlowState.TutorialReferenceInspect) && !flow.Paused && !floorBlocked && state != "error" && state != "ended";
            checklist.gameObject.SetActive(tutorialChecklist); body.gameObject.SetActive(!tutorialChecklist);
            positiveIcon.SetActive(state == "pass" || state == "TutorialComplete" || state == "ParticipantComplete" || state == "ResearcherSummary");
            negativeIcon.SetActive(state == "fail");
            mainPanel.movable = (tutorialChecklist && !controller.ReferenceOpen) || state.Contains("Active");
            mainToolbar.SetActive(mainPanel.movable);
            if (!mainPanel.movable) mainMinimized = false;
            mainContent.SetActive(!mainMinimized); mainPill.SetActive(mainMinimized);
            mainPill.GetComponent<RectTransform>().sizeDelta = MainPillSize; // Keep serialized scenes aligned with root hit bounds.
            mainPill.GetComponentInChildren<TMP_Text>().text = Text("action.restore_task");
            title.rectTransform.anchoredPosition = new Vector2(0, positiveIcon.activeSelf || negativeIcon.activeSelf ? 105 : 245);
            title.alignment = positiveIcon.activeSelf || negativeIcon.activeSelf ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            body.rectTransform.anchoredPosition = new Vector2(0, positiveIcon.activeSelf || negativeIcon.activeSelf ? -59 : 25);
            body.alignment = positiveIcon.activeSelf || negativeIcon.activeSelf ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            bool resultState = state == "pass" || state == "fail" || state == "ParticipantComplete" || state == "ResearcherSummary";
            bool checkpoint = state.Contains("Checkpoint");
            var panelSize = new Vector2(1000, compactTask ? 410 : tutorialChecklist ? 760 : resultState ? 570 : checkpoint ? 680 : 570);
            expandedMainSize = panelSize;
            mainContent.GetComponent<RectTransform>().sizeDelta = panelSize;
            mainContent.transform.Find("Surface").GetComponent<RectTransform>().sizeDelta = panelSize;
            if (!mainMinimized) mainPanel.GetComponent<RectTransform>().sizeDelta = panelSize;
            mainToolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, panelSize.y / 2 - 48);
            title.fontSize = compactTask ? 38 : 46;
            title.rectTransform.sizeDelta = new Vector2(872, compactTask ? 76 : 118);
            body.fontSize = compactTask ? 28 : 32;
            body.rectTransform.sizeDelta = new Vector2(872, compactTask ? 135 : 280);
            if (compactTask) { title.rectTransform.anchoredPosition = new Vector2(0, 80); body.rectTransform.anchoredPosition = new Vector2(0, -18); }
            else if (!tutorialChecklist && !resultState) { title.rectTransform.anchoredPosition = new Vector2(0, checkpoint ? 210 : 136); body.rectTransform.anchoredPosition = new Vector2(0, checkpoint ? -12 : -8); body.rectTransform.sizeDelta = new Vector2(872, checkpoint ? 280 : 180); }
            if (resultState) { positiveIcon.transform.localPosition = negativeIcon.transform.localPosition = new Vector3(0, 167, 0); title.rectTransform.anchoredPosition = new Vector2(0, 61); body.rectTransform.anchoredPosition = new Vector2(0, -74); body.rectTransform.sizeDelta = new Vector2(840, 130); }
            var rule = mainContent.transform.Find("HeaderRule");
            if (rule) { rule.gameObject.SetActive(!resultState); rule.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, compactTask ? 40 : tutorialChecklist ? 212 : checkpoint ? 157 : 90); }
            actionsRoot.anchoredPosition = new Vector2(0, -panelSize.y / 2 + 76);
            bool technicalTest = controller && controller.TechnicalSelfTest;
            waitingLabel.gameObject.SetActive((state == "pause" && !selfResume && !testerResume) || state == "error");
            waitingLabel.rectTransform.anchoredPosition = new Vector2(0, -panelSize.y / 2 + 67);
            waitingLabel.text = Text("status.wait_researcher");
            foreach (var action in actions) { action.gameObject.SetActive(false); action.interactable = true; }
            helperCanvas.gameObject.SetActive(false); // Tutorial instructions already live on the primary panel; retire the tabletop kiosk.
            switch (state)
            {
                case "setup":
                    if (controller && controller.TechnicalSelfSetup) { TechnicalSetup(); break; }
                    title.text = Text("setup.title"); body.text = Text("setup.wait"); break;
                case "error": title.text = Text("error.title"); body.text = Text("error.body") + (controller.PauseKind == "stop" ? "\n" + Text("stop.body") : ""); break;
                case "ended": title.text = Text("session.ended.title"); body.text = Text("session.ended.body"); break;
                case "tracking": title.text = Text("tracking.title"); body.text = Text("tracking.body"); break;
                case "pause":
                    // Temporary pause: participant may continue (technical/tutorial). Study task pause: researcher resumes.
                    title.text = Text(controller.PauseKind == "stop" ? "stop.title" : selfResume ? "pause.participant.title" : "pause.title");
                    body.text = "<color=#" + ColorUtility.ToHtmlStringRGB(Ink) + ">" + Text(testerResume ? "stop.tester_body" : controller.PauseKind == "stop" ? "stop.body" : selfResume ? "pause.participant.body" : "pause.body")
                        + (!selfResume && controller.HelpRequested ? "\n" + Text("pause.help_sent") : "")
                        + "\n<size=90%>" + Text("pause.safety") + "</size></color>";
                    if (selfResume) SetActions(("session_resume", "action.resume"));
                    else if (testerResume) SetActions(("tester_resume", "action.resume"));
                    else SetActions(("panel_recovery", "action.recover_short"));
                    break;
                case "TutorialWelcome": title.text = Text("welcome.title"); body.text = Text("welcome.body") + "\n\n" + Text(controller.TutorialUsesHands ? "welcome.hand" : "welcome.controller"); SetActions(("tutorial_start", "tutorial.start")); break;
                case "TutorialInteractions": case "TutorialReferenceInspect":
                    title.text = Text("tutorial.title");
                    if (flow.TutorialCompleted == 1) SetActions(("tutorial_select", "action.practice"));
                    else if (flow.TutorialCompleted == 3) SetActions(("reference", "action.reference"));
                    // With the practice station, Inspect is offered only when the current instruction is an Inspect sample.
                    else if (flow.TutorialCompleted == 4 && (!controller.trainingStation || controller.TutorialFact == "inspect" || controller.TutorialFact == "inspect_fail"))
                        SetActions(("inspect", "action.inspect"));
                    break;
                case "TutorialComplete": title.text = Text("tutorial.complete"); body.text = Text("tutorial.complete.body"); SetActions(("tutorial_continue", "action.go_task1")); break;
                // Ready: Task 1 and Task 2 share one structure — where you are (task n of 2), the neutral goal, and one Start.
                case "Task1Ready": case "Task2Ready":
                    title.text = string.Format(Text("task.progress"), state == "Task2Ready" ? 2 : 1);
                    body.text = Text(controller.TaskStartConfirmation ? "task.start.confirm.body" : "task.ready.general");
                    if (controller.TaskStartConfirmation) SetActions(("task_start_back", "action.back"), ("task_start_confirm", "task.start.confirm.action"));
                    else SetActions(("task_start", "action.start_task"));
                    break;
                case "Task1Active": case "Task2Active": title.text = TaskName(flow); body.text = TaskBrief(flow.CurrentTask); SetActions(("reference", "action.reference"), ("inspect", "action.inspect")); break;
                case "checking": title.text = Text("inspect.checking.title"); body.text = Text("inspect.checking.body"); break;
                case "timeout": title.text = Text("task.timeout.title"); body.text = Text("task.timeout.body");
                    if (technicalTest) SetActions(("technical_resume", "technical.checkpoint_continue"));
                    break;
                case "pass": case "fail": title.text = Text(state == "pass" ? flow.State == ResearchFlowState.Task1Active ? "task1.completed" : "task2.completed" : "inspect.fail.title");
                    body.text = InspectionFeedbackText(state == "pass");
                    if (controller.InspectionFeedbackVisible)
                        SetActions(("inspect_continue", state == "fail" ? "action.return_task" : flow.State == ResearchFlowState.Task1Active ? "action.go_checkpoint" : "action.finish_vr"));
                    break;
                case "PostTask1Checkpoint": case "PostTask2Checkpoint": title.text = Text("checkpoint.title"); body.text = Text("checkpoint.body");
                    if (technicalTest) SetActions(("technical_resume", "technical.checkpoint_continue"));
                    break;
                case "ParticipantComplete": case "ResearcherSummary": title.text = Text("completion.title"); body.text = Text("completion.body");
                    if (controller.Language == "Japanese") body.text = body.text.Replace("ください。", "<nobr>ください。</nobr>");
                    if (lastQuestionnairePending && technicalTest) SetActions(("technical_resume", "technical.checkpoint_continue"));
                    break;
            }
            bool expandedTutorial = compactTutorialReference && tutorialChecklist && !controller.ReferenceOpen;
            LayoutSecondPass(state, tutorialChecklist, resultState, checkpoint);
            if (confirming && tutorialChecklist && !compactTutorialReference) ShowTutorialConfirmation();
            var mainLayoutSize = mainPanel.GetComponent<RectTransform>().sizeDelta;
            if (((tutorialChecklist && !controller.ReferenceOpen) || (state == "pause" && state == previousState)) &&
                (mainLayoutSize.x > previousMainLayoutSize.x + 1 || mainLayoutSize.y > previousMainLayoutSize.y + 1))
                mainLayoutNeedsRevalidation = true;
            previousMainLayoutSize = mainLayoutSize;
            RevalidateMainLayoutIfReady();
            if (expandedTutorial) mainPanel.EnsureClear(requireView: false);
            if (state != previousState)
            {
                if ((previousState == "Task1Active" || previousState == "Task2Active") && Interrupting(state))
                { savedTaskPosition = mainPanel.transform.position; savedTaskRotation = mainPanel.transform.rotation; savedTaskMinimized = beforeMinimized; hasTaskPose = true; }
                if (helperDismiss != null) { StopCoroutine(helperDismiss); helperDismiss = null; }
                helperText.gameObject.SetActive(false); helperCanvas.transform.Find("Bubble").gameObject.SetActive(false);
                // Tracking blips (e.g. a system screenshot) keep the participant's panel where it was.
                bool keepPose = state == "tracking" || previousState == "tracking";
                if (!keepPose && (!mainPanel.movable || state == "TutorialComplete" || (compactTask && !Interrupting(previousState))) && Head) mainPanel.Recenter(false, false);
                if (compactTask && recoverTaskOnResume)
                { mainPanel.Recenter(false, false, reason: "explicit_recovery_resume"); recoverTaskOnResume = false; }
                if (compactTask && hasTaskPose && Interrupting(previousState) && previousState != "tracking") mainPanel.RestorePose(savedTaskPosition, savedTaskRotation);
                mainPanel.EnsureClear(requireView: !(compactTask && hasTaskPose && Interrupting(previousState) && previousState != "tracking"));
                Animate(mainGroup, ref mainAnimation);
                if (positiveIcon.activeSelf) AnimateSuccessIcon();
                previousState = state;
            }
            RefreshReference();
            string instruction = tutorialChecklist && !flow.Paused ? controller.TutorialInstructionKey + "/" + shownFact + "/" + TutorialReader : null;
            if (instruction != placedInstruction)
            {
                placedInstruction = instruction;
                if (instruction != null)
                {
                    var panel = TutorialReader ? referencePanel : mainPanel;
                    if (panel && panel.gameObject.activeInHierarchy) panel.Recenter(false, false, onlyIfNeeded: true, reason: "displayed_instruction:" + instruction);
                }
            }
            RefreshRecoveryUtility();
            ApplyUIPolicyToToolbars();
            if (tutorialChecklist) controller.TutorialPromptShown();
        }

        // ADVISOR_UI_AMENDMENT_20261014: in the fixed policy the drag handle (grip line AND its invisible hit area) and the
        // participant Recenter control are removed, not merely hidden. Minimize/Close/Pause and Reader reopen stay available.
        void ApplyUIPolicyToToolbars()
        {
            foreach (var toolbar in new[] { mainToolbar ? mainToolbar.transform : null, referenceExpanded ? referenceExpanded.transform.Find("Toolbar") : null })
            {
                if (!toolbar) continue;
                var handle = toolbar.Find("DragHandle");
                if (handle && handle.gameObject.activeSelf != ResearchUIPolicy.PanelDragAllowed) handle.gameObject.SetActive(ResearchUIPolicy.PanelDragAllowed);
                var recenter = toolbar.Find("Recenter");
                bool show = recenter && recenter.GetComponent<ResearchUIAction>().command == "reference_distance" ? ReaderDistanceControl : ResearchUIPolicy.ParticipantRecenterAllowed;
                if (recenter && !show && recenter.gameObject.activeSelf) recenter.gameObject.SetActive(false);
                else if (recenter && show && ResearchUIPolicy.ReaderPoseFixedPerTask && !recenter.gameObject.activeSelf) recenter.gameObject.SetActive(true);
            }
        }

        static bool Interrupting(string state) => state == "checking" || state == "fail" || state == "pause" || state == "tracking";

        // Four presentation parts over the five recorded groups: 1 look/move/select, 2 handle an object, 3 Reader, 4 practice station
        // + Inspect. Evidence is still credited per skill; only the participant-facing counter changes (no "8 / 24" wall).
        internal const int TutorialParts = 4;
        internal static int TutorialPart(int completedGroups) => completedGroups <= 1 ? 1 : Mathf.Min(TutorialParts, completedGroups);
        string TutorialProgress() => string.Format(Text("tutorial.part_progress"), TutorialPart(controller.Flow?.TutorialCompleted ?? 0), TutorialParts);

        public bool OpenResearcherSetup()
        {
            if (!controller || !controller.TechnicalSelfSetup || controller.Record != null || controller.Flow != null
                || !string.IsNullOrEmpty(controller.Error)) return false;
            var setup = Array.Find(controller.GetComponentsInChildren<ResearcherSetupView>(true), candidate => candidate.controller == controller);
            if (!setup) return false;
            mainPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 620);
            mainPanel.centered = true; mainPanel.movable = false; mainPanel.EnsureClear();
            if (mainPanel.PlacementBlocked || !mainPanel.GetComponent<Canvas>().enabled || !setup.ShowOnQuest(this)) return false;
            questSetup = setup;
            mainContent.SetActive(false); mainPill.SetActive(false); mainToolbar.SetActive(false);
            return true;
        }

        public void CloseResearcherSetup()
        {
            if (!questSetup) return;
            var setup = questSetup; questSetup = null; setup.ReturnToDesktop();
            mainContent.SetActive(true); Refresh();
        }

        string TaskProgress(ResearchSessionFlow flow) => string.Format(Text("task.progress"), flow.State <= ResearchFlowState.PostTask1Checkpoint ? 1 : 2);
        // Job codes such as B-04 never break across lines.
        string TaskName(ResearchSessionFlow flow) =>
            System.Text.RegularExpressions.Regex.Replace(Text("task." + flow.CurrentTask + ".title"), @"[A-Z][0-9]*-[0-9]+\S*", "<nobr>$0</nobr>");

        string TaskBrief(string task)
        {
            var parts = Text("task." + task + ".body").Split('\n');
            if (parts.Length != 4) return string.Join("\n", parts);
            // Goal, then the two conditions as one group, then where the details are. Every spec sentence stays verbatim.
            string goal = Text("task.ready.goal").Split(new[] { "{0}" }, StringSplitOptions.None)[0], gap = "\n<size=45%>\n</size>";
            return "<b>" + goal + "</b>" + parts[0] + gap + "<b>" + Text("task.conditions") + "</b>\n"
                + "•<indent=1em>" + parts[1] + "</indent>\n•<indent=1em>" + parts[2] + "</indent>" + gap
                + "<color=#" + ColorUtility.ToHtmlStringRGB(Muted) + ">" + parts[3] + "</color>";
        }

        // Technical package only: readiness at a glance, one primary Start, options as small secondary buttons.
        void TechnicalSetup()
        {
            title.text = Text("technical.setup.title");
            var next = controller.TechnicalNextRun();
            body.text = Text("technical.ready_summary") + "\n" + string.Format(Text("technical.next_run"), next.id, next.layout);
            string[] keys = { "headset", "left", "right", "floor", "logger" };
            bool[] readiness = { controller.HeadsetReady, controller.GuardTracked(Oculus.Interaction.Input.Handedness.Left), controller.GuardTracked(Oculus.Interaction.Input.Handedness.Right), controller.FloorReady, controller.LoggerReady };
            setupDetailsVisible = setupDetailsRequested || !controller.TechnicalReady || Array.Exists(readiness, ready => !ready);
            technicalDetails.GetComponentInChildren<TMP_Text>().text = Text(setupDetailsVisible ? "technical.details_hide" : "technical.details_show");
            technicalDetails.interactable = controller.TechnicalReady && !Array.Exists(readiness, ready => !ready);
            for (int i = 0; i < keys.Length; i++)
            {
                var row = technicalRows.GetChild(i); var label = row.Find("Label").GetComponent<TMP_Text>();
                label.text = Text("technical." + keys[i]); label.font = Font;
                var status = row.Find("Status"); var statusLabel = status.Find("Label").GetComponent<TMP_Text>();
                statusLabel.text = Text(readiness[i] ? "action.ready" : "technical.waiting"); statusLabel.font = Font;
                statusLabel.rectTransform.sizeDelta = new Vector2(158, 48);
                statusLabel.color = readiness[i] ? Positive : Muted;
                status.GetComponent<TrainingRoundedGraphic>().color = ResearchUITheme.Glass;
                status.Find("Checkmark").gameObject.SetActive(readiness[i]);
            }
            SetActions(("technical_start", "technical.start"));
            actions[0].interactable = controller.TechnicalReady;
            technicalOptions.Find("Option_language").GetComponentInChildren<TMP_Text>().text = Text("technical.option." + controller.SetupLanguage);
            technicalOptions.Find("Option_order").GetComponentInChildren<TMP_Text>().text = Text("technical.option." + controller.SetupOrder);
            operatorEntry.GetComponentInChildren<TMP_Text>().text = Text("technical.operator_setup");
        }

        void SetActions(params (string command, string key)[] buttons)
        {
            float width = buttons.Length == 1 ? 680 : (872 - (buttons.Length - 1) * ResearchUITheme.TargetGap) / buttons.Length;
            float cursor = buttons.Length == 1 ? -340 : -436;
            for (int i = 0; i < buttons.Length; i++)
            {
                actions[i].gameObject.SetActive(true); var rt = actions[i].GetComponent<RectTransform>(); float itemWidth = buttons.Length == 2 ? (i == 0 ? 555 : 295) : width;
                rt.sizeDelta = new Vector2(itemWidth, compactTask ? 76 : 94);
                rt.anchoredPosition = new Vector2(cursor + itemWidth / 2, 0); cursor += itemWidth + ResearchUITheme.TargetGap;
                actions[i].GetComponent<ResearchUIAction>().command = buttons[i].command; actionLabels[i].text = Text(buttons[i].key);
                actions[i].GetComponent<ResearchUIAction>().labelKey = "";
                actions[i].transform.Find("UtilityIcon")?.gameObject.SetActive(false);
                actionLabels[i].gameObject.SetActive(true);
                actionLabels[i].rectTransform.offsetMin = new Vector2(12, 5);
                actionLabels[i].rectTransform.offsetMax = new Vector2(-12, -5);
                // D03: 25 px thin Thai/Japanese strokes rendered washed out in the Game View; action labels read at body-adjacent size.
                actionLabels[i].fontSize = 30;
                var variant = buttons[i].command == "pause" ? ResearchButtonVariant.Ghost : compactTask ? ResearchButtonVariant.Secondary : i == 0 ? ResearchButtonVariant.Primary : ResearchButtonVariant.Secondary;
                if (compactTask && (buttons[i].command == "reference" || buttons[i].command == "inspect")) variant = ResearchButtonVariant.Secondary;
                ResearchUITheme.Button(actions[i], variant);
            }
        }

        void LayoutSecondPass(string state, bool tutorialChecklist, bool resultState, bool checkpoint)
        {
            bool welcome = state == "TutorialWelcome";
            bool tutorialDone = state == "TutorialComplete";
            bool technicalSetup = state == "setup" && controller && controller.TechnicalSelfSetup;
            bool pause = state == "pause";
            bool referenceCompact = tutorialChecklist && controller && controller.ReferenceOpen;
            compactTutorialReference = referenceCompact;
            float width = compactTask ? 700 : referenceCompact ? 520 : tutorialChecklist ? 860 : resultState || checkpoint || pause ? 720 : 780;
            title.fontSize = compactTask ? 32 : 38;
            bool coachDemo = tutorialChecklist && !referenceCompact && !tutorialDone && controller.TutorialFact != "inspect";
            float instructionWidth = width - 2 * Pad - (coachDemo ? TutorialDemoBand : 0);
            if (tutorialChecklist) body.text = referenceCompact ? string.Format(Text("tutorial.compact.now"), Text("tutorial.compact." + (controller.TutorialFact == "reposition" ? "drag" + (controller.TutorialUsesHands ? ".hand" : "") : controller.TutorialFact == "reference" ? "switch" : controller.TutorialFact))) : Text(controller.TutorialNeedsRelease ? "tutorial.try.release" + (controller.TutorialUsesHands ? ".hand" : ".controller") : controller.TutorialInstructionKey)
                + (controller.TutorialFact == "inspect" ? "\n\n" + Text("tutorial.examine_difference") : "");
            body.fontSize = tutorialChecklist ? (referenceCompact ? 28 : 30) : 32;
            body.fontStyle = FontStyles.Normal; BodySpacing(body);
            float textMeasureWidth = (tutorialChecklist ? instructionWidth : width - 88) - body.margin.x - body.margin.z;
            float textHeight = body.GetPreferredValues(body.text, textMeasureWidth, 1500).y + body.margin.y + body.margin.w;
            if (checkpoint) textHeight = LayoutCheckpointSteps(width);
            // Measure at the width the title really gets: narrower when the Pause utility shares its row (design_rule D08).
            bool pauseInTitleRow = (mainPanel.movable || state == "checking" || state == "fail" || state == "pass") && !welcome && !tutorialDone && !referenceCompact;
            bool stopInTitleRow = controller && controller.ParticipantCanStop && !referenceCompact;
            float titleReserve = pauseInTitleRow && stopInTitleRow ? 276 : pauseInTitleRow || stopInTitleRow ? 180 : 88;
            // Task: the step counter shares the Stop/Pause row, so the task name gets the full width below it.
            float titleHeight = compactTask ? TaskEyebrow + title.GetPreferredValues(title.text, width - 88, 500).y + 8
                : Mathf.Max(64, title.GetPreferredValues(title.text, width - titleReserve, 500).y + 8);
            // Stable coach size: the slate must not resize (and shove Reference) as instructions change,
            // so the action row is always reserved and content has a fixed minimum.
            bool tutorialAction = false;
            if (tutorialChecklist && !referenceCompact)
                foreach (var action in actions) tutorialAction |= action.gameObject.activeSelf;
            float coachContent = Mathf.Max(coachDemo ? 324 : 170, textHeight + 8);
            // Coach: title 56, progress 36, rule, content, reserved success line, then the one action.
            float height = technicalSetup ? (setupDetailsVisible ? 990 : Mathf.Max(640, textHeight + 600)) : referenceCompact ? Mathf.Max(130, textHeight + 2 * ResearchUITheme.SpaceXL)
                : tutorialChecklist ? 128 + coachContent + ResearchUITheme.SectionGap + 48 + (tutorialAction ? ResearchUITheme.ActionGap + 102 : 40)
                : compactTask ? Mathf.Max(310, textHeight + titleHeight + 222) : Mathf.Max(360, textHeight + titleHeight + 210);
            float statusHeight = positiveIcon.activeSelf || negativeIcon.activeSelf ? 128 : 0;
            height += statusHeight + (pause && waitingLabel.gameObject.activeSelf ? 80 : 0);
            var size = new Vector2(width, height); expandedMainSize = size;
            mainContent.GetComponent<RectTransform>().sizeDelta = size;
            var outer = mainContent.transform.Find("Surface").GetComponent<RectTransform>();
            var surface = mainContent.transform.Find("ContentSurface").GetComponent<RectTransform>();
            // One surface holds the brief and its toolbar; a second box around the actions read as extra clutter.
            outer.sizeDelta = size; outer.anchoredPosition = Vector2.zero;
            surface.sizeDelta = outer.sizeDelta - Vector2.one * 8; surface.anchoredPosition = outer.anchoredPosition;
            surface.GetComponent<TrainingRoundedGraphic>().color = ResearchUITheme.Content;
            mainPanel.GetComponent<RectTransform>().sizeDelta = mainMinimized ? MainPillSize : size + new Vector2(0, mainPanel.movable ? ToolbarBand : 0);
            mainContent.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, mainPanel.movable ? ToolbarBand / 2 : 0);
            mainToolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -height / 2 - ToolbarBand / 2);
            LayoutToolbar(mainToolbar.transform, width);
            // The practice coach only needs its move handle. Panel utility buttons compete with the current lesson.
            foreach (var key in new[] { "Minimize", "Recenter" })
                mainToolbar.transform.Find(key)?.gameObject.SetActive(!tutorialChecklist);
            taskToolbarSurface.gameObject.SetActive(false);
            mainContent.transform.Find("ActionRule").gameObject.SetActive(false);
            var rule = mainContent.transform.Find("HeaderRule").GetComponent<RectTransform>();
            rule.gameObject.SetActive(technicalSetup || welcome || compactTask);
            rule.sizeDelta = new Vector2(width - 88, 1.5f); rule.anchoredPosition = new Vector2(0, height / 2 - titleHeight - 38);
            float eyebrow = compactTask ? TaskEyebrow : 0;
            title.rectTransform.sizeDelta = new Vector2(width - 88, titleHeight - eyebrow);
            title.rectTransform.anchoredPosition = new Vector2(0, height / 2 - 32 - statusHeight - eyebrow - (titleHeight - eyebrow) / 2);
            title.alignment = statusHeight > 0 ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            title.gameObject.SetActive(!referenceCompact);
            body.rectTransform.sizeDelta = new Vector2(width - 88, textHeight + 14);
            body.rectTransform.anchoredPosition = new Vector2(0, height / 2 - statusHeight - titleHeight - 58 - (textHeight + 14) / 2);
            body.alignment = statusHeight > 0 ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            foreach (var icon in new[] { positiveIcon, negativeIcon })
            {
                icon.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, height / 2 - 88);
                foreach (var stroke in icon.GetComponentsInChildren<Graphic>(true)) stroke.color = icon == positiveIcon ? Positive : ResearchUITheme.Failure;
            }
            body.gameObject.SetActive(!tutorialChecklist && (!technicalSetup || !setupDetailsVisible));
            checkpointSteps.gameObject.SetActive(checkpoint);
            checkpointSteps.anchoredPosition = body.rectTransform.anchoredPosition;
            if (checkpoint) body.gameObject.SetActive(false);
            welcomeItems.gameObject.SetActive(false);
            setupBrand.gameObject.SetActive(technicalSetup);
            technicalRows.gameObject.SetActive(technicalSetup && setupDetailsVisible);
            technicalOptions.gameObject.SetActive(technicalSetup);
            // Quest 16 Oct: a solo tester changed language/order only inside the dense researcher panel; the two toggles live on the card.
            foreach (Transform option in technicalOptions) option.gameObject.SetActive(technicalSetup && option.name != "Option_input");
            checklist.gameObject.SetActive(false);
            tutorialProgress.gameObject.SetActive(tutorialChecklist && !referenceCompact || compactTask);
            mainPauseEligible = (mainPanel.movable || state == "checking" || state == "fail" || state == "pass") && !welcome && !tutorialDone && !referenceCompact;
            helpUtility.gameObject.SetActive(mainPauseEligible);
            var chip = helpUtility.GetComponent<RectTransform>();
            chip.sizeDelta = new Vector2(80, 80); chip.anchoredPosition = new Vector2(width / 2 - 76, height / 2 - 56);
            UtilityIcon(helpUtility, "pause", "action.pause_short");
            stopUtility.gameObject.SetActive(stopInTitleRow);
            var stopRect = stopUtility.GetComponent<RectTransform>();
            stopRect.sizeDelta = new Vector2(80, 80); stopRect.anchoredPosition = new Vector2(width / 2 - (mainPauseEligible ? 172 : 76), height / 2 - 56);
            UtilityIcon(stopUtility, "stop", "action.stop_short");
            if (compactTask)
            {
                // Minimize joins Stop/Pause at the header's right end, in the Reader's order, instead of floating under the panel.
                var minimize = mainToolbar.transform.Find("Minimize") as RectTransform;
                bool headerMinimize = minimize && minimize.gameObject.activeSelf;
                if (headerMinimize)
                {
                    minimize.anchoredPosition = new Vector2(width / 2 - 76, height / 2 - 56) - mainToolbar.GetComponent<RectTransform>().anchoredPosition;
                    chip.anchoredPosition -= new Vector2(96, 0); stopRect.anchoredPosition -= new Vector2(96, 0);
                }
                int chips = (headerMinimize ? 1 : 0) + (mainPauseEligible ? 1 : 0) + (stopUtility.gameObject.activeSelf ? 1 : 0);
                float lane = width - 88 - chips * 96;
                tutorialProgress.text = TaskProgress(controller.Flow); tutorialProgress.font = Font; tutorialProgress.color = Muted;
                tutorialProgress.fontSize = 26; tutorialProgress.alignment = TextAlignmentOptions.MidlineLeft;
                tutorialProgress.rectTransform.sizeDelta = new Vector2(lane, 52);
                tutorialProgress.rectTransform.anchoredPosition = new Vector2(-width / 2 + 44 + lane / 2, height / 2 - 56);
            }
            // A centred result title sits under its status icon, not in the utility row.
            else if ((helpUtility.gameObject.activeSelf || stopUtility.gameObject.activeSelf) && statusHeight == 0)
            { title.rectTransform.sizeDelta = new Vector2(width - titleReserve, titleHeight); title.rectTransform.anchoredPosition += new Vector2(mainPauseEligible && stopUtility.gameObject.activeSelf ? -94 : -46, 0); }
            tutorialDemo.gameObject.SetActive(coachDemo);
            highlightPractice = false;
            // Tutorial "move the panel": the Reference bar pulses; the coach card is the only written instruction. Never during Task A/B.
            bool moveStep = tutorialChecklist && !tutorialDone && controller && controller.TutorialFact == "reposition";
            foreach (var handle in GetComponentsInChildren<ResearchDragHandle>(true))
            {
                handle.highlight = moveStep && handle.panel == referencePanel;
                var hint = handle.GetComponent<ResearchControlHint>(); if (!hint || !hint.label) continue;
                hint.key = controller && controller.TutorialUsesHands ? "panel.handle.hand" : "panel.handle";
                hint.Show();
            }
            if (!tutorialChecklist) tutorialSuccess.gameObject.SetActive(false);
            retainedLabel.gameObject.SetActive(false);
            if (tutorialChecklist && !referenceCompact)
            {
                int active = controller?.Flow?.TutorialCompleted ?? 0;
                float top = height / 2, left = -width / 2 + Pad, inner = width - 2 * Pad;
                bool help = helpUtility.gameObject.activeSelf;
                string fact = tutorialDone ? null : controller.TutorialInstructionFact;
                // The current micro-action is the heading; the broad step name is quiet context on the progress line.
                title.text = Text(fact == null ? "tutorial.complete" : controller.TutorialNeedsGrab ? "tutorial.fact.regrab" : "tutorial.fact." + fact);
                title.fontSize = 36; title.alignment = TextAlignmentOptions.Left;
                title.rectTransform.sizeDelta = new Vector2(inner - (help ? 236 : 0), 72);
                title.rectTransform.anchoredPosition = new Vector2(help ? -118 : 0, top - 56);
                // Five steps are a small progress indicator only; one instruction competes for attention.
                float progressY = top - 122;
                checklist.sizeDelta = new Vector2(inner - 240, 36); checklist.anchoredPosition = new Vector2(-120, progressY);
                UpdateChecklist();
                tutorialProgress.text = TutorialProgress();
                tutorialProgress.font = Font; tutorialProgress.color = Muted; tutorialProgress.alignment = TextAlignmentOptions.Right; tutorialProgress.fontSize = 24;
                tutorialProgress.rectTransform.sizeDelta = new Vector2(230, 52);
                tutorialProgress.rectTransform.anchoredPosition = new Vector2(width / 2 - Pad - 115, progressY);
                rule.gameObject.SetActive(false);
                float contentTop = top - 128;
                tutorialDemo.localScale = Vector3.one;
                tutorialDemo.anchoredPosition = new Vector2(left + 145, contentTop - 135);
                tutorialDemo.Find("Card").gameObject.SetActive(false);
                body.gameObject.SetActive(true);
                body.alignment = TextAlignmentOptions.TopLeft;
                body.rectTransform.sizeDelta = new Vector2(instructionWidth, textHeight + 8);
                body.rectTransform.anchoredPosition = new Vector2(left + (coachDemo ? TutorialDemoBand : 0) + instructionWidth / 2, contentTop - coachContent / 2);
                demoKind = DemoKind(fact, controller.TutorialUsesHands);
                UpdateTutorialSequence();
                var controlName = tutorialDemo.Find("ControlName").GetComponent<TMP_Text>();
                controlName.font = Font; controlName.color = ResearchUITheme.Blue;
                controlName.text = Text("tutorial.control." + (demoKind.StartsWith("stick") ? "stick" : demoKind.StartsWith("grip") ? "grip" : demoKind.StartsWith("trigger") ? "trigger" : demoKind.StartsWith("pinch") ? "pinch" : "head"));
                controlName.gameObject.SetActive(true);
                controlName.rectTransform.sizeDelta = new Vector2(280, 52);
                controlName.rectTransform.anchoredPosition = new Vector2(0, -154);
                highlightPractice = fact == "grab";
                instructionGroup.alpha = 1; // Next action is readable immediately; feedback animates independently.
                shownFact = fact;
                tutorialSuccess.sizeDelta = new Vector2(inner, 48);
                tutorialSuccess.anchoredPosition = new Vector2(0, contentTop - coachContent - ResearchUITheme.SectionGap - 24);
                tutorialSuccess.Find("Chip").GetComponent<RectTransform>().sizeDelta = new Vector2(inner, 48);
                tutorialSuccess.Find("Checkmark").GetComponent<RectTransform>().anchoredPosition = new Vector2(-inner / 2 + 30, 0);
                successLabel.rectTransform.sizeDelta = new Vector2(inner - 72, 48); successLabel.rectTransform.anchoredPosition = new Vector2(24, 0);
                successLabel.font = Font; successLabel.color = FeedbackColor;
                successLabel.text = CompletionText();
                // A practice result sentence may wrap; grow the chip upward to its text instead of shrinking the font.
                float chipHeight = Mathf.Max(48, successLabel.GetPreferredValues(successLabel.text, inner - 72, 0).y + 12);
                if (chipHeight > 48)
                {
                    tutorialSuccess.sizeDelta = new Vector2(inner, chipHeight); tutorialSuccess.anchoredPosition += new Vector2(0, (chipHeight - 48) / 2);
                    tutorialSuccess.Find("Chip").GetComponent<RectTransform>().sizeDelta = new Vector2(inner, chipHeight);
                    successLabel.rectTransform.sizeDelta = new Vector2(inner - 72, chipHeight);
                }
                tutorialSuccess.gameObject.SetActive(TutorialFeedbackVisible); tutorialSuccess.Find("Checkmark").gameObject.SetActive(FeedbackCheckmark);
            RecordTutorialFeedbackApplied();
                retainedLabel.font = Font; retainedLabel.color = Muted;
                retainedLabel.text = Text("tutorial.retained");
                retainedLabel.rectTransform.sizeDelta = new Vector2(inner, 48);
                retainedLabel.rectTransform.anchoredPosition = tutorialSuccess.anchoredPosition;
                retainedLabel.gameObject.SetActive(controller.TutorialNeedsGrab && string.IsNullOrEmpty(CompletedTutorialFact));
                if (referenceCompact) foreach (var action in actions) action.gameObject.SetActive(false);
            }
            actionsRoot.anchoredPosition = new Vector2(0, -height / 2 + 62);
            var shown = new List<UnityEngine.UI.Button>();
            foreach (var action in actions) if (action.gameObject.activeSelf) shown.Add(action);
            for (int i = 0; i < shown.Count; i++)
            {
                float actionWidth = (width - 88 - (shown.Count - 1) * ResearchUITheme.TargetGap) / Mathf.Max(1, shown.Count);
                var rt = shown[i].GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(actionWidth, compactTask ? 76 : ResearchUITheme.TargetHeight);
                rt.anchoredPosition = new Vector2((i - (shown.Count - 1) / 2f) * (actionWidth + ResearchUITheme.TargetGap), 0);
                var label = shown[i].GetComponentInChildren<TMP_Text>(); label.fontSize = 28;
                if (pause)
                {
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, actionWidth - 24);
                    label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 72);
                    label.rectTransform.anchoredPosition = Vector2.zero;
                }
                if (!pause && shown[i].GetComponent<ResearchUIAction>().command == "panel_recovery")
                {
                    UtilityIcon(shown[i], "recenter", "action.recover_short");
                    shown[i].transform.Find("UtilityIcon").gameObject.SetActive(true);
                }
            }
            if (technicalSetup)
            {
                setupBrand.rectTransform.anchoredPosition = new Vector2(0, height / 2 - 54);
                title.fontSize = 28; title.color = Muted; title.rectTransform.anchoredPosition = new Vector2(0, height / 2 - 107);
                rule.anchoredPosition = new Vector2(0, height / 2 - 150);
                // Readiness rows: 60 high on an 80 pitch, a full section gap below the rule.
                float pitch = ResearchUITheme.RowHeight + ResearchUITheme.RowGap;
                technicalRows.sizeDelta = new Vector2(690, 4 * pitch + ResearchUITheme.RowHeight);
                technicalRows.anchoredPosition = new Vector2(0, height / 2 - 174 - technicalRows.sizeDelta.y / 2);
                for (int i = 0; i < technicalRows.childCount; i++)
                {
                    var row = (RectTransform)technicalRows.GetChild(i);
                    row.anchoredPosition = new Vector2(0, 2 * pitch - i * pitch); row.sizeDelta = new Vector2(690, ResearchUITheme.RowHeight);
                    row.Find("Label").GetComponent<RectTransform>().sizeDelta = new Vector2(410, 56);
                    row.Find("Rule").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -pitch / 2);
                    row.Find("Rule").gameObject.SetActive(i < technicalRows.childCount - 1);
                }
                body.rectTransform.anchoredPosition = new Vector2(0, height / 2 - 174 - (textHeight + 14) / 2);
                technicalOptions.anchoredPosition = new Vector2(0, -height / 2 + 160);
                float optionWidth = width - 120;
                for (int i = 0; i < technicalOptions.childCount; i++)
                {
                    var option = (RectTransform)technicalOptions.GetChild(i);
                    if (!option.gameObject.activeSelf) continue;
                    bool toggle = option.name.StartsWith("Option_", StringComparison.Ordinal);
                    float half = (optionWidth - ResearchUITheme.TargetGap) / 2;
                    option.sizeDelta = new Vector2(toggle ? half : optionWidth, 64);
                    option.anchoredPosition = toggle ? new Vector2((option.name == "Option_language" ? -1 : 1) * (half + ResearchUITheme.TargetGap) / 2, 180)
                        : option == technicalDetails.transform ? new Vector2(0, 90) : Vector2.zero;
                    ResearchUITheme.Button(option.GetComponent<UnityEngine.UI.Button>(), ResearchButtonVariant.Secondary);
                    var label = option.GetComponentInChildren<TMP_Text>(); label.fontSize = 26; label.font = Font;
                }
            }
            else title.color = Ink;
            if (referenceCompact)
            {
                shownFact = controller.TutorialFact;
                body.gameObject.SetActive(false);
                tutorialSuccess.gameObject.SetActive(false); rule.gameObject.SetActive(false);
                foreach (var action in actions) action.gameObject.SetActive(false);
            }
            mainContent.SetActive(!mainMinimized && !referenceCompact);
            mainPanel.gameObject.SetActive(!referenceCompact);
            var pauseIcon = mainContent.transform.Find("PauseIcon");
            pauseIcon.gameObject.SetActive(false);
            pauseIcon.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, height / 2 - 92);
            foreach (var stroke in pauseIcon.GetComponentsInChildren<TrainingRoundedGraphic>()) stroke.color = new Color(.55f, .79f, 1);
            mainContent.transform.Find("WaitingChip").gameObject.SetActive(false);
            safetyLabel.gameObject.SetActive(false);
            waitingLabel.rectTransform.sizeDelta = new Vector2(width - 88, 50);
            float waitingY = -height / 2 + (pause ? 62 + ResearchUITheme.TargetHeight / 2 + ResearchUITheme.SpaceXL + 25 : 48);
            waitingLabel.rectTransform.anchoredPosition = new Vector2(0, waitingY); waitingLabel.fontSize = 24;
            // Completion text already names the researcher; a figure beside its first line read as an unexplained marker
            // (and sat on the panel edge in Japanese). The icon stays only beside the "Waiting for researcher" label.
            var researcher = (RectTransform)mainContent.transform.Find("WaitingResearcherIcon");
            researcher.gameObject.SetActive(waitingLabel.gameObject.activeSelf);
            waitingLabel.rectTransform.anchoredPosition += new Vector2(24, 0);
            researcher.anchoredPosition = new Vector2(-Mathf.Min(waitingLabel.preferredWidth, width - 160) / 2 - 12, waitingY);
        }

        float LayoutCheckpointSteps(float width)
        {
            var lines = Text("checkpoint.body").Split('\n'); float total = 0;
            for (int i = 0; i < 3; i++)
            {
                var row = (RectTransform)checkpointSteps.GetChild(i); var label = row.Find("Label").GetComponent<TMP_Text>();
                label.text = lines[i].Substring(1).TrimStart().Replace("、", "、\n"); label.font = Font; label.color = Ink; label.fontSize = 28;
                float height = Mathf.Max(64, label.GetPreferredValues(label.text, width - 164, 1000).y + 12);
                row.sizeDelta = new Vector2(width - 88, height); row.anchoredPosition = new Vector2(0, -total - height / 2);
                ((RectTransform)row.Find("RowIcon")).anchoredPosition = new Vector2(-row.sizeDelta.x / 2 + 24, 0);
                label.rectTransform.sizeDelta = new Vector2(width - 164, height); label.rectTransform.anchoredPosition = new Vector2(38, 0);
                total += height + 16;
            }
            total -= 16; checkpointSteps.sizeDelta = new Vector2(width - 88, total);
            foreach (RectTransform row in checkpointSteps) row.anchoredPosition += new Vector2(0, total / 2);
            return total;
        }

        void UpdateChecklist()
        {
            int active = controller?.Flow?.TutorialCompleted ?? 0;
            float first = -checklist.rect.width / 2 + 14;
            for (int i = 0; i < 5; i++)
            {
                bool current = i == active, completed = i < active;
                stepRows[i].gameObject.SetActive(true);
                stepRows[i].anchoredPosition = new Vector2(first + i * 40, 0); stepRows[i].sizeDelta = new Vector2(28, 28);
                stepBackgrounds[i].gameObject.SetActive(true); stepBackgrounds[i].rectTransform.sizeDelta = stepRows[i].sizeDelta;
                stepBackgrounds[i].color = completed ? ResearchUITheme.Elevated : current ? Accent : ResearchUITheme.Glass;
                ResearchUITheme.Surface(stepBackgrounds[i], false, 14).edgeAlpha = current ? .9f : completed ? .4f : .1f;
                // Names/status stay for logs and assistive review but are not drawn as competing instructions.
                stepLabels[i].text = Text("tutorial." + Steps[i]); stepLabels[i].gameObject.SetActive(false);
                stepDetails[i].text = Text(completed ? "tutorial.done" : "tutorial.future"); stepDetails[i].gameObject.SetActive(false);
                stepChecks[i].SetActive(completed); stepChecks[i].GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                if (checklistAnimation == null) stepChecks[i].transform.localScale = Vector3.one * DotCheckScale;
                stepRows[i].Find("StepNumber").gameObject.SetActive(false);
                var line = checklist.Find("ProgressLine_" + i); if (line) line.gameObject.SetActive(false);
                var dot = stepRows[i].Find("StateDot"); if (dot) dot.gameObject.SetActive(false);
            }
        }

        void LayoutReferenceControls(float height)
        {
            // Shared ownership below exposes one global Pause across the readable surfaces.
            if (!referenceExpanded.transform.Find("Pause")) Button(referenceExpanded.transform, "Pause", Vector2.zero, new Vector2(128, 56), "", "pause", false);
            if (!referenceExpanded.transform.Find("Stop")) Button(referenceExpanded.transform, "Stop", Vector2.zero, new Vector2(80, 80), "", "stop", false);
            string[] keys = { "Stop", "Pause", "Minimize", "Close" };
            for (int i = 0; i < keys.Length; i++)
            {
                var control = referenceExpanded.transform.Find(keys[i]) as RectTransform;
                if (!control) continue;
                control.sizeDelta = new Vector2(80, 80);
                control.anchoredPosition = new Vector2(218 + i * 96, height / 2 - 56);
                UtilityIcon(control.GetComponent<UnityEngine.UI.Button>(), keys[i].ToLowerInvariant(), keys[i] == "Pause" ? "action.pause_short" : keys[i] == "Stop" ? "action.stop_short" : "action." + keys[i].ToLowerInvariant());
            }
        }

        void PrepareTutorialReaderCue()
        {
            if (!referenceTutorialCue) referenceTutorialCue = Label(referenceExpanded.transform, "TutorialCue", Vector2.zero, new Vector2(692, 52), "", 28);
            if (!referenceTutorialProgress) referenceTutorialProgress = Label(referenceExpanded.transform, "TutorialReaderProgress", Vector2.zero, new Vector2(692, 52), "", 24);
            referenceTutorialCue.gameObject.SetActive(TutorialReader);
            referenceTutorialProgress.gameObject.SetActive(TutorialReader);
            if (!TutorialReader) return;
            shownFact = controller.TutorialFact;
            referenceTutorialCue.font = Font; referenceTutorialCue.color = Ink; referenceTutorialCue.fontSize = 28;
            referenceTutorialCue.alignment = TextAlignmentOptions.TopLeft;
            referenceTutorialCue.text = Text(controller.TutorialInstructionKey);
            referenceTutorialProgress.font = Font; referenceTutorialProgress.color = Muted;
            referenceTutorialProgress.alignment = TextAlignmentOptions.Right;
            referenceTutorialProgress.rectTransform.sizeDelta = new Vector2(692, 52);
            referenceTutorialProgress.text = TutorialProgress();
            // Localized lines keep their authored font size; reserve the actual measured cue height.
            tutorialCueHeight = Mathf.Max(52, referenceTutorialCue.GetPreferredValues(referenceTutorialCue.text, 692, 1500).y + 8);
        }

        void LayoutReferenceTutorialCue(float height)
        {
            PrepareTutorialReaderCue();
            if (!TutorialReader) return;
            referenceTutorialProgress.rectTransform.anchoredPosition = new Vector2(128, 277);
            referenceTutorialProgress.rectTransform.sizeDelta = new Vector2(820, 52); referenceTutorialProgress.fontSize = 24;
            referenceTutorialCue.rectTransform.anchoredPosition = new Vector2(128, 224);
            referenceTutorialCue.rectTransform.sizeDelta = new Vector2(820, 52); referenceTutorialCue.fontSize = 25;
            tutorialDemo.gameObject.SetActive(false);
            tutorialSuccess.SetParent(referenceExpanded.transform, false); tutorialSuccess.Find("Chip").gameObject.SetActive(false);
            // Capture C03: the former (-284,-354) 512x52 slot overlapped the video Play control and time label (viewer spans x>=-281,
            // transport at its bottom edge). The sidebar column below the last format tab (bottom y=-275) is free in every format.
            tutorialSuccess.anchoredPosition = new Vector2(-438, -338); tutorialSuccess.sizeDelta = new Vector2(240, 100);
            tutorialSuccess.Find("Checkmark").GetComponent<RectTransform>().anchoredPosition = new Vector2(-96, 26);
            successLabel.rectTransform.anchoredPosition = new Vector2(16, 0); successLabel.rectTransform.sizeDelta = new Vector2(188, 96);
            successLabel.textWrappingMode = TextWrappingModes.Normal;
            if (!string.IsNullOrEmpty(CompletedTutorialFact)) ShowTutorialConfirmation(); else tutorialSuccess.gameObject.SetActive(false);
        }

        void LayoutToolbar(Transform toolbar, float width)
        {
            // Keep the generous light hit surface; only the grip receives state tint.
            var handle = toolbar.Find("DragHandle").GetComponent<RectTransform>();
            handle.anchoredPosition = Vector2.zero; handle.sizeDelta = new Vector2(HandleWidth, HandleHeight);
            // design_rule D01: every movable panel shows only the grip line; the transparent pill stays the generous hit area.
            var pill = handle.GetComponent<TrainingRoundedGraphic>(); pill.color = Color.clear; pill.raycastTarget = true; pill.Radius = HandleHeight / 2;
            var edge = pill.GetComponent<ResearchGlassEffect>(); if (edge) edge.edgeAlpha = 0;
            var grip = handle.Find("GripLine").GetComponent<TrainingRoundedGraphic>();
            grip.color = Color.white; grip.canvasRenderer.SetColor(Muted); grip.Radius = 3; grip.rectTransform.sizeDelta = new Vector2(96, 6);
            var hint = handle.Find("FocusLabel") as RectTransform;
            if (hint) { hint.anchoredPosition = new Vector2(0, -HandleHeight / 2 - 30); hint.sizeDelta = new Vector2(360, 48); }
            foreach (var key in new[] { "Minimize", "Recenter", "Close" })
            {
                var control = toolbar.Find(key); if (!control) continue;
                var rt = control.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(key == "Minimize" ? -224 : key == "Recenter" ? 224 : 320, 0);
                rt.sizeDelta = new Vector2(80, 80);
                bool distance = control.GetComponent<ResearchUIAction>()?.command == "reference_distance";
                UtilityIcon(control.GetComponent<UnityEngine.UI.Button>(), distance ? (readerCloser ? "farther" : "closer") : key.ToLowerInvariant(),
                    distance ? (readerCloser ? "action.reader_default" : "action.reader_closer") : "action." + key.ToLowerInvariant());
            }
        }

        void UtilityIcon(UnityEngine.UI.Button button, string kind, string hintKey)
        {
            var action = button.GetComponent<ResearchUIAction>();
            bool recovery = action && action.command == "panel_recovery";
            bool compactRecovery = recovery && button.GetComponent<RectTransform>().sizeDelta.x < 200;
            ResearchUITheme.Button(button, recovery ? ResearchButtonVariant.Secondary : ResearchButtonVariant.Icon);
            bool pause = kind == "pause", stop = kind == "stop";
            if (action) action.labelKey = stop ? "action.stop_short" : pause ? "action.pause_caption" : recovery ? hintKey : "";
            var label = button.transform.Find("Label");
            if (label)
            {
                label.gameObject.SetActive(pause || stop || recovery);
                if (pause || stop || recovery)
                {
                    var caption = label.GetComponent<TMP_Text>(); caption.text = Text(action.labelKey);
                    caption.font = Font; caption.fontSize = ResearchUITheme.Caption; caption.lineSpacing = 0;
                    caption.textWrappingMode = compactRecovery ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
                    caption.alignment = TextAlignmentOptions.Center; caption.margin = Vector4.zero;
                    var rt = caption.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                    rt.anchoredPosition = recovery && !compactRecovery ? new Vector2(20, 0) : new Vector2(0, -19);
                    rt.sizeDelta = recovery && !compactRecovery ? new Vector2(button.GetComponent<RectTransform>().sizeDelta.x - 68, 72) : new Vector2(button.GetComponent<RectTransform>().sizeDelta.x - 8, 40);
                }
            }
            var icon = button.transform.Find("UtilityIcon")?.GetComponent<ResearchUtilityIcon>();
            if (!icon) icon = Rect(button.transform, "UtilityIcon", Vector2.zero, new Vector2(28, 28)).gameObject.AddComponent<ResearchUtilityIcon>();
            if (!icon.GetComponent<CanvasRenderer>()) icon.gameObject.AddComponent<CanvasRenderer>();
            icon.kind = kind; icon.color = Ink; icon.raycastTarget = false; icon.SetVerticesDirty();
            icon.rectTransform.anchoredPosition = recovery && !compactRecovery ? new Vector2(-button.GetComponent<RectTransform>().sizeDelta.x / 2 + 32, 0) : new Vector2(0, pause || stop || compactRecovery ? 17 : 0);
            var hint = button.GetComponent<ResearchControlHint>(); if (!hint) hint = button.gameObject.AddComponent<ResearchControlHint>();
            hint.enabled = true; hint.view = this; hint.key = hintKey;
            if (!hint.label) hint.label = Label(button.transform, "FocusLabel", new Vector2(-70, -66), new Vector2(260, 72), "", 24);
            hint.label.alignment = TextAlignmentOptions.Center;
            hint.label.rectTransform.anchoredPosition = new Vector2(-70, -66); hint.label.rectTransform.sizeDelta = new Vector2(260, 72);
            hint.label.transform.Find("TooltipSurface")?.gameObject.SetActive(false);
            if (!hint.background)
            {
                var surface = Round(button.transform, "TooltipSurface", new Vector2(-70, -66), new Vector2(260, 72), ResearchUITheme.Content, 10);
                surface.transform.SetSiblingIndex(hint.label.transform.GetSiblingIndex()); hint.background = surface.gameObject;
            }
            hint.enabled = !recovery;
            hint.Show();
        }

        void PrepareRecoveryUtility()
        {
            if (!RecoveryCanvas) RecoveryCanvas = transform.Find("PanelRecoveryUtility")?.GetComponent<Canvas>();
            if (!RecoveryCanvas)
            {
                RecoveryCanvas = CanvasRoot(transform, "PanelRecoveryUtility", new Vector2(200, 96)); RecoveryCanvas.sortingOrder = 12;
                Round(RecoveryCanvas.transform, "Surface", Vector2.zero, new Vector2(200, 96), Background, 16, true);
                Button(RecoveryCanvas.transform, "RecoverPanels", Vector2.zero, RecoveryButtonSize, "", "panel_recovery", false);
                Button(RecoveryCanvas.transform, "Pause", Vector2.zero, new Vector2(80, 80), "", "pause", false);
                recoveryPanel = RecoveryCanvas.gameObject.AddComponent<ResearchPanelMotion>();
                RecoveryCanvas.gameObject.SetActive(false);
            }
            if (!RecoveryCanvas.transform.Find("Stop"))
                Button(RecoveryCanvas.transform, "Stop", Vector2.zero, new Vector2(80, 80), "", "stop", false);
            // Serialized recovery controls use the same layout as newly built controls.
            foreach (string key in new[] { "RecoverPanels", "Pause", "Stop" })
            {
                var control = RecoveryCanvas.transform.Find(key).GetComponent<UnityEngine.UI.Button>();
                var rect = control.GetComponent<RectTransform>(); rect.sizeDelta = key == "RecoverPanels" ? RecoveryButtonSize : new Vector2(80, 80);
                UtilityIcon(control, key == "Stop" ? "stop" : key == "Pause" ? "pause" : "recenter", key == "Stop" ? "action.stop_short" : key == "Pause" ? "action.pause_short" : "action.recover_panels");
            }
            recoveryPanel = RecoveryCanvas.GetComponent<ResearchPanelMotion>();
            recoveryPanel.view = this; recoveryPanel.panelId = "recovery"; recoveryPanel.centered = true; recoveryPanel.movable = false;
            LayoutRecoveryUtility(false, compact: true);
            if (ParticipantCanvases != null && Array.IndexOf(ParticipantCanvases, RecoveryCanvas) < 0)
            { var canvases = new List<Canvas>(ParticipantCanvases) { RecoveryCanvas }; ParticipantCanvases = canvases.ToArray(); }
        }

        bool LayoutRecoveryUtility(bool fallbackPause, bool compact = false, bool fallbackStop = false)
        {
            var rect = (RectTransform)RecoveryCanvas.transform;
            var recoverSize = compact ? new Vector2(128, 80) : RecoveryButtonSize;
            int safetyActions = (fallbackPause ? 1 : 0) + (fallbackStop ? 1 : 0);
            var size = recoverSize + new Vector2(16 + 96 * safetyActions, 16);
            bool changed = rect.sizeDelta != size;
            rect.sizeDelta = size;
            RecoveryCanvas.transform.Find("Surface").GetComponent<RectTransform>().sizeDelta = size;
            var recover = RecoveryCanvas.transform.Find("RecoverPanels").GetComponent<RectTransform>();
            recover.sizeDelta = recoverSize;
            recover.anchoredPosition = new Vector2(-48 * safetyActions, 0);
            var pause = RecoveryCanvas.transform.Find("Pause").GetComponent<RectTransform>();
            pause.anchoredPosition = new Vector2(recoverSize.x / 2 + 8 - 48 * (safetyActions - 1), 0);
            pause.gameObject.SetActive(fallbackPause);
            var stop = RecoveryCanvas.transform.Find("Stop").GetComponent<RectTransform>();
            stop.anchoredPosition = new Vector2(recoverSize.x / 2 + 8 - 48 * (safetyActions - 1) + (fallbackPause ? 96 : 0), 0);
            stop.gameObject.SetActive(fallbackStop);
            if (changed) UtilityIcon(recover.GetComponent<UnityEngine.UI.Button>(), "recenter", compact ? "action.recover_short" : "action.recover_panels");
            return changed;
        }

        public void RefreshRecoveryUtility()
        {
            if (!RecoveryCanvas || !Head || !gameObject.activeInHierarchy) return;
            RecoveryCanvas.worldCamera = Head.GetComponent<Camera>();
            // Match Command("pause") safety gates; floor readiness and render comparisons do not block Pause.
            bool available = controller && controller.Flow != null && !controller.Flow.Paused && !controller.Transitioning
                && string.IsNullOrEmpty(controller.Error) && (mainPauseEligible || controller.ReferenceOpen)
                && (controller.Record == null || (controller.Record.session_state == RegistryState.InProgress && !SessionWriterUnavailable))
                && (controller.Logger == null || controller.LoggerReady);
            bool mainOwns = available && mainPauseEligible && mainContent.activeInHierarchy && mainPanel.GetComponent<Canvas>().enabled
                && mainGroup.alpha > .01f && mainGroup.interactable && mainGroup.blocksRaycasts
                && !mainPanel.PlacementBlocked && mainPanel.IsReadableFromHead;
            bool referenceOwns = available && controller.ReferenceOpen && !controller.ReferenceMinimized
                && referenceExpanded.activeInHierarchy && referencePanel.GetComponent<Canvas>().enabled && referenceGroup.alpha > .01f
                && referenceGroup.interactable && referenceGroup.blocksRaycasts && !referencePanel.PlacementBlocked && referencePanel.IsReadableFromHead;
            helpUtility.gameObject.SetActive(mainOwns && !referenceOwns);
            var referencePause = referenceExpanded.transform.Find("Pause"); if (referencePause) referencePause.gameObject.SetActive(referenceOwns);
            bool stopAvailable = controller && controller.ParticipantCanStop;
            bool referenceStopOwns = stopAvailable && controller.ReferenceOpen && !controller.ReferenceMinimized
                && referenceExpanded.activeInHierarchy && referencePanel.GetComponent<Canvas>().enabled && referenceGroup.alpha > .01f
                && referenceGroup.interactable && referenceGroup.blocksRaycasts && !referencePanel.PlacementBlocked && referencePanel.IsReadableFromHead;
            bool mainStopOwns = stopAvailable && !referenceStopOwns && mainContent.activeInHierarchy && mainPanel.GetComponent<Canvas>().enabled
                && mainGroup.alpha > .01f && mainGroup.interactable && mainGroup.blocksRaycasts && !mainPanel.PlacementBlocked && mainPanel.IsReadableFromHead;
            stopUtility.gameObject.SetActive(mainStopOwns);
            var referenceStop = referenceExpanded.transform.Find("Stop"); if (referenceStop) referenceStop.gameObject.SetActive(referenceStopOwns);
            bool fallbackStop = stopAvailable && !mainStopOwns && !referenceStopOwns;
            // Quest 16 Oct: each glance at the table re-placed this strip (34 recentres in 2 min). A short dwell ignores glances;
            // Menu still pauses at once.
            // A minimized or closed panel is a deliberate state, not a glance: Stop appears at once there.
            bool glance = mainContent.activeInHierarchy && !controller.ReferenceOpen;
            if (!fallbackStop || !glance) fallbackSince = float.NegativeInfinity;
            else if (float.IsNegativeInfinity(fallbackSince)) fallbackSince = Time.unscaledTime;
            fallbackStop &= !glance || recoveryWasNeeded || Time.unscaledTime - fallbackSince >= FallbackDwellSeconds;
            bool layoutChanged = LayoutRecoveryUtility(false, compact: true, fallbackStop: fallbackStop);
            recoveryPanel.centered = true;
            bool paused = controller && controller.Flow?.Paused == true && controller.PauseKind == "participant" && !controller.ParticipantMayResume
                && !controller.Transitioning && string.IsNullOrEmpty(controller.Error);
            bool needed = fallbackStop || (paused && (mainPanel.PlacementBlocked || !mainPanel.IsReadableFromHead
                || (recoveryWasNeeded && mainPanel.RecenterPending)));
            if (!paused || mainPanel.PlacementBlocked || needed == recoveryWasNeeded)
                recoveryTransitionSince = float.NegativeInfinity;
            else
            {
                if (float.IsNegativeInfinity(recoveryTransitionSince)) recoveryTransitionSince = Time.unscaledTime;
                if (Time.unscaledTime - recoveryTransitionSince < .08f) needed = recoveryWasNeeded;
            }
            if (RecoveryCanvas.gameObject.activeSelf != needed) RecoveryCanvas.gameObject.SetActive(needed);
            if (needed && (!recoveryWasNeeded || layoutChanged) && (layoutChanged || !recoveryPlaced || !recoveryPanel.IsReadableFromHead))
            { recoveryPanel.Recenter(false, false); recoveryPlaced = true; }
            recoveryWasNeeded = needed;
        }

        public void ShowHelper()
        {
            int step = Mathf.Clamp(controller?.Flow?.TutorialCompleted ?? 0, 0, 4);
            if (!controller || controller.Flow == null || !controller.Flow.IsTutorial || controller.Flow.Paused || !string.IsNullOrEmpty(controller.Error)) return;
            helperText.text = Text("tutorial." + Steps[step] + ".helper");
            helperText.gameObject.SetActive(true); helperCanvas.transform.Find("Bubble").gameObject.SetActive(true);
            if (helperDismiss != null) StopCoroutine(helperDismiss);
            helperDismiss = StartCoroutine(DismissHelper());
        }

        IEnumerator DismissHelper()
        {
            yield return new WaitForSecondsRealtime(4);
            helperText.gameObject.SetActive(false); helperCanvas.transform.Find("Bubble").gameObject.SetActive(false); helperDismiss = null;
        }

        public void RefreshReference()
        {
            bool open = controller && controller.ReferenceOpen && string.IsNullOrEmpty(controller.Error) && !SessionEnded && !SessionWriterUnavailable;
            if (!open)
            {
                // Remember the placed Reader pose (participants cannot move it in the fixed policy) before the close visual runs.
                if (ResearchUIPolicy.ReaderPoseFixedPerTask && referencePanel.gameObject.activeSelf && !referencePanel.PlacementBlocked
                    && !referencePanel.RecenterPending && ReaderAnchorKey != null)
                {
                    readerAnchorPosition = referencePanel.transform.position; readerAnchorRotation = referencePanel.transform.rotation;
                    readerAnchorKey = ReaderAnchorKey;
                }
                ReferenceViewer?.SetVisible(false);
                if (referenceTutorialCue) referenceTutorialCue.gameObject.SetActive(false);
                referenceGroup.blocksRaycasts = referenceGroup.interactable = false;
                if (referencePanel.gameObject.activeSelf && referenceClose == null && Application.isPlaying && isActiveAndEnabled && string.IsNullOrEmpty(controller?.Error)) referenceClose = StartCoroutine(CloseReferenceVisual());
                else if (!Application.isPlaying || !string.IsNullOrEmpty(controller?.Error)) referencePanel.gameObject.SetActive(false);
                mainGroup.alpha = 1; mainGroup.interactable = true; mainGroup.blocksRaycasts = true;
                NotifyReferenceVisibility("reference_closed");
                if (compactTutorialReference) Refresh();
                return;
            }
            if (referenceClose != null) { StopCoroutine(referenceClose); referenceClose = null; }
            bool wasOpen = referencePanel.gameObject.activeSelf;
            referencePanel.gameObject.SetActive(true); referencePanel.movable = true;
            referenceGroup.blocksRaycasts = referenceGroup.interactable = true;
            bool minimized = controller.ReferenceMinimized;
            if (minimized) ReferenceViewer?.SetVisible(false); // Save native media time before hierarchy deactivation stops the decoder.
            referenceExpanded.SetActive(!minimized); referencePill.SetActive(minimized);
            referencePill.GetComponent<RectTransform>().sizeDelta = ReferencePillSize;
            pillText.text = Text("reference.restore_short"); pillText.font = Font;
            PrepareReferenceViewer(); LayoutReaderShell();
            ReferenceViewer.UseRegistry(controller.ReaderResource);
            ReferenceViewer.Bind(ReferenceKey, controller.ReferenceSection, controller.IsTechnical, Text("reference.content_unavailable"));
            if (!wasOpen || previousMinimized != minimized) Animate(referenceGroup, ref referenceAnimation, enter: true);
            previousMinimized = minimized; previousSection = controller.ReferenceSection; previousReferenceKey = ReferenceKey;
            Canvas.ForceUpdateCanvases(); RefreshPageControls();
            if (!wasOpen)
            {
                if (controller.Flow.IsTaskActive && mainPanel.PlacementBlocked) mainPanel.Recenter(false, false);
                // Fixed policy: reopen where the Reader was first placed in this task (RestorePose re-validates clearance and
                // falls back to a logged System placement); legacy policy keeps the original head-relative placement.
                if (ResearchUIPolicy.ReaderPoseFixedPerTask && readerAnchorKey != null && readerAnchorKey == ReaderAnchorKey)
                    referencePanel.RestorePose(readerAnchorPosition, readerAnchorRotation);
                else { readerCloser = false; referencePanel.Recenter(false, false, reason: ResearchUIPolicy.ReaderPoseFixedPerTask ? "fixed_reader_anchor_established" : "recenter"); }
            }
            bool showTask = controller.Flow.IsTaskActive;
            mainGroup.interactable = showTask; mainGroup.blocksRaycasts = showTask; mainGroup.alpha = 1;
            SyncReaderVisibility(); NotifyReferenceVisibility("reference_refresh");
        }

        public bool LocalCommand(string command, string input)
        {
            bool allowed = controller && controller.LocalUICommandsAllowed, accepted = false;
            if (command != null && command.StartsWith("reference_media.", StringComparison.Ordinal))
                return allowed && ReferenceViewer && ReferenceViewer.Command(command, input);
            switch (command)
            {
                case "technical_details":
                    if (!controller || !controller.TechnicalSelfSetup || controller.Record != null || controller.Flow != null
                        || controller.Transitioning || !string.IsNullOrEmpty(controller.Error) || !controller.TechnicalReady
                        || !controller.FloorReady || !controller.GuardTracked(Oculus.Interaction.Input.Handedness.Left)
                        || !controller.GuardTracked(Oculus.Interaction.Input.Handedness.Right)) return false;
                    setupDetailsRequested = !setupDetailsRequested; Refresh(); accepted = true; break;
                case "panel_recovery":
                    // Geometry recovery also works on a paused/error card; it never changes task state or resumes a study.
                    if (!controller || controller.Flow == null || !Head || controller.Transitioning) return false;
                    if (!controller.IsTechnical && (input == "None" || input.StartsWith("Simulated", StringComparison.Ordinal))) return false;
                    if (controller.Flow.Paused && controller.PauseKind == "participant")
                    { hasTaskPose = false; recoverTaskOnResume = controller.Flow.IsTaskActive; }
                    if (mainPanel && mainPanel.gameObject.activeInHierarchy) mainPanel.Recenter(false, false, input, reason: "explicit_panel_recovery");
                    if (controller.ReferenceOpen && referencePanel && referencePanel.gameObject.activeInHierarchy)
                        referencePanel.Recenter(false, false, input, reason: "explicit_panel_recovery");
                    controller.Log("PANEL_RECENTER", "all_visible_panels", "RecoveryRequested", inputSource: input);
                    accepted = true; break;
                case "reference_previous": case "reference_next":
                    if (!allowed || !controller.ReferenceOpen || controller.ReferenceMinimized || !ReferenceViewer) return false;
                    accepted = ReferenceViewer.Navigate(command == "reference_previous" ? -1 : 1, input);
                    RefreshPageControls(); break;
                case "main_minimize":
                    if (!allowed || !mainPanel.movable || mainMinimized) return false;
                    mainMinimized = true; mainContent.SetActive(false); mainPill.SetActive(true); mainPanel.GetComponent<RectTransform>().sizeDelta = MainPillSize;
                    controller.Log("PANEL_MINIMIZE", "task_information", "Minimized", source: "User", inputSource: input); Animate(mainGroup, ref mainAnimation, enter: true); accepted = true; break;
                case "main_restore":
                    if (!allowed || !mainMinimized) return false;
                    mainMinimized = false; mainContent.SetActive(true); mainPill.SetActive(false); mainPanel.GetComponent<RectTransform>().sizeDelta = expandedMainSize + new Vector2(0, mainPanel.movable ? ToolbarBand : 0);
                    mainPanel.EnsureClear();
                    controller.Log("PANEL_RESTORE", "task_information", "Restored", inputSource: input); Animate(mainGroup, ref mainAnimation, enter: true); accepted = true; break;
                case "main_recenter":
                    if (!ResearchUIPolicy.ParticipantRecenterAllowed)
                    { controller?.Log("PANEL_RECENTER", "task_information", "RejectedByUIPolicy", source: "User", inputSource: input, reason: ResearchUIPolicy.Active); return false; }
                    if (!allowed || !Head || !mainPanel.movable) return false;
                    mainPanel.Recenter(true, true, input); accepted = true; break;
                case "reference_distance":
                {
                    if (!ResearchUIPolicy.ReaderDistanceAllowed(controller && controller.IsTechnical))
                    { controller?.Log("READER_DISTANCE", "reference", "RejectedByUIPolicy", source: "User", inputSource: input, reason: ResearchUIPolicy.Active); return false; }
                    if (!allowed || !Head || !controller.ReferenceOpen || controller.ReferenceMinimized || referencePanel.PlacementBlocked || referencePanel.RecenterPending) return false;
                    var from = new Pose(referencePanel.transform.position, referencePanel.transform.rotation);
                    Pose to;
                    if (readerCloser) to = readerDefaultPose;
                    else if (referencePanel.TryCloserPose(out to)) readerDefaultPose = from;
                    else
                    {
                        controller.Log("READER_DISTANCE", "reference", "NoClearCloserPose", after: new { pose = referencePanel.Pose(), head = Floats(Head.position) }, source: "User", inputSource: input,
                            reason: referencePanel.LastRejection);
                        return false;
                    }
                    var fromLog = referencePanel.Pose();
                    referencePanel.RestorePose(to.position, to.rotation);
                    readerCloser = !readerCloser; readerDistanceChanges++;
                    controller.Log("READER_DISTANCE", "reference", readerCloser ? "Closer" : "Default", before: fromLog,
                        after: new { position = Floats(to.position), yaw = to.rotation.eulerAngles.y, head = Floats(Head.position), head_distance_m = Vector3.Distance(Head.position, to.position), changes = readerDistanceChanges,
                            policy = ResearchUIPolicy.ReaderDistanceVersion }, source: "User", inputSource: input);
                    LayoutToolbar(referenceExpanded.transform.Find("Toolbar"), ReaderWidth);
                    accepted = true; break;
                }
                case "reference_recenter":
                    if (!ResearchUIPolicy.ParticipantRecenterAllowed)
                    { controller?.Log("PANEL_RECENTER", "reference", "RejectedByUIPolicy", source: "User", inputSource: input, reason: ResearchUIPolicy.Active); return false; }
                    if (!allowed || !Head || !controller.ReferenceOpen || controller.ReferenceMinimized) return false;
                    referencePanel.Recenter(true, true, input); accepted = true; break;
                default: accepted = controller && controller.Command(command, input); break;
            }
            controller?.TutorialPromptShown();
            return accepted && string.IsNullOrEmpty(controller.Error);
        }

        public void RecenterAll(bool participant)
        {
            mainMinimized = false;
            Refresh();
            if (mainPanel.gameObject.activeSelf) mainPanel.Recenter(participant, false);
            if (referencePanel.gameObject.activeSelf) referencePanel.Recenter(participant, false);
        }

        public IEnumerator Fade(bool toBlack)
        {
            if (Head) transitionShade.transform.SetPositionAndRotation(Head.position + Head.forward * .3f, Head.rotation);
            float from = transitionShade.alpha, to = toBlack ? 1 : 0;
            for (float elapsed = 0; elapsed < ResearchMotion.Panel; elapsed += Time.unscaledDeltaTime)
            { transitionShade.alpha = Mathf.Lerp(from, to, ResearchMotion.Ease(elapsed / ResearchMotion.Panel)); yield return null; }
            transitionShade.alpha = to;
        }
        // Panel enter fades in from hidden. A state change on a visible slate swaps content in place: the whole
        // slate dipping to 40% read as a flash in the headset. Opacity only (sub-pixel scale shimmers text), always
        // continuing from the rendered alpha so rapid changes never snap.
        void Animate(CanvasGroup group, ref Coroutine animation, bool enter = false)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) { group.alpha = 1; return; }
            if (animation != null) StopCoroutine(animation);
            if (enter) group.alpha = 0;
            animation = StartCoroutine(Entrance(group, enter ? ResearchMotion.Panel : ResearchMotion.State));
        }
        IEnumerator Entrance(CanvasGroup group, float duration)
        {
            float from = group.alpha;
            for (float elapsed = 0; elapsed < duration && from < 1; elapsed += Time.unscaledDeltaTime)
            { group.alpha = Mathf.Lerp(from, 1, ResearchMotion.Ease(elapsed / duration)); yield return null; }
            group.alpha = 1;
        }
        void AnimateSuccessIcon()
        {
            var group = positiveIcon.GetComponent<CanvasGroup>();
            if (!group) group = positiveIcon.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            if (!Application.isPlaying || !isActiveAndEnabled) { group.alpha = 1; positiveIcon.transform.localScale = Vector3.one; return; }
            if (successAnimation != null) StopCoroutine(successAnimation);
            successAnimation = StartCoroutine(SuccessIcon(group));
        }
        IEnumerator SuccessIcon(CanvasGroup group)
        {
            for (float elapsed = 0; elapsed < ResearchMotion.State; elapsed += Time.unscaledDeltaTime)
            {
                float progress = ResearchMotion.Ease(elapsed / ResearchMotion.State);
                group.alpha = progress; positiveIcon.transform.localScale = Vector3.one * Mathf.Lerp(.9f, 1, progress);
                yield return null;
            }
            group.alpha = 1; positiveIcon.transform.localScale = Vector3.one; successAnimation = null;
        }
        // Instruction swap inside the coach: opacity only, never the RectTransform Refresh owns.
        void FadeIn(CanvasGroup group, ref Coroutine animation)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) { group.alpha = 1; return; }
            if (animation != null) StopCoroutine(animation);
            animation = StartCoroutine(FadeGroup(group, Mathf.Min(group.alpha, .15f)));
        }
        IEnumerator FadeGroup(CanvasGroup group, float from)
        {
            for (float elapsed = 0; elapsed < ResearchMotion.State; elapsed += Time.unscaledDeltaTime)
            { group.alpha = Mathf.Lerp(from, 1, ResearchMotion.Ease(elapsed / ResearchMotion.State)); yield return null; }
            group.alpha = 1;
        }
        IEnumerator CloseReferenceVisual()
        {
            if (referenceAnimation != null) { StopCoroutine(referenceAnimation); referenceAnimation = null; }
            float from = referenceGroup.alpha;
            for (float elapsed = 0; elapsed < ResearchMotion.Panel; elapsed += Time.unscaledDeltaTime)
            {
                referenceGroup.alpha = from * (1 - ResearchMotion.Ease(elapsed / ResearchMotion.Panel)); yield return null;
            }
            referencePanel.gameObject.SetActive(false);
            referenceGroup.alpha = 1; referenceClose = null;
        }

        public void ShowArrival(string station)
        {
            // A station name above an error/pause/tracking card is unrelated decoration.
            if (!Head || !arrivalGroup || !Application.isPlaying || controller && (!string.IsNullOrEmpty(controller.Error) || controller.Flow?.Paused == true || !controller.FloorReady)) return;
            if (arrivalAnimation != null) StopCoroutine(arrivalAnimation);
            var forward = Vector3.ProjectOnPlane(Head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            arrivalGroup.transform.SetPositionAndRotation(Head.position + forward * 1.4f + Vector3.up * .65f, Quaternion.LookRotation(forward, Vector3.up));
            arrivalLabel.font = Font; arrivalLabel.text = Text("arrival." + station);
            arrivalAnimation = StartCoroutine(Arrival());
        }

        IEnumerator Arrival()
        {
            const float hold = 1.4f;
            float from = arrivalGroup.alpha;
            for (float elapsed = 0; elapsed < hold; elapsed += Time.unscaledDeltaTime)
            {
                float enter = Mathf.Lerp(from, 1, ResearchMotion.Ease(elapsed / ResearchMotion.Panel));
                float exit = ResearchMotion.Ease((hold - elapsed) / ResearchMotion.Panel);
                arrivalGroup.alpha = Mathf.Min(enter, exit); yield return null;
            }
            arrivalGroup.alpha = 0;
        }

        public void AnimateChecklist()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (checklistAnimation != null) StopCoroutine(checklistAnimation); checklistAnimation = StartCoroutine(Checkmark());
        }
        IEnumerator Checkmark()
        {
            int i = Mathf.Clamp((controller?.Flow?.TutorialCompleted ?? 1) - 1, 0, 4);
            for (float elapsed = 0; elapsed < ResearchMotion.State; elapsed += Time.unscaledDeltaTime)
            { stepChecks[i].transform.localScale = Vector3.one * DotCheckScale * Mathf.Lerp(.7f, 1, ResearchMotion.Ease(elapsed / ResearchMotion.State)); yield return null; }
            stepChecks[i].transform.localScale = Vector3.one * DotCheckScale;
            checklistAnimation = null;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
        static Canvas CanvasRoot(Transform parent, string name, Vector2 size)
        {
            var rt = Rect(parent, name, Vector2.zero, size); rt.localScale = Vector3.one * .001f;
            var canvas = rt.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; rt.gameObject.AddComponent<GraphicRaycaster>(); return canvas;
        }
        static TrainingRoundedGraphic Round(Transform parent, string name, Vector2 pos, Vector2 size, Color color, float radius, bool raycast = false)
        {
            var rt = Rect(parent, name, pos, size); var graphic = rt.gameObject.AddComponent<TrainingRoundedGraphic>(); graphic.color = color; graphic.Radius = radius; graphic.raycastTarget = raycast; return graphic;
        }
        TMP_Text Label(Transform parent, string name, Vector2 pos, Vector2 size, string value, float fontSize)
        {
            var rt = Rect(parent, name, pos, size); var label = rt.gameObject.AddComponent<TextMeshProUGUI>(); label.font = Font; label.text = value; label.fontSize = fontSize; label.color = Ink; label.raycastTarget = false;
            // Generated inactive labels must use uGUI text units before Awake measures their first layout.
            label.isOrthographic = true;
            label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow; label.verticalAlignment = VerticalAlignmentOptions.Middle; label.margin = new Vector4(5, 5, 5, 5); return label;
        }
        Button Button(Transform parent, string name, Vector2 pos, Vector2 size, string label, string command, bool primary)
        {
            var graphic = Round(parent, name, pos, size, primary ? Accent : Card, 16, true);
            var button = graphic.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f); colors.pressedColor = new Color(.66f, .75f, .85f); colors.disabledColor = new Color(.5f, .5f, .5f, .7f); colors.fadeDuration = .09f; button.colors = colors;
            var text = Label(graphic.transform, "Label", Vector2.zero, size - new Vector2(24, 10), label, 30); text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(12, 5); text.rectTransform.offsetMax = new Vector2(-12, -5);
            var action = graphic.gameObject.AddComponent<ResearchUIAction>(); action.view = this; action.command = command; return button;
        }
        static GameObject StatusIcon(Transform parent, bool pass, Vector2 pos, float size)
        {
            var root = Rect(parent, pass ? "Checkmark" : "Cross", pos, new Vector2(90, 90) * size);
            Color color = pass ? Positive : new Color32(241, 91, 106, 255);
            var a = Round(root, "StrokeA", new Vector2(pass ? -17 : 0, pass ? -7 : 0) * size, new Vector2(pass ? 39 : 76, 9) * size, color, 4);
            a.transform.localRotation = Quaternion.Euler(0, 0, pass ? -44 : 45);
            var b = Round(root, "StrokeB", new Vector2(pass ? 13 : 0, pass ? 5 : 0) * size, new Vector2(pass ? 67 : 76, 9) * size, color, 4);
            b.transform.localRotation = Quaternion.Euler(0, 0, pass ? 48 : -45); return root.gameObject;
        }
    }
}
