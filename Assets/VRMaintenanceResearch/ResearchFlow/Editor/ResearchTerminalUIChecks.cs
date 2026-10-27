#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Inactive real callbacks and reopened disk output. These fixtures never acquire native input.
    public static class ResearchTerminalUIChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static string LastEvidencePath { get; private set; }

        public static string Run(string evidenceDirectory)
        {
            evidenceDirectory = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(evidenceDirectory);
            var checks = new List<object>();
            var fixtures = new List<object>();
            int failures = 0;
            void Check(bool pass, string name, object actual = null)
            { checks.Add(new { name, pass, actual }); if (!pass) failures++; }
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            void Case(string id, Action<Fixture> exercise)
            {
                Fixture fixture = null;
                try
                {
                    if (!donor) throw new InvalidOperationException("A scene ResearchFlowView with the three existing fonts is required.");
                    if (!ResearchFloorPolicy.EditorFixtureAllowed) throw new InvalidOperationException("Inactive Editor fixture requires no running native XR subsystem.");
                    fixture = new Fixture(evidenceDirectory, id, donor);
                    Check(fixture.Writer.State == HandsLabLogState.Active, id + ": isolated writer opened", fixture.Writer.State.ToString());
                    if (fixture.Writer.State != HandsLabLogState.Active) throw new InvalidOperationException("Fixture disk writer is unavailable: " + fixture.Writer.State);
                    exercise(fixture);
                }
                catch (Exception ex) { Check(false, id + ": unexpected fixture exception", ex.ToString()); }
                finally
                {
                    if (fixture != null)
                    {
                        try { fixtures.Add(fixture.Evidence()); }
                        catch (Exception ex) { Check(false, id + ": final raw reopen", ex.ToString()); }
                        try { fixture.Dispose(); }
                        catch (Exception ex) { Check(false, id + ": fixture cleanup", ex.ToString()); }
                    }
                }
            }

            void Positive(Fixture f, string name)
            {
                f.View.Refresh();
                var before = f.Events();
                bool minimized = f.View.LocalCommand("main_minimize", "None");
                Check(minimized && Get<bool>(f.View, "mainMinimized") && !Get<GameObject>(f.View, "mainContent").activeSelf
                    && Get<GameObject>(f.View, "mainPill").activeSelf, name + ": minimize accepted and changed visible slate");
                bool restored = f.View.LocalCommand("main_restore", "None");
                Check(restored && !Get<bool>(f.View, "mainMinimized") && Get<GameObject>(f.View, "mainContent").activeSelf
                    && !Get<GameObject>(f.View, "mainPill").activeSelf, name + ": restore accepted and changed visible slate");
                var added = f.Events().Skip(before.Length).ToArray();
                Check(added.Count(row => (string)row["event"] == "PANEL_MINIMIZE") == 1
                    && added.Count(row => (string)row["event"] == "PANEL_RESTORE") == 1,
                    name + ": accepted actions each exist once in reopened live events", added);
            }

            void Refuse(Fixture f, string name)
            {
                f.View.Refresh();
                foreach (string command in new[] { "main_minimize", "main_restore", "main_recenter" })
                {
                    // A restore needs an already minimized slate. Explicit fixture state avoids a trivial repeated-action rejection.
                    bool minimized = command == "main_restore";
                    Field(f.View, "mainMinimized", minimized);
                    Get<GameObject>(f.View, "mainContent").SetActive(!minimized);
                    Get<GameObject>(f.View, "mainPill").SetActive(minimized);
                    var beforeState = f.State();
                    var beforeRaw = f.Raw();
                    bool accepted = f.View.LocalCommand(command, "None");
                    bool sameState = beforeState == f.State(), sameRaw = Equal(beforeRaw, f.Raw());
                    Check(!accepted && sameState && sameRaw, name + ": " + command + " rejected before state or raw mutation",
                        new { accepted, state_unchanged = sameState, raw_bytes_unchanged = sameRaw,
                            before_state = JObject.Parse(beforeState), after_state = JObject.Parse(f.State()) });
                }
            }

            void ActivePortControl(Fixture f, string name)
            {
                f.RefreshController();
                Check(f.Port.InteractionAllowed && Get<bool>(f.Controller, "interactionAllowed"),
                    name + ": actual Controller.Refresh enables the isolated port while writer is live");
            }
            void ClosedPortCheck(Fixture f, string name)
            {
                var state = f.Controller.Flow.State; bool paused = f.Controller.Flow.Paused;
                var raw = f.Raw(); f.RefreshController();
                bool stable = Equal(raw, f.Raw());
                Check(!f.Port.InteractionAllowed && !Get<bool>(f.Controller, "interactionAllowed")
                    && f.Controller.Flow.State == state && f.Controller.Flow.Paused == paused && stable,
                    name + ": actual Controller.Refresh closes the port and interaction gate without flow/raw mutation",
                    new { f.Port.InteractionAllowed, controller_interaction_allowed = Get<bool>(f.Controller, "interactionAllowed"),
                        flow_unchanged = f.Controller.Flow.State == state && f.Controller.Flow.Paused == paused, raw_bytes_unchanged = stable });
            }

            Case("a", f => Positive(f, "Active InProgress record and live writer"));
            Case("b", f =>
            {
                Check(!f.Controller.Flow.Paused, "Abort fixture begins unpaused");
                f.Controller.ResearcherAbort(terminationReason: "technical_failure");
                f.View.Refresh();
                Check(f.Controller.Record.session_state == RegistryState.Aborted && !f.Controller.Flow.Paused
                    && f.Writer.State == HandsLabLogState.Incomplete, "Actual unpaused Abort closes record and writer", new {
                        record_state = f.Controller.Record.session_state.ToString(), paused = f.Controller.Flow.Paused, writer_state = f.Writer.State.ToString() });
                Check(!Get<GameObject>(f.View, "mainToolbar").activeSelf && !f.View.mainPanel.movable,
                    "Aborted task has no movable task toolbar");
                Check(!Get<GameObject>(f.View, "positiveIcon").activeSelf, "Abort does not display a positive completion icon");
                Refuse(f, "Actual unpaused Abort");
            });
            Case("c", f =>
            {
                ActivePortControl(f, "Completed writer control");
                Check(f.Writer.Complete("isolated_terminal_ui_fixture", "Tester"), "Completed writer fixture closes successfully");
                Check(f.Controller.Record.session_state == RegistryState.InProgress, "Completed writer retains active record control");
                ClosedPortCheck(f, "Completed writer and InProgress record");
                Refuse(f, "Completed writer and InProgress record");
            });
            Case("d", f =>
            {
                ActivePortControl(f, "Disposed writer control");
                f.Writer.Dispose();
                Check(f.Controller.Record.session_state == RegistryState.InProgress && f.Writer.State == HandsLabLogState.Incomplete,
                    "Disposed writer retains active record control");
                ClosedPortCheck(f, "Disposed writer and InProgress record");
                Refuse(f, "Disposed writer and InProgress record");
            });
            Case("e", f =>
            {
                ActivePortControl(f, "Null writer control");
                f.Writer.Dispose(); Set(f.Controller, "Logger", null);
                Check(f.Controller.Record.session_state == RegistryState.InProgress && f.Controller.Logger == null,
                    "Missing writer retains active record control");
                ClosedPortCheck(f, "Null writer and InProgress record");
                Refuse(f, "Null writer and InProgress record");
            });
            Case("f", f =>
            {
                // panel_recovery recenters only panels active in the hierarchy (ResearchFlowView.LocalCommand), so the slate must be live.
                f.ActivateView();
                Check(f.Controller.Command("pause", "None") && f.Controller.Flow.Paused, "Actual Pause accepted before safety recovery");
                f.View.Refresh();
                SafetyRecovery(f, "Paused safety recovery", false, Check);
                Set(f.Controller, "Error", "isolated_terminal_ui_error"); f.View.Refresh();
                SafetyRecovery(f, "Error safety recovery", true, Check);
            });
            Case("g", f =>
            {
                Set(f.Controller, "Record", null);
                Set(f.Controller, "Flow", new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialInteractions, 2));
                Positive(f, "Record-null Tutorial callback fixture with live writer");
                Check(f.Controller.Record == null && f.Writer.State == HandsLabLogState.Active,
                    "Tutorial compatibility creates no replacement participant record");
            });
            foreach (var tutorialState in new[] { ResearchFlowState.TutorialInteractions, ResearchFlowState.TutorialReferenceInspect })
            {
                var selected = tutorialState;
                Case(selected == ResearchFlowState.TutorialInteractions ? "h" : "i", f =>
                {
                    f.ChangeFlow(selected, selected == ResearchFlowState.TutorialInteractions ? 2 : 3);
                    Check(Get<RectTransform>(f.View, "tutorialDemo").gameObject.activeSelf
                        && Get<TMPro.TMP_Text>(f.View, "tutorialProgress").gameObject.activeSelf && !f.Controller.Flow.Paused,
                        selected + ": actual Abort begins on an unpaused visible Tutorial coach");
                    f.Controller.ResearcherAbort(terminationReason: "technical_failure"); f.View.Refresh();
                    Check(f.Controller.Record.session_state == RegistryState.Aborted && !f.Controller.Flow.Paused,
                        selected + ": actual Abort closes the Tutorial record without a fake Pause");
                    Check(!Get<RectTransform>(f.View, "checklist").gameObject.activeSelf
                        && !Get<RectTransform>(f.View, "tutorialDemo").gameObject.activeSelf
                        && !Get<RectTransform>(f.View, "tutorialSuccess").gameObject.activeSelf,
                        selected + ": ended card hides practice checklist, demo and success feedback");
                    var body = Get<TMPro.TMP_Text>(f.View, "body");
                    Check(body.gameObject.activeSelf && body.text == ResearchStrings.Get("session.ended.body", f.Controller.Language)
                        && Get<TMPro.TMP_Text>(f.View, "title").text == ResearchStrings.Get("session.ended.title", f.Controller.Language),
                        selected + ": neutral ended title and body are visible", new { body.gameObject.activeSelf, body.text });
                    Check(!Get<GameObject>(f.View, "mainToolbar").activeSelf && !f.View.mainPanel.movable
                        && !Get<GameObject>(f.View, "positiveIcon").activeSelf,
                        selected + ": ended Tutorial has no movable toolbar or positive completion icon");
                    Refuse(f, selected + " actual Abort");
                });
            }
            Case("j", f =>
            {
                var panel = f.View.mainPanel;
                panel.transform.SetPositionAndRotation(f.View.Head.position - f.View.Head.forward * 2, f.View.Head.rotation);
                var position = panel.transform.position; var rotation = panel.transform.rotation;
                typeof(ResearchPanelMotion).GetMethod("QueueRecenter", Flags).Invoke(panel, new object[] { false, false, "None" });
                int frame = Get<int>(panel, "requestedFrame") + 1;
                Check(panel.RecenterPending, "Queued recenter is pending before acquisition");
                // A sentinel on the panel root is included even in an inactive callback fixture; no native acquisition is claimed.
                var handle = panel.gameObject.AddComponent<ResearchDragHandle>(); handle.panel = panel; Field(handle, "dragging", true);
                Check(panel.IsDragging, "Explicit held-state fixture is observed by the real panel predicate");
                void Apply(bool poseReady) => typeof(ResearchPanelMotion).GetMethod("TryApplyPendingRecenter", Flags)
                    .Invoke(panel, new object[] { poseReady, true, true, frame });
                bool SamePose() => panel.transform.position == position && Quaternion.Angle(panel.transform.rotation, rotation) < .001f;
                Apply(true);
                Check(panel.RecenterPending && SamePose(), "Queued-before-held recenter preserves held pose and pending request");
                Field(handle, "dragging", false); Apply(false);
                Check(panel.RecenterPending && SamePose(), "Release does not waive required native-pose readiness");
                Apply(true);
                Check(!panel.RecenterPending && !SamePose() && !panel.PlacementBlocked && panel.IsReadableFromHead,
                    "Later ready pose after release applies the pending readable placement");
                UnityEngine.Object.DestroyImmediate(handle);
            });
            Case("k", f =>
            {
                f.ChangeFlow(ResearchFlowState.TutorialInteractions, 2);
                var itemRoot = new GameObject("IsolatedStaleSelectObject"); itemRoot.transform.SetParent(f.View.transform);
                var item = itemRoot.AddComponent<VRMaintenanceResearch.HandsLabQuest3.Runtime.HandsLabObject>();
                item.stableId = "fixture.training.stale_select";
                item.body = itemRoot.AddComponent<Rigidbody>(); item.grabbable = itemRoot.AddComponent<Oculus.Interaction.Grabbable>();
                f.Controller.trainingObjects = new[] { item };
                string[] names = { "grabActions", "grabRotations", "grabPositions", "lastHeldSnapshots", "grabSources" };
                int[] Counts() => names.Select(name => Get<System.Collections.IDictionary>(f.Controller, name).Count).ToArray();
                var before = Counts();
                Check(before.All(count => count == 0) && f.Writer.Complete("isolated_stale_select_fixture", "Tester"),
                    "Stale Select control closes a live writer with empty grab bookkeeping");
                var raw = f.Raw(); var state = f.Controller.Flow.State; bool paused = f.Controller.Flow.Paused;
                // Invoke the real callback before any Controller.Refresh can disable native acquisition paths.
                typeof(ResearchSessionFlowController).GetMethod("ObjectPointer", Flags).Invoke(f.Controller, new object[] {
                    item, new Oculus.Interaction.PointerEvent(731, Oculus.Interaction.PointerEventType.Select, Pose.identity) });
                var after = Counts(); bool stable = Equal(raw, f.Raw());
                Check(before.SequenceEqual(after) && stable && f.Controller.Flow.State == state && f.Controller.Flow.Paused == paused,
                    "Closed writer rejects a stale Select before held bookkeeping, flow or raw mutation",
                    new { dictionary_names = names, before_counts = before, after_counts = after, raw_bytes_unchanged = stable,
                        flow_unchanged = f.Controller.Flow.State == state && f.Controller.Flow.Paused == paused });
            });

            LastEvidencePath = Path.Combine(evidenceDirectory, "terminal-ui-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(LastEvidencePath, JsonConvert.SerializeObject(new {
                status = failures == 0 ? "PASS" : "FAIL", checks = checks.Count, failures, rows = checks, fixtures,
                scope = "Inactive actual Controller/View callbacks, isolated TEST registry and actual disk writers; explicit minimized and held-state fixtures; no native acquisition or haptic/wearer proof",
                hardware_verified = false, raw_read_share = "ReadWrite", safety_error_accepted_required = false,
                safety_error_policy = "Geometry may recover while an existing Error deliberately keeps accepted feedback false; never resume or alter Flow."
            }, Formatting.Indented));
            if (failures > 0) throw new InvalidOperationException("TERMINAL_UI_FAIL " + failures + "/" + checks.Count + " " + LastEvidencePath);
            return "TERMINAL_UI_PASS " + checks.Count + " " + LastEvidencePath;
        }

        // Catches duplicate global Pause, inaccessible fallback, clipped captions and a click acquired by a retired owner.
        public static string RunPauseOwnership(string evidenceDirectory)
        {
            evidenceDirectory = Path.GetFullPath(evidenceDirectory); Directory.CreateDirectory(evidenceDirectory);
            var checks = new List<object>(); var fixtures = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object actual = null)
            { checks.Add(new { name, pass, actual }); if (!pass) failures++; }
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            void Case(string id, Action<Fixture> exercise, ParticipantLanguage language = ParticipantLanguage.Thai)
            {
                Fixture f = null;
                var cursors = UnityEngine.Object.FindObjectsByType<Oculus.Interaction.RayInteractorCursorVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Select(cursor => new { cursor, color = cursor.HoverColor }).ToArray();
                try
                {
                    if (!donor || !ResearchFloorPolicy.EditorFixtureAllowed)
                        throw new InvalidOperationException("Canonical fonts and inactive native XR are required.");
                    f = new Fixture(evidenceDirectory, id, donor, language);
                    if (f.Writer.State != HandsLabLogState.Active) throw new InvalidOperationException("Isolated writer unavailable.");
                    f.ActivateView();
                    exercise(f);
                }
                catch (Exception ex) { Check(false, id + ": fixture exception", ex.ToString()); }
                finally
                {
                    if (f != null)
                    {
                        try { fixtures.Add(f.Evidence()); } catch (Exception ex) { Check(false, id + ": raw reopen", ex.ToString()); }
                        try { f.Dispose(); } catch (Exception ex) { Check(false, id + ": cleanup", ex.ToString()); }
                    }
                    foreach (var cursor in cursors) if (cursor.cursor) cursor.cursor.HoverColor = cursor.color;
                }
            }
            string Role(Fixture f, ResearchUIAction action)
            {
                var panel = action.GetComponentInParent<ResearchPanelMotion>();
                return panel == f.View.mainPanel ? "Main" : panel == f.View.referencePanel ? "Reference" : "Recovery";
            }
            bool VisibleHit(Fixture f, ResearchUIAction action)
            {
                var button = action.GetComponent<UnityEngine.UI.Button>();
                if (!action.gameObject.activeInHierarchy || !button || !button.IsActive() || !button.IsInteractable()) return false;
                for (var t = action.transform; t && t != f.View.transform; t = t.parent)
                {
                    var group = t.GetComponent<CanvasGroup>(); var canvas = t.GetComponent<Canvas>();
                    if (group && (group.alpha <= .01f || !group.interactable || !group.blocksRaycasts)) return false;
                    if (canvas && !canvas.enabled) return false;
                }
                var panel = action.GetComponentInParent<ResearchPanelMotion>();
                if (!panel || panel.PlacementBlocked || !panel.IsReadableFromHead) return false;
                // Independent mono corner/front check, in addition to the production panel predicate.
                if (panel.transform.InverseTransformPoint(f.View.Head.position).z >= -.02f) return false;
                var corners = new Vector3[4]; ((RectTransform)action.transform).GetWorldCorners(corners);
                var camera = f.View.Head.GetComponent<Camera>();
                return corners.Select(camera.WorldToViewportPoint).All(p => !float.IsNaN(p.x) && !float.IsInfinity(p.x)
                    && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && p.z > 0 && p.x >= .02f && p.x <= .98f && p.y >= .02f && p.y <= .98f);
            }
            ResearchUIAction[] Offered(Fixture f, string command = "pause") => f.View.GetComponentsInChildren<ResearchUIAction>(true)
                .Where(a => a.command == command && VisibleHit(f, a)).ToArray();
            ResearchUIAction One(Fixture f, string name, string expected)
            {
                f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases(); var actions = Offered(f);
                Check(expected == "None" ? actions.Length == 0 : actions.Length == 1 && Role(f, actions[0]) == expected,
                    name + ": one accessible global Pause owner", new { expected, actual = actions.Select(a => new { owner = Role(f, a), path = a.transform.name }).ToArray() });
                return actions.FirstOrDefault(a => Role(f, a) == expected) ?? actions.FirstOrDefault();
            }
            void Stage(Fixture f, ResearchFlowState state, int completed, bool reference, bool minimized = false)
            {
                Set(f.Controller, "ReferenceOpen", reference); Set(f.Controller, "ReferenceMinimized", minimized);
                Set(f.Controller, "ReferenceSection", state == ResearchFlowState.TutorialReferenceInspect ? "illustrated" : "");
                f.ChangeFlow(state, completed);
                var head = f.View.Head;
                foreach (var panel in new[] { f.View.mainPanel, f.View.referencePanel })
                {
                    var position = head.position + head.forward * 1.8f + head.right * (panel == f.View.mainPanel ? -.65f : .65f) + Vector3.down * .1f;
                    panel.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
                    var group = panel.GetComponent<CanvasGroup>(); group.alpha = 1;
                }
            // Ownership fixtures inject clear poses; placement failure tests exercise the authored solver separately.
            foreach (var panel in new[] { f.View.mainPanel, f.View.referencePanel })
            {
                if (!panel.PlacementClear(panel.transform.position, panel.transform.rotation))
                    throw new InvalidOperationException("Ownership fixture pose is not clear: " + panel.name);
                Set(panel, "RecenterPending", false);
                typeof(ResearchPanelMotion).GetMethod("AcceptPose", Flags).Invoke(panel, null);
                var position = panel.transform.position; var rotation = panel.transform.rotation;
                panel.EnsureClear(false);
                if (!panel.HasAcceptedPose || panel.RecenterPending || panel.PlacementBlocked || !panel.GetComponent<Canvas>().enabled
                    || !panel.IsReadableFromHead || panel.transform.position != position || Quaternion.Angle(panel.transform.rotation, rotation) > .001f)
                    throw new InvalidOperationException("Ownership fixture did not retain its accepted readable pose: " + panel.name);
            }
                f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
            }
            void Caption(Fixture f, ResearchUIAction action, string expected, string name)
            {
                if (!action) { Check(false, name + ": caption has an owner"); return; }
                var label = action.transform.Find("Label")?.GetComponent<TMPro.TMP_Text>();
                if (!label) { Check(false, name + ": existing Label is present"); return; }
                label.ForceMeshUpdate(true, true);
                var rect = (RectTransform)action.transform;
                var glyphs = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                bool glyphsFit = glyphs.Length > 0 && glyphs.All(c => new[] { c.bottomLeft, c.topLeft, c.topRight, c.bottomRight }
                    .Select(p => rect.InverseTransformPoint(label.rectTransform.TransformPoint(p)))
                    .All(p => p.x >= rect.rect.xMin - 1 && p.x <= rect.rect.xMax + 1 && p.y >= rect.rect.yMin - 1 && p.y <= rect.rect.yMax + 1));
                Check(label.gameObject.activeInHierarchy && label.text == expected && !label.isTextOverflowing
                    && label.textInfo.lineCount == 1 && glyphsFit && VisibleHit(f, action), name + ": persistent localized caption fits actual glyphs and hit surface",
                    new { expected, label.text, label.gameObject.activeInHierarchy, label.isTextOverflowing, label.textInfo.lineCount,
                        label.fontSize, glyphs_fit = glyphsFit, hit_size = new[] { rect.rect.width, rect.rect.height }, caption_rect = new[] { label.rectTransform.rect.width, label.rectTransform.rect.height } });
            }
            void RecoveryCaption(Fixture f, ResearchUIAction action, string name, bool readerFeedback = false)
            {
                var label = action.transform.Find("Label").GetComponent<TMPro.TMP_Text>();
                var rect = (RectTransform)action.transform;
                var canvasRect = (RectTransform)f.View.RecoveryCanvas.transform;
                label.ForceMeshUpdate(true, true);
                var glyphs = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                bool glyphsFit = glyphs.Length > 0 && glyphs.All(c => new[] { c.bottomLeft, c.topLeft, c.topRight, c.bottomRight }
                    .Select(p => rect.InverseTransformPoint(label.rectTransform.TransformPoint(p)))
                    .All(p => p.x >= rect.rect.xMin - 1 && p.x <= rect.rect.xMax + 1 && p.y >= rect.rect.yMin - 1 && p.y <= rect.rect.yMax + 1));
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                bool surfaceFits = corners.Select(canvasRect.InverseTransformPoint).All(p => p.x >= canvasRect.rect.xMin - 1
                    && p.x <= canvasRect.rect.xMax + 1 && p.y >= canvasRect.rect.yMin - 1 && p.y <= canvasRect.rect.yMax + 1);
                // The ordinary strip captions RecoverPanels with recover_short (ResearchFlowView.LayoutRecoveryUtility).
                string key = readerFeedback ? "action.pause_caption" : "action.recover_short";
                string expected = ResearchStrings.Get(key, f.Controller.Language);
                Check(action.command == (readerFeedback ? "pause" : "panel_recovery") && label.gameObject.activeInHierarchy && label.enabled && label.text == expected && label.font == f.View.Font && action.labelKey == key
                    && Mathf.Approximately(label.fontSize, ResearchUITheme.Caption) && !label.enableAutoSizing && !label.isTextOverflowing
                    && label.textInfo.lineCount >= 1 && label.textInfo.lineCount <= 2 && glyphsFit && surfaceFits && VisibleHit(f, action),
                    name + ": persistent localized Recovery caption fits actual glyphs, hit target and canvas surface",
                    new { expected, label.text, label.gameObject.activeInHierarchy, label.enabled, label.fontSize, label.enableAutoSizing,
                        label.isTextOverflowing, label.textInfo.lineCount, glyphs_fit = glyphsFit, surface_fits = surfaceFits,
                        controller_language = f.Controller.Language, view_font = f.View.Font.name, actual_label_font = label.font.name,
                        hit_size = new[] { rect.rect.width, rect.rect.height }, caption_rect = new[] { label.rectTransform.rect.width, label.rectTransform.rect.height } });
                var icon = action.transform.Find("UtilityIcon").GetComponent<ResearchUtilityIcon>();
                Check(icon.gameObject.activeInHierarchy && icon.rectTransform.rect.size == new Vector2(28, 28) && !icon.raycastTarget,
                    name + ": existing 28 x 28 Recovery icon remains visible without another ray target");
                var hint = action.GetComponent<ResearchControlHint>();
                var hover = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerId = 734 };
                if (hint && hint.isActiveAndEnabled) hint.OnPointerEnter(hover);
                Check(!hint || ((!hint.label || !hint.label.gameObject.activeInHierarchy) && (!hint.background || !hint.background.activeInHierarchy)),
                    name + ": hover does not duplicate the persistent Recovery caption");
                if (hint && hint.isActiveAndEnabled) hint.OnPointerExit(hover);
            }
            // The strip keeps Stop separate; Reader feedback replaces RecoverPanels with Pause.
            void StripStop(Fixture f, string name, bool readerFeedback = false)
            {
                var strip = f.View.RecoveryCanvas.transform; var stops = Offered(f, "stop");
                Check(strip.gameObject.activeInHierarchy && !strip.Find("Pause").gameObject.activeSelf && stops.Length == 1 && stops[0].transform.parent == strip,
                    name + ": strip offers one visible Stop without a duplicate Pause button", stops.Select(a => new { owner = Role(f, a), path = a.transform.name }).ToArray());
                Caption(f, strip.Find("Stop").GetComponent<ResearchUIAction>(), ResearchStrings.Get("action.stop_short", f.Controller.Language), name + " Stop");
                RecoveryCaption(f, strip.Find("RecoverPanels").GetComponent<ResearchUIAction>(), name, readerFeedback);
            }
            void InitializeInput(ResearchUIAction action)
            {
                // Ordinary MonoBehaviour OnEnable does not run for this Edit-mode fixture. Invoke the real binding once.
                if (!Get<UnityEngine.UI.Button>(action, "button"))
                    typeof(ResearchUIAction).GetMethod("OnEnable", Flags).Invoke(action, null);
            }
            void Click(ResearchUIAction action, int pointer)
            {
                if (!action) throw new InvalidOperationException("No accessible Pause owner to click.");
                InitializeInput(action);
                var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerId = pointer };
                action.OnPointerDown(data); action.OnPointerUp(data); action.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            }
            foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese, ParticipantLanguage.English })
            {
                var selected = language;
                Case("p" + (int)selected, f =>
                {
                    Check(f.Controller.Language == selected.ToString(), selected + ": actual reserved TEST session uses the requested language",
                        new { requested = selected.ToString(), actual = f.Controller.Language, f.Controller.Record.test_data });
                    string caption = selected == ParticipantLanguage.Thai ? "พัก" : selected == ParticipantLanguage.Japanese ? "休憩" : "Pause";
                    Stage(f, ResearchFlowState.Task1Active, 5, false);
                    Caption(f, One(f, selected + " Task closed", "Main"), caption, selected + " Main");
                    Stage(f, ResearchFlowState.Task1Active, 5, true);
                    Check(f.View.mainPanel.IsReadableFromHead && f.View.referencePanel.IsReadableFromHead,
                        selected + " peer fixture has two readable panels before ownership check");
                    // An expanded readable Reader owns Pause over Main (ResearchFlowView.RefreshRecoveryUtility referenceOwns).
                    One(f, selected + " Task peer reader", "Reference");
                    Stage(f, ResearchFlowState.Task1Active, 5, true, true);
                    One(f, selected + " Task minimized Reference", "Main");
                    Check(f.View.referencePanel.IsReadableFromHead && !f.View.RecoveryCanvas.gameObject.activeSelf,
                        selected + " intentional Reference minimization has a readable restore pill without an extra recovery banner");
                    Check(f.View.LocalCommand("main_minimize", "None"), selected + " both panels can be intentionally minimized");
                    One(f, selected + " both panels minimized leave Pause to the controller Menu", "None");
                    StripStop(f, selected + " both panels minimized");
                    Check(f.View.LocalCommand("main_restore", "None"), selected + " Main restores before the separate Tutorial fixture");
                    Stage(f, ResearchFlowState.TutorialInteractions, 2, false);
                    One(f, selected + " Tutorial coach", "Main");
                    Stage(f, ResearchFlowState.TutorialReferenceInspect, 3, true);
                    Check(!f.View.mainPanel.gameObject.activeInHierarchy && f.View.referencePanel.IsReadableFromHead,
                        selected + " Tutorial reader has no detached Main coach");
                    Caption(f, One(f, selected + " Tutorial expanded reader", "Reference"), caption, selected + " Reference");
                    Stage(f, ResearchFlowState.TutorialReferenceInspect, 3, true, true);
                    One(f, selected + " Tutorial minimized reader", "None");
                    StripStop(f, selected + " Tutorial minimized reader");
                    Stage(f, ResearchFlowState.Task1Active, 5, false);
                    Check(f.View.LocalCommand("main_minimize", "None"), selected + " Main actually minimizes before fallback");
                    One(f, selected + " Main minimized", "None");
                    StripStop(f, selected + " Main minimized");
                    Stage(f, ResearchFlowState.TutorialReferenceInspect, 3, true);
                    var before = f.Events(); var state = f.Controller.Flow.State; int completed = f.Controller.Flow.TutorialCompleted;
                    Click(One(f, selected + " before actual acquired click", "Reference"), 731);
                    var added = f.Events().Skip(before.Length).ToArray();
                    Check(f.Controller.Flow.Paused && f.Controller.Flow.State == state && f.Controller.Flow.TutorialCompleted == completed
                        && added.Count(row => (string)row["event"] == "SESSION_PAUSED") == 1,
                        selected + " actual down/up/click pauses once without task or Tutorial advancement", added);
                    One(f, selected + " paused", "None");
                }, selected);
                Case("rc" + (int)selected, f =>
                {
                    Stage(f, ResearchFlowState.Task1Active, 5, true, true);
                    f.View.referencePanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                    One(f, selected + " unreadable Reference pill needs recovery while Main owns Pause", "Main");
                    var canvas = f.View.RecoveryCanvas;
                    var canvasRect = (RectTransform)canvas.transform;
                    var restore = canvas.transform.Find("RecoverPanels").GetComponent<ResearchUIAction>();
                    var pause = canvas.transform.Find("Pause").GetComponent<ResearchUIAction>();
                    var stopRect = (RectTransform)canvas.transform.Find("Stop");
                    var restoreRect = (RectTransform)restore.transform;
                    int children = canvas.transform.childCount; var before = f.Events(); var state = f.Controller.Flow.State;
                    var mainRotation = f.View.mainPanel.transform.rotation;
                    // The strip opens only for a fallback Stop or a participant-pause recall, not for a panel out of view
                    // (ResearchFlowView.RefreshRecoveryUtility `needed`; design_rule 4: looking away is no reason to open it).
                    Check(!canvas.gameObject.activeInHierarchy, selected + ": Main owning Pause keeps the strip hidden for an unreadable Reader pill");
                    f.View.mainPanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                    One(f, selected + " unreadable Main and minimized Reader leave Pause to the controller Menu", "None");
                    StripStop(f, selected + " lost Main");
                    float gap = stopRect.anchoredPosition.x - stopRect.rect.width / 2 - (restoreRect.anchoredPosition.x + restoreRect.rect.width / 2);
                    // Compact strip: 300 x 80 RecoverPanels plus one 96 px safety slot (ResearchFlowView.LayoutRecoveryUtility).
                    Check(canvas.gameObject.activeInHierarchy && !pause.gameObject.activeSelf && canvasRect.rect.size == new Vector2(412, 96)
                        && restoreRect.rect.size == new Vector2(300, 80) && restoreRect.anchoredPosition == new Vector2(-48, 0)
                        && stopRect.rect.size == new Vector2(80, 80) && stopRect.anchoredPosition == new Vector2(158, 0) && gap >= 16,
                        selected + ": fallback strip includes both actual hit targets with the intended gap", new { gap, canvas_size = new[] { canvasRect.rect.width, canvasRect.rect.height } });
                    f.View.mainPanel.transform.rotation = mainRotation;
                    One(f, selected + " recovered Main takes Pause ownership back", "Main");
                    // Owner 20 Oct: the Task card shows Pause only; Stop waits on the pause card.
                    Check(!canvas.gameObject.activeInHierarchy && Offered(f, "stop").Length == 0,
                        selected + ": strip hides again when Main owns Pause, and no surface shows Stop during the Task");
                    f.View.RefreshRecoveryUtility();
                    Check(f.View.RecoveryCanvas == canvas && canvas.transform.Find("RecoverPanels").GetComponent<ResearchUIAction>() == restore
                        && canvas.transform.Find("Pause").GetComponent<ResearchUIAction>() == pause && canvas.transform.childCount == children,
                        selected + ": strip transitions and repeated refresh reuse the same canvas, actions and hierarchy");
                    // Strip placement writes System PANEL_PLACEMENT telemetry (ResearchPanelMotion.ApplyRecenter); nothing participant-attributed.
                    var added = f.Events().Skip(before.Length).ToArray();
                    Check(f.Controller.Flow.State == state && !f.Controller.Flow.Paused && added.All(row => (string)row["source"] == "System"),
                        selected + ": caption, hover and strip layout changes emit no participant event and do not alter task state", added);
                }, selected);
                // Owner 20 Oct: one Pause on the Task card; the pause card adds "Stop and wait for the researcher" beside a permitted
                // Continue, a Study pause offers no Continue, and the off-view strip still carries Stop.
                Case("ps" + (int)selected, f =>
                {
                    Stage(f, ResearchFlowState.Task1Active, 5, false);
                    Check(Offered(f, "stop").Length == 0, selected + ": active Task card offers Pause without Stop");
                    Click(One(f, selected + " Task card before pause", "Main"), 735); f.View.Refresh(); f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                    var stops = Offered(f, "stop");
                    Check(f.Controller.Flow.Paused && f.Controller.ParticipantMayResume && Offered(f, "session_resume").Length == 1
                        && stops.Length == 1 && Role(f, stops[0]) == "Main" && !f.View.RecoveryCanvas.gameObject.activeSelf,
                        selected + ": DEV pause card offers Continue and one Stop-and-wait", f.View.GetComponentsInChildren<ResearchUIAction>(false).Select(a => a.command).ToArray());
                    One(f, selected + " pause card", "None");
                    if (stops.Length == 1)
                    {
                        var label = stops[0].transform.Find("Label").GetComponent<TMPro.TMP_Text>(); label.ForceMeshUpdate(true, true);
                        var rect = (RectTransform)stops[0].transform;
                        var glyphs = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                        bool fits = glyphs.Length > 0 && glyphs.All(c => new[] { c.bottomLeft, c.topRight }.Select(p => rect.InverseTransformPoint(label.rectTransform.TransformPoint(p)))
                            .All(p => rect.rect.Contains(new Vector2(p.x, p.y))));
                        Check(label.text == ResearchStrings.Get("action.stop_wait", f.Controller.Language) && !label.isTextOverflowing && label.textInfo.lineCount <= 2 && fits,
                            selected + ": Stop-and-wait label is the localized choice and its glyphs fit the button", new { label.text, label.textInfo.lineCount, label.fontSize, fits });
                    }
                    // Study: the researcher resumes, so the same card keeps Stop-and-wait, drops Continue and says who is awaited.
                    f.Controller.Record.test_data = false; f.View.Refresh(); f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                    Check(!f.Controller.ParticipantMayResume && Offered(f, "session_resume").Length == 0 && Offered(f, "stop").Length == 1
                        && Get<TMPro.TMP_Text>(f.View, "waitingLabel").gameObject.activeInHierarchy,
                        selected + ": Study pause card offers Stop-and-wait, no Continue, and names the researcher", f.View.GetComponentsInChildren<ResearchUIAction>(false).Select(a => a.command).ToArray());
                    var rotation = f.View.mainPanel.transform.rotation;
                    f.View.mainPanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                    // Edit mode does not advance unscaled time: backdate the .75 s glance dwell and the .08 s strip transition once.
                    f.View.RefreshRecoveryUtility();
                    Field(f.View, "fallbackSince", Time.unscaledTime - 1f); Field(f.View, "recoveryTransitionSince", Time.unscaledTime - 1f);
                    f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                    StripStop(f, selected + " Study pause card out of view");
                    f.View.mainPanel.transform.rotation = rotation; f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                    var before = f.Events();
                    Click(Offered(f, "stop").FirstOrDefault(), 736); f.View.Refresh(); f.View.RefreshRecoveryUtility();
                    var added = f.Events().Skip(before.Length).ToArray();
                    Check(f.Controller.Flow.Paused && f.Controller.PauseKind == "stop" && f.Controller.Record.session_state == RegistryState.InProgress
                        && added.Count(row => (string)row["event"] == "SESSION_PAUSED" && (string)row["reason"] == "participant_stop") == 1
                        && Offered(f, "stop").Length == 0 && Offered(f, "session_resume").Length == 0,
                        selected + ": Stop-and-wait applies the existing stop once and leaves no participant Continue", added);
                }, selected);
                foreach (string task in new[] { "A", "B" })
                Case("td" + (int)selected + task, f =>
                {
                    f.Port.taskId = task;
                    var state = task == "A" ? ResearchFlowState.Task1Active : ResearchFlowState.Task2Active;
                    Stage(f, state, 5, false);
                    string[] Rows() => f.View.mainPanel.GetComponentsInChildren<TMPro.TMP_Text>(false).Where(t => t.transform.parent.name.StartsWith("Line_", StringComparison.Ordinal))
                        .Select(t => { t.ForceMeshUpdate(true, true); return t.GetParsedText(); }).ToArray();
                    string[] Lines(string suffix) => ResearchStrings.Get("task." + task + "." + suffix, f.Controller.Language).Split('\n');
                    Check(Rows().Length == 2 && Rows().SequenceEqual(Lines("summary")), selected + "/" + task + ": compact card shows only goal and safety", Rows());
                    var before = f.Events();
                    var record = f.Controller.Record;
                    string identity = record.participant_id + "/" + record.session_id + "/" + record.run_id;
                    long revision = record.revision;
                    var timing = Get<ResearchTaskTiming>(f.Controller, "taskTiming");
                    double started = timing.StartedAt, active = timing.ActiveElapsed, wall = timing.WallElapsed;
                    Click(Offered(f, "task_details").FirstOrDefault(), 737);
                    Check(Rows().Length == 4 && Rows().SequenceEqual(Lines("body")), selected + "/" + task + ": Details shows all four authored task paragraphs", Rows());
                    Click(Offered(f, "task_details").FirstOrDefault(), 738);
                    Check(Rows().Length == 2 && Rows().SequenceEqual(Lines("summary")), selected + "/" + task + ": the participant collapses Details to goal and safety", Rows());
                    var added = f.Events().Skip(before.Length).ToArray();
                    Check(added.All(row => ((string)row["event"] == "PANEL_PLACEMENT" || (string)row["event"] == "PANEL_PLACEMENT_BLOCKED")
                            && (string)row["source"] == "System")
                        && !f.Controller.ReferenceOpen && !f.Controller.Flow.Paused && !f.Controller.Flow.InspectPassed.HasValue && f.Controller.Flow.State == state,
                        selected + "/" + task + ": Details adds only actual System placement telemetry, no information/Inspect/transition", added);
                    var after = f.Controller.Record;
                    Check(identity == after.participant_id + "/" + after.session_id + "/" + after.run_id && after.revision == revision
                        && timing.StartedAt == started && timing.ActiveElapsed == active && timing.WallElapsed == wall,
                        selected + "/" + task + ": Details keeps identity, persisted revision and the same task clock", new { identity, after.revision, timing.StartedAt, timing.ActiveElapsed, timing.WallElapsed });
                }, selected);
            }
            Case("ph", f =>
            {
                Stage(f, ResearchFlowState.Task1Active, 5, true);
                // The expanded Reader is the initial owner (referenceOwns); turning it away hands Pause back to Main.
                var retired = Offered(f).FirstOrDefault(a => Role(f, a) == "Reference");
                if (!retired) throw new InvalidOperationException("Stale-owner fixture has no initial Reference Pause.");
                InitializeInput(retired);
                var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerId = 733 };
                retired.OnPointerDown(data); var before = f.Events();
                f.View.referencePanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                One(f, "Reader feedback handoff after acquired Reference press", "Recovery");
                retired.OnPointerUp(data); retired.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                // A stale release is refused with System UI_CLICK_REJECTED telemetry (ResearchUIAction.OnPointerUp), never a Pause.
                var added = f.Events().Skip(before.Length).ToArray();
                Check(!f.Controller.Flow.Paused && added.All(row => (string)row["source"] == "System"),
                    "Retired owner cannot release/click an acquired press after geometry-only handoff", added);
            });
            Case("pr", f =>
            {
                Stage(f, ResearchFlowState.Task1Active, 5, true);
                var panel = f.View.mainPanel; var handle = panel.gameObject.AddComponent<ResearchDragHandle>(); handle.panel = panel; Field(handle, "dragging", true);
                panel.transform.rotation *= Quaternion.Euler(0, 180, 0); f.View.referencePanel.transform.rotation *= Quaternion.Euler(0, 180, 0);
                var position = panel.transform.position; var rotation = panel.transform.rotation;
                var strip = f.View.RecoveryCanvas.GetComponent<ResearchPanelMotion>();
                var stripPosition = strip.transform.position; var stripRotation = strip.transform.rotation;
                var stripSize = ((RectTransform)strip.transform).sizeDelta;
                var readerPosition = f.View.referencePanel.transform.position; var readerRotation = f.View.referencePanel.transform.rotation;
                One(f, "Held rear Main and hidden Reference", "None");
                // A held panel defers every placement, the strip included, so nothing moves under the ray (ResearchFlowView.PanelPlacementBusy).
                Check(f.View.PanelPlacementBusy && !strip.gameObject.activeSelf && !strip.RecenterPending,
                    "Held rear Main and hidden Reference: fallback layout waits for release", new { pending = strip.RecenterPending, active = strip.gameObject.activeSelf });
                Check(panel.IsDragging && panel.transform.position == position && Quaternion.Angle(panel.transform.rotation, rotation) < .001f,
                    "Fallback availability leaves the injected held pose unchanged");
                Check(strip.transform.position == stripPosition && Quaternion.Angle(strip.transform.rotation, stripRotation) < .001f
                    && ((RectTransform)strip.transform).sizeDelta == stripSize && f.View.referencePanel.transform.position == readerPosition
                    && Quaternion.Angle(f.View.referencePanel.transform.rotation, readerRotation) < .001f,
                    "Held feedback preserves Reader and recovery geometry");
                Field(handle, "dragging", false); UnityEngine.Object.DestroyImmediate(handle);
                // Edit mode never advances Time.frameCount; apply the deferred placement as the next frame would.
                typeof(ResearchPanelMotion).GetMethod("TryApplyPendingRecenter", Flags).Invoke(strip, new object[] { true, true, true, Time.frameCount + 1 });
                f.View.RefreshRecoveryUtility(); Canvas.ForceUpdateCanvases();
                StripStop(f, "Released rear Main and hidden Reference", true);
                One(f, "Released Reader feedback owns Pause", "Recovery");
                // The controller Menu remains an independent Pause route; it calls Command("pause", "Controller").
                Check(f.Controller.Command("pause", "Controller"), "Controller Menu pause remains accepted alongside Reader feedback");
                f.View.Refresh(); One(f, "Paused safety card", "None");
                SafetyRecovery(f, "Paused geometry remains available", false, Check);
                Set(f.Controller, "Error", "pause_ownership_fixture_error"); f.View.Refresh(); One(f, "Error safety card", "None");
                SafetyRecovery(f, "Error geometry remains available", true, Check);
                Set(f.Controller, "Error", ""); f.Controller.ResearcherAbort(terminationReason: "technical_failure"); f.View.Refresh();
                One(f, "Actually aborted record", "None");
                var raw = f.Raw(); Check(!f.Controller.Command("pause", "None") && Equal(raw, f.Raw()), "Terminal Pause rejects before raw mutation");
            });
            Case("pm", f =>
            {
                Stage(f, ResearchFlowState.TutorialReferenceInspect, 3, true, true);
                var canvas = f.View.RecoveryCanvas;
                var restore = canvas.transform.Find("RecoverPanels").GetComponent<ResearchUIAction>();
                var pause = canvas.transform.Find("Pause").GetComponent<ResearchUIAction>();
                var restoreRect = (RectTransform)restore.transform; var pauseRect = (RectTransform)pause.transform;
                var label = pause.transform.Find("Label").GetComponent<TMPro.TMP_Text>();
                var icon = pause.transform.Find("UtilityIcon").GetComponent<ResearchUtilityIcon>();
                // Reuse the same already-created hierarchy, matching the serialized legacy Recovery controls.
                restoreRect.sizeDelta = pauseRect.sizeDelta = new Vector2(64, 64);
                restoreRect.anchoredPosition = new Vector2(-44, 0); pauseRect.anchoredPosition = new Vector2(44, 0);
                label.gameObject.SetActive(false); pause.labelKey = ""; icon.rectTransform.anchoredPosition = Vector2.zero;
                Check(pauseRect.rect.width == 64 && !label.gameObject.activeSelf && pause.labelKey == ""
                    && icon.rectTransform.anchoredPosition == Vector2.zero, "Reused Recovery fixture actually starts with legacy target and hidden caption");
                var raw = f.Raw(); var state = f.Controller.Flow.State; int children = canvas.transform.childCount;
                var prepare = typeof(ResearchFlowView).GetMethod("PrepareRecoveryUtility", Flags);
                prepare.Invoke(f.View, null);
                One(f, "Reused legacy Recovery after real preparation", "None");
                StripStop(f, "Reused Recovery Thai");
                var stopRect = (RectTransform)canvas.transform.Find("Stop");
                Check(restoreRect.rect.size == new Vector2(300, 80), "Reused Recovery restores the actual labeled compact RecoverPanels 300 x 80 hit target", new[] { restoreRect.rect.width, restoreRect.rect.height });
                Check(pauseRect.rect.size == new Vector2(80, 80), "Reused Recovery restores the actual Pause 80 x 80 hit target", new[] { pauseRect.rect.width, pauseRect.rect.height });
                float gap = stopRect.anchoredPosition.x - stopRect.rect.width / 2 - (restoreRect.anchoredPosition.x + restoreRect.rect.width / 2);
                Check(restoreRect.anchoredPosition == new Vector2(-48, 0) && stopRect.anchoredPosition == new Vector2(158, 0) && gap >= 16,
                    "Reused Recovery has the intended spacing without overlapping hit targets", new { restore = new[] { restoreRect.anchoredPosition.x, restoreRect.anchoredPosition.y }, stop = new[] { stopRect.anchoredPosition.x, stopRect.anchoredPosition.y }, gap });
                Check(pause.labelKey == "action.pause_caption", "Reused Recovery persists the localized Pause label key", pause.labelKey);
                Check(icon.rectTransform.anchoredPosition == new Vector2(0, 17) && !icon.raycastTarget,
                    "Reused Recovery stacks its existing icon above the caption without a new raycast target", new[] { icon.rectTransform.anchoredPosition.x, icon.rectTransform.anchoredPosition.y });
                prepare.Invoke(f.View, null);
                Check(f.View.RecoveryCanvas == canvas && canvas.transform.Find("Pause").GetComponent<ResearchUIAction>() == pause
                    && canvas.transform.Find("RecoverPanels").GetComponent<ResearchUIAction>() == restore && canvas.transform.childCount == children,
                    "Repeated Recovery preparation preserves existing canvas, controls and hierarchy");
                Check(f.Controller.Flow.State == state && !f.Controller.Flow.Paused && Equal(raw, f.Raw()),
                    "Recovery visual migration does not emit participant actions or advance the flow");
            });
            LastEvidencePath = Path.Combine(evidenceDirectory, "pause-ownership-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(LastEvidencePath, JsonConvert.SerializeObject(new { status = failures == 0 ? "PASS" : "FAIL", checks = checks.Count, failures, rows = checks, fixtures,
                hardware_verified = false, scope = "Active real uGUI callbacks with inactive Controller, isolated TEST writers and synthetic mono camera; explicit held state; no native input, haptic or wearer proof",
                raw_read_share = "ReadWrite", manual_lifecycle_refresh = true, cursor_hover_colors_restored = true }, Formatting.Indented));
            if (failures > 0) throw new InvalidOperationException("PAUSE_OWNERSHIP_FAIL " + failures + "/" + checks.Count + " " + LastEvidencePath);
            return LastEvidencePath;
        }

        static void SafetyRecovery(Fixture f, string name, bool error, Action<bool, string, object> check)
        {
            var state = f.Controller.Flow.State; bool paused = f.Controller.Flow.Paused; string existingError = f.Controller.Error;
            var panel = f.View.mainPanel;
            panel.transform.SetPositionAndRotation(f.View.Head.position - f.View.Head.forward * 2, f.View.Head.rotation);
            var hiddenPosition = panel.transform.position;
            bool accepted = f.View.LocalCommand("panel_recovery", "None");
            bool recovered = Vector3.Distance(hiddenPosition, panel.transform.position) > .1f && panel.IsReadableFromHead && !panel.PlacementBlocked;
            check(recovered && (error || accepted), name + ": hidden slate geometry remains recoverable",
                new { accepted, recovered, position = Vector(panel.transform.position), panel.PlacementBlocked, panel.IsReadableFromHead });
            check(f.Controller.Flow.State == state && f.Controller.Flow.Paused == paused && f.Controller.Error == existingError,
                name + ": no resume, flow transition or Error clearing", new { state = f.Controller.Flow.State.ToString(), f.Controller.Flow.Paused, f.Controller.Error });
        }

        sealed class Fixture : IDisposable
        {
            readonly GameObject root;
            readonly string id;
            public readonly ResearchSessionFlowController Controller;
            public readonly ResearchFlowView View;
            public readonly ResearchTaskPort Port;
            public readonly HandsLabLogger Writer;
            public Fixture(string directory, string caseId, ResearchFlowView donor, ParticipantLanguage language = ParticipantLanguage.Thai)
            {
                id = caseId;
                root = new GameObject("TerminalUI_Isolated_" + caseId) { hideFlags = HideFlags.HideAndDontSave }; root.SetActive(false);
                try
                {
                    string registryRoot = Path.Combine(directory, "g", caseId);
                    var registry = new ResearchParticipantRegistry(registryRoot);
                    var record = registry.CreateNext(ParticipantGroup.Test);
                    record = registry.StartOrResume(record.participant_id, language, ExperimentInputMode.Controller, TaskOrder.AB);
                    var flow = new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.Task1Active, 5);
                    record = registry.SaveProgress(record, flow);
                    Controller = root.AddComponent<ResearchSessionFlowController>(); Controller.registryRoot = registryRoot;
                    Set(Controller, "Registry", registry); Set(Controller, "Record", record); Set(Controller, "Flow", flow);
                    Port = root.AddComponent<ResearchTaskPort>(); Port.taskId = "A"; Port.demoMode = true; Port.contentApproved = false;
                    Port.Bind(() => new { fixture = "isolated_terminal_ui", repair_completed = false }, () => false, allowed => { });
                    Controller.taskPorts = new[] { Port };
                    View = ResearchFlowView.Build(root.transform, donor.thaiFont, donor.japaneseFont, donor.englishFont);
                    Controller.view = View; View.controller = Controller;
                    var head = new GameObject("IsolatedEditorHead"); head.transform.SetParent(root.transform);
                    head.transform.SetPositionAndRotation(new Vector3(40, 1.65f, 40), Quaternion.Euler(0, 180, 0)); View.Head = head.transform;
                    foreach (var panel in new[] { View.mainPanel, View.referencePanel }) Field(panel, "environment", Array.Empty<Renderer>());
                    // Keep names short: Windows long-path failures must not masquerade as UI regressions.
                    Writer = new HandsLabLogger(Path.Combine(directory, "l"), "None", "IsolatedTerminalUIFixture_" + caseId,
                        technicalTest: true, testData: true, phase: "ResearchSession", taskId: "session", runId: record.run_id);
                    Set(Controller, "Logger", Writer);
                    // S1 offers task controls only while the task clock runs (ResearchSessionFlowController.LocalUICommandsAllowed).
                    Get<ResearchTaskTiming>(Controller, "taskTiming").Start(Writer.Timestamp);
                    View.Refresh();
                }
                catch { Dispose(); throw; }
            }
            // Activates only the uGUI tree. Controller remains inactive; manual refresh is explicit, with no frame simulation.
            public void ActivateView()
            {
                View.gameObject.SetActive(false); View.enabled = false;
                View.transform.SetParent(null, false); View.Head.SetParent(View.transform, true);
                var camera = View.Head.gameObject.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 16f / 9;
                View.gameObject.SetActive(true);
                Field(View.RecoveryCanvas.GetComponent<ResearchPanelMotion>(), "environment", Array.Empty<Renderer>());
            }
            public JObject[] Events() => ReadText(Writer.EventsPath).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
            public void RefreshController() => typeof(ResearchSessionFlowController).GetMethod("Refresh", Flags).Invoke(Controller, null);
            public void ChangeFlow(ResearchFlowState state, int tutorialCompleted)
            {
                var flow = new ResearchSessionFlow(TaskOrder.AB, state, tutorialCompleted);
                Set(Controller, "Flow", flow); Set(Controller, "Record", Controller.Registry.SaveProgress(Controller.Record, flow)); View.Refresh();
            }
            public string[] Paths => new[] { Writer.EventsPath, Writer.MovementPath, Writer.MetadataPath };
            public byte[][] Raw() => Paths.Select(Read).ToArray();
            public string State() => JsonConvert.SerializeObject(new {
                minimized = Get<bool>(View, "mainMinimized"), content = Get<GameObject>(View, "mainContent").activeSelf,
                pill = Get<GameObject>(View, "mainPill").activeSelf, toolbar = Get<GameObject>(View, "mainToolbar").activeSelf,
                positive = Get<GameObject>(View, "positiveIcon").activeSelf, View.mainPanel.movable,
                panel_position = Vector(View.mainPanel.transform.position), panel_rotation = Rotation(View.mainPanel.transform.rotation),
                panel_size = new[] { View.mainPanel.GetComponent<RectTransform>().sizeDelta.x, View.mainPanel.GetComponent<RectTransform>().sizeDelta.y },
                flow = Controller.Flow.State.ToString(), Controller.Flow.Paused, record_state = Controller.Record?.session_state.ToString(),
                logger_state = Controller.Logger?.State.ToString(), Controller.Error, Controller.ReferenceOpen, Controller.ReferenceMinimized
            });
            public object Evidence() => new {
                case_id = id, participant_id = Controller.Record?.participant_id, session_id = Controller.Record?.session_id,
                run_id = Writer.RunId, test_data = true, language = Controller.Record?.language.ToString(), state_before_fixture_cleanup = JObject.Parse(State()),
                raw_before_fixture_cleanup = Paths.Select(path => { var bytes = Read(path); return new { path, bytes = bytes.Length, sha256 = Hash(bytes) }; }).ToArray(),
                event_count = Events().Length, export_path = Controller.LastExportPath
            };
            public void Dispose()
            {
                // A detached (activated) view is owned by this fixture; never leave a running duplicate UI after a failure.
                if (View) UnityEngine.Object.DestroyImmediate(View.gameObject);
                Writer?.Dispose();
                if (Controller) Set(Controller, "Logger", null);
                if (root) UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Flags).GetValue(target);
        static void Field(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
        static void Set(object target, string property, object value) => target.GetType().GetProperty(property, Flags).GetSetMethod(true).Invoke(target, new[] { value });
        static float[] Vector(Vector3 v) => new[] { v.x, v.y, v.z };
        static float[] Rotation(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        static bool Equal(byte[][] a, byte[][] b) => a.Length == b.Length && a.Select((bytes, i) => bytes.SequenceEqual(b[i])).All(same => same);
        static byte[] Read(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var bytes = new MemoryStream()) { file.CopyTo(bytes); return bytes.ToArray(); }
        }
        static string ReadText(string path) => System.Text.Encoding.UTF8.GetString(Read(path)).TrimStart('\uFEFF');
        static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
