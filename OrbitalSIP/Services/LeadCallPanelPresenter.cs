using System;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>What the call panel renders for the caller's lead.</summary>
    public enum LeadPanelState
    {
        /// <summary>Lookup still in flight. No create button — we do not yet know
        /// whether creating is safe.</summary>
        Loading,

        /// <summary>An open lead exists and was loaded: show the card.</summary>
        ActiveLead,

        /// <summary>No open lead AND this operator may create one.</summary>
        OfferCreate,

        /// <summary>No open lead, but this operator holds no `leads:create` —
        /// cc_operator / cc_manager. Show nothing rather than a button that 403s.</summary>
        Hidden,

        /// <summary>The lookup failed, or the server could not determine the lead.
        /// Show the retry message. Never a create button.</summary>
        Unavailable,
    }

    /// <summary>
    /// Pure decision logic for the in-call lead panel, kept out of the view so it
    /// can be tested without an Avalonia harness (same split as CallInfoPresenter).
    ///
    /// The rule these functions enforce: the create button appears ONLY in
    /// <see cref="LeadPanelState.OfferCreate"/>. A failed lookup, an `unavailable`
    /// state, and «no lead but no permission» all withhold it — rendering any of
    /// them as «no lead» is the false affordance this whole feature removes.
    /// </summary>
    public static class LeadCallPanelPresenter
    {
        public const string TransferBlockedUnknownKey = "LeadTransferBlockedUnknown";

        /// <summary>
        /// Picks the panel state. A null result means «not looked up yet»; a result
        /// that failed is <see cref="LeadPanelState.Unavailable"/>, NEVER
        /// «no lead» — that collapse is the bug.
        /// </summary>
        public static LeadPanelState SelectState(LeadCallContextResult? result)
        {
            if (result == null)
                return LeadPanelState.Loading;

            if (!result.Success || result.Context == null)
                return LeadPanelState.Unavailable;

            var context = result.Context;

            if (context.HasActiveLead)
                return LeadPanelState.ActiveLead;

            if (context.HasNoLead)
                return context.Actions.CanCreateLead
                    ? LeadPanelState.OfferCreate
                    : LeadPanelState.Hidden;

            // `unavailable`, or a state this build does not recognise.
            return LeadPanelState.Unavailable;
        }

        /// <summary>The one place that decides whether «Создать лид» is on screen.</summary>
        public static bool ShowsCreateButton(LeadPanelState state) =>
            state == LeadPanelState.OfferCreate;

        public static bool ShowsLeadCard(LeadPanelState state) =>
            state == LeadPanelState.ActiveLead;

        /// <summary>
        /// Whether the one-tap transfer may fire. The server already folds every
        /// blocking condition into CanTransferToOwner; the extension check is kept
        /// because it is what actually gets dialled and an empty string would place
        /// a call to nowhere.
        /// </summary>
        public static bool CanTransferToOwner(LeadCallContext? context) =>
            context != null
            && context.HasActiveLead
            && context.Actions.CanTransferToOwner
            && !string.IsNullOrWhiteSpace(context.Owner?.ExtensionNumber);

        /// <summary>
        /// i18n key explaining why the transfer is unavailable, or null when it is
        /// available. Driven by the server's reason, never re-derived from
        /// ManualStatus: a supervisor-forced pause leaves ManualStatus null but
        /// still yields `paused`, so deriving it locally would show «нет причины»
        /// for exactly the operator a supervisor pulled out of rotation.
        /// </summary>
        public static string? TransferBlockedKey(string? reason)
        {
            if (reason == null)
                return null;

            return reason switch
            {
                TransferBlockedReasons.NoOwner => "LeadTransferBlockedNoOwner",
                TransferBlockedReasons.NoExtension => "LeadTransferBlockedNoExtension",
                TransferBlockedReasons.NotRegistered => "LeadTransferBlockedNotRegistered",
                TransferBlockedReasons.OnCall => "LeadTransferBlockedOnCall",
                TransferBlockedReasons.Paused => "LeadTransferBlockedPaused",
                // A reason added server-side later still has to say SOMETHING —
                // a disabled button with no explanation is worse than a generic one.
                _ => TransferBlockedUnknownKey,
            };
        }

        /// <summary>«#4821 · Иванов Иван»</summary>
        public static string LeadHeadline(LeadCallSummary lead) =>
            $"#{lead.Id} · {lead.Name}";

        /// <summary>Status, plus the stage when the lead sits on a pipeline.</summary>
        public static string LeadSubline(LeadCallSummary lead) =>
            string.IsNullOrWhiteSpace(lead.StageName)
                ? lead.Status
                : $"{lead.Status} · {lead.StageName}";

        /// <summary>
        /// Re-validates the CRM link before it reaches the OS shell. The backend
        /// already builds it from FRONTEND_URL and checks the scheme, but this
        /// string is handed to the shell, so a non-http(s) value arriving through a
        /// misconfigured backend would be an arbitrary-URI launch. Fail closed.
        /// </summary>
        public static bool IsLaunchableUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
