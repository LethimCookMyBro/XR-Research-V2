using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Researcher control channel for the STANDALONE technical build.
    ///
    /// Why this exists: on a standalone headset there is exactly one display, so
    /// ResearcherSetupView is disabled by design and the researcher has no way to drive
    /// a session. Quest Link, the intended researcher path, is unusable on this machine
    /// (ovrError_XRStreamingPluginIssue -6101). See ResearchFlowQuestLinkErrata.md.
    ///
    /// Design constraints honoured:
    ///  - Participant headset stays standalone; no researcher UI reaches the participant display.
    ///  - No alternate session state machine. This NEVER touches ResearchSessionFlow directly;
    ///    it only calls the existing StartSession / ResearcherResume / ResearcherAbort /
    ///    SetDemoPass entry points and the existing ResearchParticipantRegistry.
    ///  - DEV / TEST / PILOT only, on EVERY state-mutating command, not just "start".
    ///    "start" resolves the id by scanning ONLY the technical groups, so a real
    ///    Thai/Japanese study record is structurally unreachable. "resume", "abort", "demo"
    ///    and "export" additionally require the CURRENT session to be technical test data.
    ///  - No server or networking. A restricted app-owned ContentProvider receives a
    ///    profile/lifetime-bound request into the existing command JSON mailbox.
    ///  - This type IS compiled into every player. It is not excluded at compile time.
    ///    The runtime gate is Install(), which refuses to instantiate the channel unless
    ///    Application.identifier equals the exact technical identity, so in a study build no instance
    ///    exists and no command file is ever polled.
    ///
    /// Transport uses `content call --user N` on the exact technical package provider.
    /// Its manifest declaration exists only during the technical build transaction.
    /// No arbitrary file path, command, URI grant, network listener or shell is exposed.
    /// </summary>
    public sealed class ResearchAdbControlChannel : MonoBehaviour
    {
        const string CommandFile = "researcher-command.json";
        const string ResponseFile = "researcher-response.json";
        const float PollSeconds = .5f;
        const string TechnicalIdentity = "th.ac.tmu.vr.researchflow.quest3.technical";

        static readonly ParticipantGroup[] TechnicalGroups =
            { ParticipantGroup.Dev, ParticipantGroup.Test, ParticipantGroup.Pilot };
        // time_limit is system-owned (420 s); the researcher picks among the rest.
        static readonly string[] AbortReasons = ResearchParticipantRegistry.ResearcherTerminationReasons;

        ResearchSessionFlowController controller;
        string commandPath, responsePath;
        float nextPoll;
        string requestId;
        JObject requestBinding;
        readonly HashSet<string> handledRequests = new HashSet<string>(StringComparer.Ordinal);
        readonly Queue<string> requestOrder = new Queue<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
        const string ProfileProvider = "th.ac.tmu.vr.researchflow.transport.QuestProfileProvider";
        bool appPaused, appFocused;
        void PublishProfile()
        {
            using (var provider = new AndroidJavaClass(ProfileProvider))
                provider.CallStatic("updateState", controller?.Record?.session_id ?? string.Empty, controller?.Record?.run_id ?? string.Empty,
                    appFocused && !appPaused && Application.isFocused);
        }
        void OnApplicationFocus(bool focused) { appFocused = focused; PublishProfile(); }
        void OnApplicationPause(bool paused) { appPaused = paused; PublishProfile(); }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Hard gate: the channel only exists in the technical build identity.
            if (!string.Equals(Application.identifier, TechnicalIdentity, StringComparison.Ordinal)) return;
            if (FindFirstObjectByType<ResearchSessionFlowController>() == null) return;
            var host = new GameObject("ResearchAdbControlChannel") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<ResearchAdbControlChannel>();
        }

        void Awake()
        {
            controller = FindFirstObjectByType<ResearchSessionFlowController>();
            commandPath = Path.Combine(Application.persistentDataPath, CommandFile);
            responsePath = Path.Combine(Application.persistentDataPath, ResponseFile);
#if UNITY_ANDROID && !UNITY_EDITOR
            appFocused = Application.isFocused;
            PublishProfile();
#endif
            Debug.Log("RESEARCH_ADB_CHANNEL_READY " + commandPath);
        }

        void Update()
        {
            if (controller == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollSeconds;
#if UNITY_ANDROID && !UNITY_EDITOR
            PublishProfile();
#endif
            if (!File.Exists(commandPath)) return;

            string raw;
            try { raw = File.ReadAllText(commandPath); }
            catch (IOException) { return; }          // still being written; retry next tick
            try { File.Delete(commandPath); }        // consume exactly once, never replay
            catch (IOException) { return; } // Never execute a command that cannot be consumed exactly once.

            string command = "(unparsed)";
            requestId = null;
            requestBinding = null;
            try
            {
                var request = JObject.Parse(raw);
                requestId = (string)request["request_id"];
                requestBinding = request["binding"] as JObject;
                command = (string)request["command"] ?? "";
#if UNITY_ANDROID && !UNITY_EDITOR
                if (controller == null || appPaused || !appFocused || !Application.isFocused)
                { Respond(command, Fail("QUEST_PROFILE_GUARD: current application is paused/unfocused")); return; }
                string refusal;
                using (var provider = new AndroidJavaClass(ProfileProvider))
                    refusal = provider.CallStatic<string>("validateJson", raw);
                if (refusal != null) { Respond(command, Fail("QUEST_PROFILE_GUARD: " + refusal)); return; }
                if (!handledRequests.Add(requestId)) { Respond(command, Fail("QUEST_PROFILE_GUARD: duplicate request UUID")); return; }
                requestOrder.Enqueue(requestId);
                if (requestOrder.Count > 128) handledRequests.Remove(requestOrder.Dequeue());
#endif
                Respond(command, Execute(command, request));
            }
            catch (Exception ex)
            {
                Respond(command, Fail("Unhandled: " + ex.Message));
            }
        }

        object Execute(string command, JObject request)
        {
            switch (command)
            {
                case "state": return Ok("state");
                case "list": return List(request);
                case "create": return Create(request);
                case "start": return StartSessionCommand(request);
                case "resume": return Resume();
                case "abort": return Abort(request);
                case "amend_reason": return AmendReason(request);
                case "demo": return Demo(request);
                case "export": return Export();
                case "export_saved": return ExportSaved(request);
                case "technical_scenario":
                    var scenario = controller.GetComponent<ResearchTechnicalScenario>() ?? controller.gameObject.AddComponent<ResearchTechnicalScenario>();
                    return scenario.Begin(controller, (bool?)request["simulated_presence"] == true, (string)request["order"] != "BA", out string scenarioError, (string)request["render_mode"] ?? "msaa")
                        ? Ok(command, scenario.State) : Fail(scenarioError);
                case "benchmark": return Benchmark(request);
                case "benchmark_cancel": return CancelBenchmark();
                case "motion_trace": return MotionTrace(request);
                case "view_probe": case "frame_timing": return ViewDiagnostic(command, request);
                case "haptic_probe": return HapticProbe();
                // Blind drag A/B for the wear session; never changes the ray/controller pose, only the panel target filter.
                case "drag_filter":
                    if (!TechnicalSession(command, out string filterRefusal)) return Fail(filterRefusal);
                    ResearchDragHandle.FilterEnabled = (bool?)request["enabled"] ?? true; return Ok("drag_filter");
                default: return Fail("Unknown command: '" + command + "'");
            }
        }

        // ---- commands -------------------------------------------------------

        object List(JObject request)
        {
            if (!TechnicalGroup(request, out var group, out string error)) return Fail(error);
            var records = controller.Registry.List(group)
                .Select(r => new { r.participant_id, r.session_id, r.run_id, group = r.group.ToString(), state = r.session_state.ToString(), r.test_data })
                .ToArray();
            return Ok("list", new { group = group.ToString(), participants = records });
        }

        object Create(JObject request)
        {
            if (!TechnicalGroup(request, out var group, out string error)) return Fail(error);
            var record = controller.Registry.CreateNext(group);
            LogCommand("create", record.participant_id, null, null, "Created");
            return Ok("create", new { created = record.participant_id, group = record.group.ToString(), record.test_data });
        }

        object StartSessionCommand(JObject request)
        {
            string id = (string)request["participant"];
            if (string.IsNullOrWhiteSpace(id)) return Fail("'participant' is required");

            // Resolve across TECHNICAL GROUPS ONLY. A Thai/Japanese study record can never
            // be found here, so this path cannot start a real participant study session.
            var record = TechnicalGroups
                .SelectMany(g => controller.Registry.List(g))
                .FirstOrDefault(r => r.participant_id == id);
            if (record == null)
            {
                LogCommand("start", id, null, null, "RefusedNonTechnical");
                return Fail("Refused: '" + id + "' is not a DEV/TEST/PILOT participant. " +
                            "This channel is technical-only; real study IDs require an approved study-control design.");
            }
            if (!ResearchParticipantRegistry.IsTechnical(record.group) || !record.test_data)
            {
                LogCommand("start", id, null, null, "RefusedNotTestData");
                return Fail("Refused: '" + id + "' is not marked test data.");
            }
            if (record.session_state != RegistryState.Available && record.session_state != RegistryState.InProgress)
                return Fail("Participant '" + id + "' is " + record.session_state + "; not startable.");

            if (!ParseEnum(request, "language", record.language, out ParticipantLanguage language, out string error)) return Fail(error);
            if (!ParseEnum(request, "input", record.input_mode, out ExperimentInputMode input, out error)) return Fail(error);

            TaskOrder? order = null;
            string layout = null;
            var orderToken = request["order"];
            if (orderToken != null && orderToken.Type != JTokenType.Null)
            {
                // Same cells as the Setup/Console picker: AB, BA, AB_P, AB_Q, BA_P, BA_Q.
                string[] parts = ((string)orderToken ?? "").Replace("->", "").Replace(" ", "").ToUpperInvariant().Split('_');
                if (parts.Length > 2 || !Enum.TryParse(parts[0], out TaskOrder parsed) || !Enum.IsDefined(typeof(TaskOrder), parsed)
                    || parts.Length == 2 && parts[1] != "P" && parts[1] != "Q")
                    return Fail("'order' must be AB, BA, AB_P, AB_Q, BA_P or BA_Q (or omitted for counterbalance).");
                order = parsed;
                if (parts.Length == 2) layout = parts[1];
            }

            if (!controller.LoggerReady) return Fail("Logger not ready: " + controller.Error);
            if (!controller.DeviceReady)
                return Fail("Device not ready: both input guards must report valid tracking. " +
                            "Put the headset on and wake the controllers or show both hands.");

            controller.StartSession(id, language, input, order, layout);
            bool started = controller.Flow != null && string.IsNullOrEmpty(controller.Error);
            LogCommand("start", id, input.ToString(), order == null ? "Auto" : order + (layout == null ? "" : "_" + layout), started ? "Started" : "Failed");
            return started
                ? Ok("start", new { started = id, language = language.ToString(), input = input.ToString(), order = controller.Record?.task_order.ToString(),
                    task_a_layout = controller.Record?.task_a_layout })
                : Fail("StartSession did not take: " + controller.Error);
        }

        object Resume()
        {
            if (controller.Flow == null) return Fail("No session is running.");
            if (!TechnicalSession("resume", out string refusal)) return Fail(refusal);
            var before = controller.Flow.State.ToString();
            controller.ResearcherResume();
            var after = controller.Flow.State.ToString();
            LogCommand("resume", controller.Record?.participant_id, null, null, before + " -> " + after);
            return Ok("resume", new { before, after, changed = before != after });
        }

        object Abort(JObject request)
        {
            if (controller.Record == null || controller.Record.session_state != RegistryState.InProgress)
                return Fail("No in-progress session to abort.");
            if (!TechnicalSession("abort", out string refusal)) return Fail(refusal);
            bool invalid = request["invalid"] != null && (bool)request["invalid"];
            // Researcher-selected S1 termination category; omitted = "other" so an emergency stop never waits on a choice.
            string reason = (string)request["reason"] ?? "other";
            if (Array.IndexOf(AbortReasons, reason) < 0)
                return Fail("'reason' must be one of: " + string.Join(", ", AbortReasons) + ".");
            string id = controller.Record.participant_id;
            // Record the request while the session writer is still open.
            LogCommand("abort", id, null, null, (invalid ? "InvalidateRequested:" : "AbortRequested:") + reason);
            controller.ResearcherAbort(invalid, reason);
            return Ok("abort", new { aborted = id, invalid, reason });
        }

        object AmendReason(JObject request)
        {
            string id = (string)request["participant"], reason = (string)request["reason"];
            var record = TechnicalGroups.SelectMany(g => controller.Registry.List(g)).FirstOrDefault(r => r.participant_id == id);
            if (record == null || !record.test_data) return Fail("Refused: '" + id + "' is not a DEV/TEST/PILOT test-data participant.");
            string error = controller.AmendTermination(id, reason, "ADB");
            if (error != null) return Fail(error);
            var saved = controller.Registry.List(record.group).Single(r => r.participant_id == id);
            return Ok("amend_reason", new { participant = id, saved.termination_reason, saved.termination_amendments,
                note = "Re-export with export_saved so the PC copy carries the amended record." });
        }

        object Demo(JObject request)
        {
            // NOTE: controller.IsTechnical alone is NOT sufficient - it is
            // `Record?.test_data ?? true`, which fails OPEN when there is no session.
            if (!TechnicalSession("demo", out string refusal)) return Fail(refusal);
            if (request["pass"] == null) return Fail("'pass' (true/false) is required.");
            bool pass = (bool)request["pass"];
            controller.SetDemoPass(pass);
            LogCommand("demo", controller.Record?.participant_id, null, null, pass ? "DemoPass" : "DemoFail");
            return Ok("demo", new { pass });
        }

        object Export()
        {
            if (controller.Record == null) return Fail("No session to export.");
            // Export exposes session data and the researcher summary, so it is gated too.
            if (!TechnicalSession("export", out string refusal)) return Fail(refusal);
            string path = controller.ExportSession();
            if (string.IsNullOrEmpty(path)) return Fail("Snapshot failed: " + controller.ExportDeliveryError);
            LogCommand("export", controller.Record.participant_id, null, null, "SnapshotQueued");
            return Ok("export", new { path, delivery = "queued_pc_verification_pending", summary = controller.SummaryText() });
        }

        object ExportSaved(JObject request)
        {
            if(controller.Registry==null)return Fail("Registry not ready.");
            string participant=(string)request["participant"],session=(string)request["session_id"],run=(string)request["run_id"];
            string path=ResearchExportQueue.SnapshotSavedRun(controller.Registry,participant,session,run,controller.Logger?.RunId);
            return Ok("export_saved",new{path,participant_code=participant,session_id=session,run_id=run,lifecycle="partial",delivery="queued_pc_verification_pending",historical_build_provenance="unknown_pre_upgrade"});
        }

        // ---- helpers --------------------------------------------------------

        object Benchmark(JObject request)
        {
            if (!TechnicalSession("benchmark", out string refusal)) return Fail(refusal);
            string mode = (string)request["mode"];
            if (!ResearchRenderExperiment.ValidMode(mode)) return Fail("mode must be resolution, predicted_time or msaa.");
            var experiment = controller.GetComponent<ResearchRenderExperiment>() ?? controller.gameObject.AddComponent<ResearchRenderExperiment>();
            if (!experiment.Begin(controller, mode, out string error, (float?)request["selected_scale"] ?? 0)) return Fail(error);
            LogCommand("benchmark", controller.Record.participant_id, null, null, "Pending:" + mode);
            return Ok("benchmark", new { status = "pending", experiment = experiment.Status });
        }

        // Bounded in-memory motion trace (5-30 s), written once afterwards; never during a render benchmark.
        object MotionTrace(JObject request)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!TechnicalSession("motion_trace", out string refusal)) return Fail(refusal);
            if (controller.GetComponent<ResearchRenderExperiment>()?.Running == true) return Fail("A render benchmark is running; trace after it ends.");
            var trace = controller.GetComponent<ResearchMotionTrace>() ?? controller.gameObject.AddComponent<ResearchMotionTrace>();
            if (!trace.Begin(controller, (int?)request["seconds"] ?? 20, out string error)) return Fail(error);
            LogCommand("motion_trace", controller.Record.participant_id, null, null, "Capturing");
            return Ok("motion_trace", trace.Status);
