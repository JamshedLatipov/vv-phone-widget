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

        /// <summary>
        /// A card painted from a 409 `LEAD_ALREADY_OPEN`, whose background refresh
        /// did not (yet) come back with a full lead. The 409 is PROOF the lead
        /// exists, so this outranks Unavailable and OfferCreate: it must not decay
        /// into «не удалось проверить», and certainly not back into a create button.
        /// </summary>
        ConflictLead,
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

        /// <summary>
        /// The panel state for the view's complete input. This is the entry point
        /// the view uses; the single-argument overload above is the core of it.
        /// </summary>
        public static LeadPanelState SelectState(
            string? callerNumber,
            LeadCallContextResult? result,
            bool conflictShown)
        {
            // An anonymous / withheld number cannot be looked up at all — the
            // endpoint requires at least one digit in `phone` — so «Не удалось
            // проверить» plus a «Повторить» that can never succeed would be a lie.
            // Render nothing instead.
            if (!IsLookupablePhone(callerNumber))
                return LeadPanelState.Hidden;

            var state = SelectState(result);

            // A 409 already proved the lead exists. The background refresh may
            // only UPGRADE that to a full card; letting it downgrade would replace
            // proof with «не удалось проверить», or — worse — re-offer the very
            // create the 409 just refused.
            if (conflictShown && state != LeadPanelState.ActiveLead)
                return LeadPanelState.ConflictLead;

            return state;
        }

        /// <summary>Mirrors the endpoint's own rule: `phone` must be non-empty and
        /// carry at least one digit, else the request is rejected with 400.</summary>
        public static bool IsLookupablePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return false;

            foreach (var c in phone)
                if (char.IsDigit(c)) return true;

            return false;
        }

        /// <summary>
        /// The one place that decides whether «Создать лид» is on screen.
        ///
        /// Withheld ONLY where we positively know it does not belong. Everything
        /// else — including a failed lookup — keeps it, because hiding it there
        /// would remove a capability operators have today: the desktop release ships
        /// separately from the CRM (CLAUDE.md §12), so a widget deployed first would
        /// leave EVERY operator unable to create a lead until the backend lands, and
        /// an operator on a role that cannot reach `call-context` would lose it
        /// permanently.
        ///
        /// This is deliberately NOT fail-closed, unlike the lead-card decisions
        /// above, and the difference is the conflict card: an optimistic create is
        /// now safe, because a duplicate comes back 409 and the panel renders the
        /// existing lead instead of an error. The false affordance this feature
        /// removes is a create that DEAD-ENDS, and it no longer does.
        /// </summary>
        public static bool ShowsCreateButton(LeadPanelState state) => state switch
        {
            // A lead is on screen — creating a second one is exactly the 409.
            LeadPanelState.ActiveLead => false,
            LeadPanelState.ConflictLead => false,
            // `Hidden` has TWO producers, and the button is withheld for both —
            // for different reasons, so neither can be removed on the other's
            // argument:
            //
            //  1. The lookup succeeded and said no lead AND no `leads:create`
            //     (cc_operator / cc_manager). The server told us plainly that a
            //     create would come back 403.
            //
            //  2. The caller number carries no digits — withheld / anonymous /
            //     «Неизвестный» — so there is nothing to look up and nothing to
            //     put ON the lead. A lead minted here has no number to call back
            //     on: junk its owner then has to clean up.
            //     This one is decided CLIENT-side, with no backend involved, so
            //     the deploy-ordering argument above deliberately does NOT rescue
            //     it — that argument is about not letting an unreachable CRM cost
            //     us the button, and this decision needs no CRM to be correct.
            //     It IS a behaviour change: before this panel, «Лид» on a withheld
            //     number created one named after whatever the label showed.
            LeadPanelState.Hidden => false,

            LeadPanelState.OfferCreate => true,
            // We could not check. Optimistic create, 409 handles the duplicate.
            LeadPanelState.Unavailable => true,
            // Still checking — the lookup can take a while (its server side runs a
            // full AMI endpoint sweep) and that is the worst moment to have no
            // button, mid-call.
            LeadPanelState.Loading => true,

            // A state added later should preserve the capability, not silently
            // remove it. See above: an unnecessary create is cheap now.
            _ => true,
        };

        public static bool ShowsLeadCard(LeadPanelState state) =>
            state == LeadPanelState.ActiveLead || state == LeadPanelState.ConflictLead;

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

        /// <summary>
        /// Which confirmation the comment box shows. A comment that saved without a
        /// call link is a SUCCESS with a caveat, not a failure: the note is on the
        /// lead either way, but the link to the recording — the point of attributing
        /// it to this call — is missing, and silently claiming plain success would
        /// hide that.
        /// </summary>
        public static string CommentStatusKey(bool saved, bool linkedToCall)
        {
            if (!saved) return "LeadPanelCommentFailed";
            return linkedToCall ? "LeadPanelCommentSaved" : "LeadPanelCommentSavedNoLink";
        }

        /// <summary>Reasons the transfer is unavailable, with a generic fallback for
        /// «blocked but the server named no reason».</summary>
        public static string TransferBlockedKeyOrDefault(string? reason) =>
            TransferBlockedKey(reason) ?? TransferBlockedUnknownKey;

        /// <summary>«#4821 · Иванов Иван»</summary>
        public static string LeadHeadline(LeadCallSummary lead) =>
            JoinParts($"#{lead.Id}", lead.Name);

        /// <summary>
        /// Headline for a card built from a 409 alone. `existingLeadName` really can
        /// be null (`existing?.name ?? null` on the backend), so the parts are joined
        /// only when non-empty — «#4821 ·» with a dangling separator is worse than
        /// «#4821».
        /// </summary>
        public static string ConflictHeadline(int? leadId, string? leadName, string? fallbackMessage)
        {
            var headline = JoinParts(leadId.HasValue ? $"#{leadId}" : null, leadName);
            return headline.Length > 0 ? headline : (fallbackMessage ?? string.Empty);
        }

        /// <summary>Status, plus the stage when the lead sits on a pipeline.</summary>
        public static string LeadSubline(string? statusText, string? stageName) =>
            JoinParts(statusText, stageName);

        /// <summary>
        /// i18n key for a backend LeadStatus, or null for a value this build does
        /// not know — the caller then shows the raw value, exactly as the CRM's own
        /// statusLabel() falls back to it.
        /// </summary>
        public static string? LeadStatusKey(string? status) => status switch
        {
            "new" => "LeadStatusNew",
            "contacted" => "LeadStatusContacted",
            "qualified" => "LeadStatusQualified",
            "proposal_sent" => "LeadStatusProposalSent",
            "negotiating" => "LeadStatusNegotiating",
            "converted" => "LeadStatusConverted",
            "rejected" => "LeadStatusRejected",
            "lost" => "LeadStatusLost",
            "overdue" => "LeadStatusOverdue",
            "stalled" => "LeadStatusStalled",
            "no_answer" => "LeadStatusNoAnswer",
            _ => null,
        };

        /// <summary>
        /// Identity of the CURRENT call, used to decide whether cached context may
        /// be reused when the view is rebuilt, or null when there is no reliable
        /// identity and NOTHING may be cached.
        ///
        /// The start time is in the key because the same number calling back later
        /// is a different call, and reusing its stale «no lead» would re-offer a
        /// create for a caller who has since acquired one. It is also why a null
        /// start time yields no key at all rather than a placeholder: SipService
        /// leaves it null before answer and resets it on hangup, while
        /// ActiveCallerId is never cleared, so a shared «number + nothing» key would
        /// let a pre-answer view hand its stale result to a later call.
        /// </summary>
        public static string? BuildCallKey(string? callerNumber, DateTime? callStartedAt) =>
            callStartedAt == null || string.IsNullOrWhiteSpace(callerNumber)
                ? null
                : $"{callerNumber}|{callStartedAt.Value.ToString("O")}";

        private static string JoinParts(string? first, string? second)
        {
            var hasFirst = !string.IsNullOrWhiteSpace(first);
            var hasSecond = !string.IsNullOrWhiteSpace(second);

            if (hasFirst && hasSecond) return $"{first!.Trim()} · {second!.Trim()}";
            if (hasFirst) return first!.Trim();
            return hasSecond ? second!.Trim() : string.Empty;
        }

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
