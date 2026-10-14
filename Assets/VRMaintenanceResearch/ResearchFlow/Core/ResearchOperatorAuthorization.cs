using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRMaintenanceResearch.ResearchFlow
{
    [Serializable]
    public sealed class ResearchOperatorAuthorization
    {
        public string schema, operator_id, participant_id, session_id, link_status;
        public long? record_revision;
        public bool? consent_approved, content_approved, language_approved;
        public string consent_approval_ref, content_approval_ref, language_approval_ref;
    }

    [Serializable]
    public sealed class ResearchOperatorSessionLink
    {
        public string schema = "research.operator-session-link.1";
        public string authorization_sha256, operator_id, participant_id, session_id, run_id, linked_at;
    }

    [Serializable]
    public sealed class ResearchOperatorCommand
    {
        public string schema, request_id, command, participant_id, session_id, termination_reason;
        public ParticipantLanguage? language;
        public ExperimentInputMode? input_mode;
        public TaskOrder? task_order;
        public string task_a_layout;
    }

    // Local provenance assertion, not an approval authority: no default grants and no generated approvals.
    public static class ResearchOperatorGuard
    {
        public static ResearchOperatorAuthorization ParseAuthorization(string json)
        {
            var document = JObject.Parse(json);
            foreach (var property in document.Properties())
                if (string.Equals(property.Name, "consent_approved", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "content_approved", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "language_approved", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.Type != JTokenType.Boolean && property.Value.Type != JTokenType.Null)
                        throw new InvalidOperationException("Approval acknowledgments must be actual JSON booleans or null.");
                }
            return document.ToObject<ResearchOperatorAuthorization>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
        }

        public static bool ValidTerminationReason(string reason) => reason == "pass" || reason == "time_limit" || reason == "withdrawal"
            || reason == "safety_stop" || reason == "technical_failure" || reason == "other";

        public static bool IsTechnical(ParticipantRecord record) => record != null &&
            ResearchParticipantRegistry.IsTechnical(record.group) && record.test_data;

        public static string Demand(string command, ParticipantRecord record, ResearchOperatorAuthorization authorization)
        {
            if (command != "start" && command != "resume" && command != "abort" && command != "invalidate" && command != "export")
                throw new InvalidOperationException("Unsupported local operator command.");
            if (record == null) throw new InvalidOperationException("An exact participant/session record is required.");
            if (IsTechnical(record)) return "TechnicalTestData";
            if (record.group != ParticipantGroup.Thai && record.group != ParticipantGroup.Japanese || record.test_data)
                throw new InvalidOperationException("Participant group and test-data provenance are inconsistent.");
            if (authorization == null || authorization.schema != "research.operator-authorization.1")
                throw new InvalidOperationException("Production operator authorization is missing or has an unsupported schema.");
            if (string.IsNullOrWhiteSpace(authorization.operator_id) || authorization.participant_id != record.participant_id)
                throw new InvalidOperationException("Production operator/participant identity is not linked.");
            bool preSession = command == "start" && record.session_state == RegistryState.Available;
            if (preSession)
            {
                if (authorization.link_status != "pre_session" || !string.IsNullOrEmpty(authorization.session_id)
                    || authorization.record_revision != record.revision || !string.IsNullOrEmpty(record.session_id))
                    throw new InvalidOperationException("Pre-session approval must match the available record revision exactly.");
            }
            else if (authorization.link_status != "linked" || string.IsNullOrEmpty(record.session_id)
                || authorization.session_id != record.session_id)
                throw new InvalidOperationException("Production operator authorization is linked to a different or missing session.");
            if (authorization.consent_approved != true || string.IsNullOrWhiteSpace(authorization.consent_approval_ref)
                || authorization.content_approved != true || string.IsNullOrWhiteSpace(authorization.content_approval_ref)
                || authorization.language_approved != true || string.IsNullOrWhiteSpace(authorization.language_approval_ref))
                throw new InvalidOperationException("Explicit consent, content and language approvals and their references are required.");
            return "ProductionApprovedExternalAssertions";
        }

        public static void DemandRequest(ResearchOperatorCommand request, ParticipantRecord record)
        {
            if (request == null || request.schema != "research.operator-command.1" || !Guid.TryParseExact(request.request_id, "N", out _))
                throw new InvalidOperationException("A versioned local command with a UUID request ID is required.");
            if (record == null || request.participant_id != record.participant_id || request.session_id != (record.session_id ?? ""))
                throw new InvalidOperationException("Local command participant/session identity does not match the current record.");
            if (request.termination_reason != null && !ValidTerminationReason(request.termination_reason))
                throw new InvalidOperationException("Local command termination reason is unsupported.");
            if ((request.command == "abort" || request.command == "invalidate") && request.termination_reason == "pass")
                throw new InvalidOperationException("Aborted/invalidated sessions cannot claim a passed task.");
            if (request.language.HasValue && !Enum.IsDefined(typeof(ParticipantLanguage), request.language.Value)
                || request.input_mode.HasValue && !Enum.IsDefined(typeof(ExperimentInputMode), request.input_mode.Value)
                || request.task_order.HasValue && !Enum.IsDefined(typeof(TaskOrder), request.task_order.Value))
                throw new InvalidOperationException("Local command setup values are invalid.");
        }

        public static bool LinkMatches(ResearchOperatorSessionLink link, ResearchOperatorAuthorization authorization, string authorizationHash, ParticipantRecord record)
            => link != null && authorization != null && record != null && link.schema == "research.operator-session-link.1"
                && link.authorization_sha256 == authorizationHash && !string.IsNullOrEmpty(authorizationHash)
                && link.operator_id == authorization.operator_id && link.participant_id == authorization.participant_id
                && link.participant_id == record.participant_id && !string.IsNullOrEmpty(link.session_id)
                && link.session_id == record.session_id && link.run_id == record.run_id;
    }
}