#else
            return Fail("Motion trace exists only in the technical development build.");
#endif
        }

        // view_probe: idle, unworn app only (the component re-checks stillness); frame_timing: idle or a technical session.
        object ViewDiagnostic(string command, JObject request)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (controller.Record != null && !TechnicalSession(command, out string refusal)) return Fail(refusal);
            var diagnostic = controller.GetComponent<ResearchViewDiagnostic>() ?? controller.gameObject.AddComponent<ResearchViewDiagnostic>();
            string error;
            bool started;
            if (command == "frame_timing") started = diagnostic.BeginTiming(controller, (int?)request["seconds"] ?? 20, out error);
            else if (request["plan"] is JObject plan) started = diagnostic.BeginProbe(controller, plan, out error);
            else { started = false; error = "'plan' object required."; }
            return started ? Ok(command, diagnostic.Status) : Fail(error);
#else
            return Fail("View diagnostics exist only in the technical development build.");
#endif
        }

        object CancelBenchmark()
        {
            if (!TechnicalSession("benchmark_cancel", out string refusal)) return Fail(refusal);
            var experiment = controller.GetComponent<ResearchRenderExperiment>();
            experiment?.Cancel();
            LogCommand("benchmark_cancel", controller.Record.participant_id, null, null, "Cancelled");
            return Ok("benchmark_cancel", experiment?.Status);
        }

        object HapticProbe()
        {
            if (!TechnicalSession("haptic_probe", out string refusal)) return Fail(refusal);
            var results = new[] { UnityEngine.XR.XRNode.LeftHand, UnityEngine.XR.XRNode.RightHand }.Select(node =>
            {
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                bool known = device.TryGetHapticCapabilities(out var capabilities);
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
                bool submitted = ResearchUIFeedback.TryPulse(device);
                return new { node = node.ToString(), valid = device.isValid, tracked, capabilities_known = known,
                    supports_impulse = known ? (bool?)capabilities.supportsImpulse : null,
                    channels = known ? (uint?)capabilities.numChannels : null, submitted,
                    requested_amplitude = ResearchUIFeedback.HapticAmplitude, requested_seconds = ResearchUIFeedback.HapticSeconds,
                    felt_vibration_verified = false, hardware_verified = false };
            }).ToArray();
            LogCommand("haptic_probe", controller.Record.participant_id, null, null, "ExplicitTechnicalApiProbe");
            return Ok("haptic_probe", results);
        }

        /// <summary>
        /// Refuses any command that would act on a session which is not DEV/TEST/PILOT test data.
        /// All three conditions are required. controller.IsTechnical is deliberately NOT used on
        /// its own because it is `Record?.test_data ?? true` and therefore fails open on a null
        /// Record. A refusal is logged so external attempts are visible in the session log.
        /// </summary>
        bool TechnicalSession(string command, out string error)
        {
            var record = controller.Record;
            if (record == null) { error = "No session is running."; return false; }

            bool technical = record.test_data
                             && ResearchParticipantRegistry.IsTechnical(record.group)
                             && controller.IsTechnical;
            if (!technical)
            {
                LogCommand(command, record.participant_id, null, null, "RefusedNonTechnicalSession");
                error = "Refused: current session '" + record.participant_id + "' (group " + record.group +
                        ") is not a DEV/TEST/PILOT test-data session. This ADB channel cannot act on a " +
                        "real participant study session; that requires an approved study-control design.";
                return false;
            }
            error = null; return true;
        }

        bool TechnicalGroup(JObject request, out ParticipantGroup group, out string error)
        {
            group = ParticipantGroup.Dev;
            string text = (string)request["group"];
            if (string.IsNullOrWhiteSpace(text)) { error = "'group' is required (Dev, Test or Pilot)."; return false; }
            if (!Enum.TryParse(text, true, out group) || !Enum.IsDefined(typeof(ParticipantGroup), group))
            { error = "'group' is not a valid participant group."; return false; }
            if (!ResearchParticipantRegistry.IsTechnical(group))
            { error = "Refused: '" + group + "' is a real study group. This channel is DEV/TEST/PILOT only."; return false; }
            error = null; return true;
        }

        static bool ParseEnum<T>(JObject request, string key, T fallback, out T value, out string error) where T : struct
        {
            value = fallback; error = null;
            var token = request[key];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (!Enum.TryParse((string)token, true, out value) || !Enum.IsDefined(typeof(T), value))
            { error = "'" + key + "' is not a valid " + typeof(T).Name + "."; return false; }
            return true;
        }

        /// <summary>Every external researcher command is recorded in the session log.</summary>
        void LogCommand(string command, string participant, string input, string order, string result)
        {
            controller.Log("RESEARCHER_COMMAND", "researcher.control." + command, result,
                after: new
                {
                    command,
                    source = "ADB",
                    participant_id = participant,
                    input_mode = input,
                    task_order = order,
                    result,
                    timestamp = DateTime.UtcNow.ToString("O")
                },
                source: "Tester");
        }

        object Ok(string command, object data = null) => new
        {
            ok = true,
            command,
            data,
            state = State(),
            timestamp = DateTime.UtcNow.ToString("O")
        };

        object Fail(string error) => new
        {
            ok = false,
            error,
            state = State(),
            timestamp = DateTime.UtcNow.ToString("O")
        };

        object State() => new
        {
            flow_state = controller.Flow != null ? controller.Flow.State.ToString() : "ResearcherSetup",
            participant_id = controller.Record?.participant_id,
            test_data = controller.Record?.test_data,
            language = controller.Record?.language.ToString(),
            input_mode = controller.Record?.input_mode.ToString(),
            task_order = controller.Record?.task_order.ToString(),
            current_task = controller.Flow?.CurrentTask,
            paused = controller.Flow?.Paused,
            device_ready = controller.DeviceReady,
            technical_self_setup = controller.TechnicalSelfSetup,
            technical_self_test = controller.TechnicalSelfTest,
            // Researcher readiness readout: computed only when a command arrives, never per frame.
            app_focused = Application.isFocused,
            headset = Headset(),
            floor_ready = controller.FloorReady,
            controllers = controller.guards.Where(g => g).Select(g => new { side = g.side.ToString(), mode = g.Mode.ToString(),
                tracking_valid = g.TrackingValid, interactions_allowed = g.InteractionsAllowed, suspended = g.Suspended,
                suspension_reason = g.SuspensionReason, waiting_for_neutral = g.WaitingForNeutral }).ToArray(),
            can_interact = controller.LocalUICommandsAllowed,
            flow_can_interact = controller.Flow?.CanInteract,
            transitioning = controller.Transitioning,
            diagnostic_capture = DiagnosticCapture(),
            logger_ready = controller.LoggerReady,
            run_id = controller.Record?.run_id,
            session_id = controller.Record?.session_id,
            export_path = controller.LastExportPath,
            export_delivery_error = controller.ExportDeliveryError,
            technical_scenario = controller.GetComponent<ResearchTechnicalScenario>()?.State,
            supported_scenario_render_modes = new[] { "msaa", "resolution" },
            movement = controller.GetComponent<ResearchMovementSampler>() is ResearchMovementSampler sampler ?
                new { scheduled_frames = sampler.ScheduledFrames, last_write_ms = sampler.LastWriteMilliseconds,
                    maximum_write_ms = sampler.MaximumWriteMilliseconds, native_sensor_timestamp_available = false } : null,
            error = controller.Error,
            benchmark = controller.GetComponent<ResearchRenderExperiment>()?.Status,
            render_settings = ResearchRenderExperiment.CurrentSettings(),
            motion_trace = MotionTraceStatus(),
            // Rendered-hand contact: unresolved = frames the visual skeleton was held at its last accepted pose.
            hand_contact = controller.origin ? controller.origin.GetComponentsInChildren<ResearchHandContactProxy>(true).Select(p => new { target = p.name,
                unresolved = p.UnresolvedContacts, contact_updates = p.ContactUpdates, separation = p.SeparationMetres, saturated = p.SaturatedQueries,
                max_ms = p.MaxUpdateMilliseconds }).ToArray() : null,
            drag_filter = ResearchDragHandle.FilterEnabled,
            held_objects = controller.trainingObjects.Where(o => o && o.TryGetComponent(out ResearchHeldObjectConstraint _))
                .Select(o => o.GetComponent<ResearchHeldObjectConstraint>()).Select(c => new { target = c.name, holding = c.Holding,
                    desired_separation = c.DesiredSeparationMetres, max_desired_separation = c.MaxDesiredSeparationMetres, updates = c.Updates,
                    constrained = c.ConstrainedUpdates, rejected_substeps = c.RejectedSubsteps, unresolved = c.UnresolvedPoses,
                    saturated = c.SaturatedQueries, release_corrections = c.ReleaseCorrections,
                    last_ms = c.LastUpdateMilliseconds, max_ms = c.MaxUpdateMilliseconds }).ToArray(),
            task_ports = FindObjectsByType<ResearchTaskPort>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(p => new { p.taskId, p.demoMode, p.contentApproved }).ToArray()
        };

        static object Headset()
        {
            var hmd = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked);
            bool known = hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present);
            return new { valid = hmd.isValid, tracked, user_present = known ? (bool?)present : null };
        }

        object MotionTraceStatus()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return controller.GetComponent<ResearchMotionTrace>()?.Status;
#else
            return null;
#endif
        }

        bool DiagnosticCapture()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var probe = controller.GetComponent<ResearchFlowPhysicalProbe>();
            return probe && probe.CaptureAlive;
#else
            return false;
#endif
        }

        void Respond(string command, object payload)
        {
            try
            {
                var response = JObject.FromObject(payload);
                response["request_id"] = requestId;
                response["request_binding"] = requestBinding;
#if UNITY_ANDROID && !UNITY_EDITOR
                PublishProfile();
                using (var provider = new AndroidJavaClass(ProfileProvider))
                {
                    string identity = provider.CallStatic<string>("identityJson");
                    response["transport_identity"] = identity == null ? null : JObject.Parse(identity);
                }
#endif
                response["response_created_utc"] = DateTime.UtcNow.ToString("O");
                File.WriteAllText(responsePath, response.ToString(Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning("RESEARCH_ADB_CHANNEL response write failed: " + ex.Message); }
            Debug.Log("RESEARCH_ADB_CHANNEL " + command + " -> " + JsonConvert.SerializeObject(payload));
        }
    }
}
