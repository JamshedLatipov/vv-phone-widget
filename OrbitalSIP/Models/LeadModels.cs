using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class CreateLeadRequest
    {
        [JsonPropertyName("contactId")]
        public string? ContactId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("companyId")]
        public string? CompanyId { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("stageId")]
        public string? StageId { get; set; }

        [JsonPropertyName("boardId")]
        public string? BoardId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "new";

        [JsonPropertyName("score")]
        public int Score { get; set; } = 0;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "phone";

        [JsonPropertyName("sourceDetails")]
        public string? SourceDetails { get; set; }

        [JsonPropertyName("campaign")]
        public string? Campaign { get; set; }

        [JsonPropertyName("utmSource")]
        public string? UtmSource { get; set; }

        [JsonPropertyName("utmMedium")]
        public string? UtmMedium { get; set; }

        [JsonPropertyName("utmCampaign")]
        public string? UtmCampaign { get; set; }

        [JsonPropertyName("utmContent")]
        public string? UtmContent { get; set; }

        [JsonPropertyName("utmTerm")]
        public string? UtmTerm { get; set; }

        [JsonPropertyName("assignedTo")]
        public string? AssignedTo { get; set; }

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "low";

        [JsonPropertyName("estimatedValue")]
        public int EstimatedValue { get; set; } = 0;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("customFields")]
        public Dictionary<string, string>? CustomFields { get; set; } = new Dictionary<string, string>();

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; } = new List<string>();

        [JsonPropertyName("nextFollowUpDate")]
        public string? NextFollowUpDate { get; set; }
    }

    /// <summary>
    /// Values of <see cref="LeadCallContext.LeadState"/>. Mirrors the backend's
    /// LeadCallState union (lead-call-context.service.ts). Modelled as strings,
    /// not a C# enum, like every other backend-supplied enum in these models
    /// (see CallInfoUi.Type, StatusState.ManualStatus): an unknown value from a
    /// newer backend must arrive intact rather than blow up deserialization.
    /// </summary>
    public static class LeadCallStates
    {
        /// <summary>An open lead exists and is in <see cref="LeadCallContext.Lead"/>.</summary>
        public const string Active = "active";

        /// <summary>This number has no open lead — creating one is safe.</summary>
        public const string None = "none";

        /// <summary>An open lead provably exists but could not be loaded.</summary>
        public const string Unavailable = "unavailable";
    }

    /// <summary>Values of <see cref="LeadCallActions.TransferBlockedReason"/>.</summary>
    public static class TransferBlockedReasons
    {
        public const string NoOwner = "no_owner";
        public const string NoExtension = "no_extension";
        public const string NotRegistered = "not_registered";
        public const string OnCall = "on_call";
        public const string Paused = "paused";
    }

    /// <summary>
    /// Response of GET /api/leads/call-context?phone=… — does the caller already
    /// have an open lead, who owns it, and what may this operator do about it.
    /// </summary>
    public class LeadCallContext
    {
        [JsonPropertyName("contactId")]
        public string? ContactId { get; set; }

        /// <summary>
        /// One of <see cref="LeadCallStates"/>. THIS, not <c>Lead == null</c>, is
        /// the discriminator: `none` and `unavailable` both carry a null lead,
        /// and offering «Создать лид» on `unavailable` is the 409 this feature
        /// exists to remove. Read it through <see cref="HasActiveLead"/> /
        /// <see cref="HasNoLead"/> / <see cref="LeadLookupUnavailable"/>.
        ///
        /// Defaults to <see cref="LeadCallStates.Unavailable"/> so a body that
        /// omits the field fails closed (no create offered) instead of reading
        /// as «no lead».
        /// </summary>
        [JsonPropertyName("leadState")]
        public string LeadState { get; set; } = LeadCallStates.Unavailable;

        [JsonPropertyName("lead")]
        public LeadCallSummary? Lead { get; set; }

        [JsonPropertyName("owner")]
        public LeadCallOwner? Owner { get; set; }

        [JsonPropertyName("actions")]
        public LeadCallActions Actions { get; set; } = new();

        /// <summary>An open lead exists and was loaded — show it.</summary>
        [JsonIgnore]
        public bool HasActiveLead =>
            string.Equals(LeadState, LeadCallStates.Active, StringComparison.Ordinal) && Lead != null;

        /// <summary>No open lead for this number — a create is legitimate here
        /// (subject to <see cref="LeadCallActions.CanCreateLead"/>, which also
        /// carries the caller's `leads:create` permission).</summary>
        [JsonIgnore]
        public bool HasNoLead =>
            string.Equals(LeadState, LeadCallStates.None, StringComparison.Ordinal);

        /// <summary>
        /// Neither of the above: the server said `unavailable`, or sent a state
        /// this build does not know. Both mean «we cannot vouch that creating is
        /// safe», so this is the residual on purpose — a state added server-side
        /// later lands here rather than in <see cref="HasNoLead"/>.
        /// </summary>
        [JsonIgnore]
        public bool LeadLookupUnavailable => !HasActiveLead && !HasNoLead;
    }

    /// <summary>The open lead itself, present only when leadState is `active`.</summary>
    public class LeadCallSummary
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Backend LeadStatus: new / contacted / qualified / …</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("stageName")]
        public string? StageName { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>Absolute CRM link, built server-side from FRONTEND_URL.</summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    /// <summary>Who owns the lead, and whether a transfer would reach them.</summary>
    public class LeadCallOwner
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("fullName")]
        public string FullName { get; set; } = "";

        [JsonPropertyName("extensionNumber")]
        public string? ExtensionNumber { get; set; }

        [JsonPropertyName("sipRegistered")]
        public bool SipRegistered { get; set; }

        [JsonPropertyName("onCall")]
        public bool OnCall { get; set; }

        [JsonPropertyName("manualStatus")]
        public string? ManualStatus { get; set; }

        /// <summary>Supervisor-forced pause; leaves <see cref="ManualStatus"/> null.</summary>
        [JsonPropertyName("supervisorPaused")]
        public bool SupervisorPaused { get; set; }
    }

    /// <summary>
    /// What this operator may do, already resolved against their permissions —
    /// notably <see cref="CanCreateLead"/>, which is false for cc_operator /
    /// cc_manager because they hold `lead-call:read` but not `leads:create`.
    /// </summary>
    public class LeadCallActions
    {
        [JsonPropertyName("canCreateLead")]
        public bool CanCreateLead { get; set; }

        [JsonPropertyName("canOpenLead")]
        public bool CanOpenLead { get; set; }

        [JsonPropertyName("canTransferToOwner")]
        public bool CanTransferToOwner { get; set; }

        [JsonPropertyName("canComment")]
        public bool CanComment { get; set; }

        /// <summary>One of <see cref="TransferBlockedReasons"/>, or null when the
        /// transfer is allowed.</summary>
        [JsonPropertyName("transferBlockedReason")]
        public string? TransferBlockedReason { get; set; }
    }

    /// <summary>
    /// Outcome of GET /api/leads/call-context. Deliberately NOT a bare
    /// <c>LeadCallContext?</c>: a null there reads as «no lead» at the call site,
    /// and a failed lookup rendered as «no lead» re-offers exactly the create
    /// this feature removes. Check <see cref="Success"/> before <see cref="Context"/>.
    /// </summary>
    public sealed class LeadCallContextResult
    {
        private LeadCallContextResult(LeadCallContext? context, string? error)
        {
            Context = context;
            Error = error;
        }

        /// <summary>The payload, non-null iff <see cref="Success"/>.</summary>
        public LeadCallContext? Context { get; }

        /// <summary>Short human-readable reason the lookup failed, else null.</summary>
        public string? Error { get; }

        public bool Success => Context != null;

        public static LeadCallContextResult Loaded(LeadCallContext context) =>
            new LeadCallContextResult(context, null);

        public static LeadCallContextResult Failed(string error) =>
            new LeadCallContextResult(null, error);
    }

    /// <summary>
    /// Payload for POST /api/leads/{id}/call-comment. Mirrors the backend
    /// CallCommentDto; null links are omitted on the wire (see LeadService).
    /// </summary>
    public class AddCallCommentRequest
    {
        [JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        /// <summary>UUID of the CallLog row. Must be a syntactically valid uuid —
        /// the backend rejects anything else with 400.</summary>
        [JsonPropertyName("callLogId")]
        public string? CallLogId { get; set; }

        /// <summary>Asterisk uniqueId, when the call_logs row does not exist yet.</summary>
        [JsonPropertyName("callUniqueId")]
        public string? CallUniqueId { get; set; }
    }

    /// <summary>
    /// Outcome of POST /api/leads. Replaces a bare bool so the 409
    /// «у клиента уже есть открытый лид» is distinguishable from a genuine
    /// failure — the widget reacts to it by showing the existing lead instead of
    /// an error.
    /// </summary>
    public sealed class CreateLeadResult
    {
        private CreateLeadResult(bool success, bool alreadyOpen, int? existingLeadId, string? message)
        {
            Success = success;
            AlreadyOpen = alreadyOpen;
            ExistingLeadId = existingLeadId;
            Message = message;
        }

        public bool Success { get; }

        /// <summary>409 with `error: LEAD_ALREADY_OPEN`.</summary>
        public bool AlreadyOpen { get; }

        /// <summary>`errors.existingLeadId` from the conflict body, when present.</summary>
        public int? ExistingLeadId { get; }

        /// <summary>The conflict's `message`, ready to show to the operator.</summary>
        public string? Message { get; }

        public static CreateLeadResult Created() =>
            new CreateLeadResult(true, false, null, null);

        public static CreateLeadResult Duplicate(int? existingLeadId, string? message) =>
            new CreateLeadResult(false, true, existingLeadId, message);

        public static CreateLeadResult Failed() =>
            new CreateLeadResult(false, false, null, null);
    }
}
