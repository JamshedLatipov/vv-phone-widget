using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the in-call lead panel's decision logic. The regression these exist
    /// to stop: showing «Создать лид» when we cannot prove a create would succeed.
    /// A failed lookup, an `unavailable` state, and «no lead but no leads:create»
    /// each look like "no lead" if you only check whether the lead is null, and
    /// each would put back the 409/403 this feature removes.
    /// </summary>
    public class LeadCallPanelPresenterTests
    {
        private static LeadCallContextResult Loaded(
            string leadState,
            bool canCreateLead = false,
            LeadCallSummary? lead = null,
            LeadCallOwner? owner = null,
            bool canTransfer = false,
            string? blockedReason = null,
            bool canOpen = false,
            bool canComment = false) =>
            LeadCallContextResult.Loaded(new LeadCallContext
            {
                LeadState = leadState,
                Lead = lead,
                Owner = owner,
                Actions = new LeadCallActions
                {
                    CanCreateLead = canCreateLead,
                    CanOpenLead = canOpen,
                    CanTransferToOwner = canTransfer,
                    CanComment = canComment,
                    TransferBlockedReason = blockedReason,
                },
            });

        private static LeadCallSummary SampleLead(string? stageName = "Квалификация") =>
            new LeadCallSummary
            {
                Id = 4821,
                Name = "Иванов Иван",
                Status = "contacted",
                StageName = stageName,
                Url = "https://crm.proffi.io/leads/4821",
            };

        // ── State selection ──────────────────────────────────────────────────

        [Fact]
        public void NoResultYet_IsLoading()
        {
            Assert.Equal(LeadPanelState.Loading, LeadCallPanelPresenter.SelectState(null));
        }

        [Fact]
        public void FailedLookup_IsUnavailable_NotNoLead()
        {
            var state = LeadCallPanelPresenter.SelectState(
                LeadCallContextResult.Failed("HTTP 500"));

            Assert.Equal(LeadPanelState.Unavailable, state);
        }

        [Fact]
        public void ActiveLead_ShowsTheCard()
        {
            var state = LeadCallPanelPresenter.SelectState(
                Loaded(LeadCallStates.Active, lead: SampleLead()));

            Assert.Equal(LeadPanelState.ActiveLead, state);
            Assert.True(LeadCallPanelPresenter.ShowsLeadCard(state));
        }

        [Fact]
        public void LeadCard_AppearsOnlyForTheTwoCardStates()
        {
            Assert.True(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.ActiveLead));
            Assert.True(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.ConflictLead));

            Assert.False(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.Loading));
            Assert.False(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.OfferCreate));
            Assert.False(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.Hidden));
            Assert.False(LeadCallPanelPresenter.ShowsLeadCard(LeadPanelState.Unavailable));
        }

        [Fact]
        public void NoLeadWithPermission_OffersCreate()
        {
            var state = LeadCallPanelPresenter.SelectState(
                Loaded(LeadCallStates.None, canCreateLead: true));

            Assert.Equal(LeadPanelState.OfferCreate, state);
        }

        [Fact]
        public void NoLeadWithoutPermission_IsHidden()
        {
            var state = LeadCallPanelPresenter.SelectState(
                Loaded(LeadCallStates.None, canCreateLead: false));

            Assert.Equal(LeadPanelState.Hidden, state);
        }

        [Fact]
        public void UnavailableState_IsUnavailable()
        {
            Assert.Equal(
                LeadPanelState.Unavailable,
                LeadCallPanelPresenter.SelectState(Loaded(LeadCallStates.Unavailable)));
        }

        [Fact]
        public void UnknownState_IsUnavailable()
        {
            Assert.Equal(
                LeadPanelState.Unavailable,
                LeadCallPanelPresenter.SelectState(Loaded("quantum")));
        }

        /// <summary>The server saying `active` while sending no lead must not paint
        /// an empty card.</summary>
        [Fact]
        public void ActiveWithoutLead_IsUnavailable()
        {
            Assert.Equal(
                LeadPanelState.Unavailable,
                LeadCallPanelPresenter.SelectState(Loaded(LeadCallStates.Active, lead: null)));
        }

        // ── The create button: the invariant this feature exists for ─────────

        /// <summary>
        /// The button is withheld ONLY where we positively know it does not belong.
        /// Anything else keeps it: hiding it on a lookup failure would remove a
        /// capability operators already have, and the desktop ships separately from
        /// the CRM, so a widget deployed first would otherwise strand everyone.
        /// </summary>
        [Fact]
        public void CreateButton_IsWithheldOnlyWhereWeKnowItDoesNotBelong()
        {
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.ActiveLead));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.ConflictLead));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Hidden));

            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.OfferCreate));
            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Unavailable));
            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Loading));
        }

        /// <summary>
        /// The capability survives every way the lookup can fail — a 403 on a role
        /// that cannot reach call-context, a network error, or a CRM that has not
        /// been deployed with the route yet. An optimistic create is safe now: a
        /// duplicate comes back 409 and the panel renders the existing lead.
        /// </summary>
        [Fact]
        public void FailedLookup_KeepsTheCreateButton()
        {
            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(LeadCallContextResult.Failed("HTTP 403"))));

            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(Loaded(LeadCallStates.Unavailable))));

            // Still loading: the lookup can be slow, and that is the worst moment
            // to have no button.
            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(null)));
        }

        [Fact]
        public void NoLeadWithoutCreatePermission_NeverShowsCreateButton()
        {
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(
                    Loaded(LeadCallStates.None, canCreateLead: false))));
        }

        // ── Transfer ─────────────────────────────────────────────────────────

        private static LeadCallOwner Owner(string? extension = "1042") =>
            new LeadCallOwner { UserId = 42, FullName = "Петров Пётр", ExtensionNumber = extension };

        [Fact]
        public void Transfer_AllowedWhenServerSaysSoAndOwnerHasExtension()
        {
            var context = Loaded(
                LeadCallStates.Active, lead: SampleLead(), owner: Owner(), canTransfer: true).Context;

            Assert.True(LeadCallPanelPresenter.CanTransferToOwner(context));
        }

        [Fact]
        public void Transfer_BlockedWhenServerSaysNo()
        {
            var context = Loaded(
                LeadCallStates.Active, lead: SampleLead(), owner: Owner(),
                canTransfer: false, blockedReason: TransferBlockedReasons.OnCall).Context;

            Assert.False(LeadCallPanelPresenter.CanTransferToOwner(context));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Transfer_BlockedWithoutADialableExtension(string? extension)
        {
            var context = Loaded(
                LeadCallStates.Active, lead: SampleLead(), owner: Owner(extension),
                canTransfer: true).Context;

            Assert.False(LeadCallPanelPresenter.CanTransferToOwner(context));
        }

        [Fact]
        public void Transfer_BlockedWithoutAnActiveLead()
        {
            Assert.False(LeadCallPanelPresenter.CanTransferToOwner(null));

            var noLead = Loaded(LeadCallStates.None, canCreateLead: true).Context;
            Assert.False(LeadCallPanelPresenter.CanTransferToOwner(noLead));
        }

        [Fact]
        public void TransferBlockedKey_MapsEveryServerReason()
        {
            Assert.Equal("LeadTransferBlockedNoOwner",
                LeadCallPanelPresenter.TransferBlockedKey(TransferBlockedReasons.NoOwner));
            Assert.Equal("LeadTransferBlockedNoExtension",
                LeadCallPanelPresenter.TransferBlockedKey(TransferBlockedReasons.NoExtension));
            Assert.Equal("LeadTransferBlockedNotRegistered",
                LeadCallPanelPresenter.TransferBlockedKey(TransferBlockedReasons.NotRegistered));
            Assert.Equal("LeadTransferBlockedOnCall",
                LeadCallPanelPresenter.TransferBlockedKey(TransferBlockedReasons.OnCall));
            Assert.Equal("LeadTransferBlockedPaused",
                LeadCallPanelPresenter.TransferBlockedKey(TransferBlockedReasons.Paused));
        }

        [Fact]
        public void TransferBlockedKey_IsNullWhenNotBlocked()
        {
            Assert.Null(LeadCallPanelPresenter.TransferBlockedKey(null));
        }

        /// <summary>
        /// The view's slot: it only reaches here when the transfer IS blocked, so a
        /// server that named no reason must still yield a message. Distinct from
        /// TransferBlockedKey(null), which means «not blocked».
        /// </summary>
        [Fact]
        public void TransferBlockedKeyOrDefault_NeverReturnsNull()
        {
            Assert.Equal(
                LeadCallPanelPresenter.TransferBlockedUnknownKey,
                LeadCallPanelPresenter.TransferBlockedKeyOrDefault(null));

            Assert.Equal(
                LeadCallPanelPresenter.TransferBlockedUnknownKey,
                LeadCallPanelPresenter.TransferBlockedKeyOrDefault("something_new"));

            // A real reason still comes through unchanged.
            Assert.Equal(
                "LeadTransferBlockedOnCall",
                LeadCallPanelPresenter.TransferBlockedKeyOrDefault(TransferBlockedReasons.OnCall));
        }

        /// <summary>A reason added server-side later must still say something —
        /// a disabled button with no explanation is worse than a generic one.</summary>
        [Fact]
        public void TransferBlockedKey_FallsBackForUnknownReason()
        {
            Assert.Equal(
                LeadCallPanelPresenter.TransferBlockedUnknownKey,
                LeadCallPanelPresenter.TransferBlockedKey("supervisor_ate_the_phone"));
        }

        // ── Comment confirmation ─────────────────────────────────────────────

        /// <summary>
        /// The call-link step can fail (a non-2xx from /cdr/channel-uniqueid or
        /// /cdr/log) while the comment itself still saves. That must not read as a
        /// failure — nor as an unqualified success, since the recording link is
        /// exactly what the attribution is for.
        /// </summary>
        [Fact]
        public void CommentStatus_DistinguishesSavedWithAndWithoutACallLink()
        {
            Assert.Equal("LeadPanelCommentSaved",
                LeadCallPanelPresenter.CommentStatusKey(saved: true, linkedToCall: true));

            Assert.Equal("LeadPanelCommentSavedNoLink",
                LeadCallPanelPresenter.CommentStatusKey(saved: true, linkedToCall: false));
        }

        [Fact]
        public void CommentStatus_IsAFailureOnlyWhenTheCommentItselfFailed()
        {
            Assert.Equal("LeadPanelCommentFailed",
                LeadCallPanelPresenter.CommentStatusKey(saved: false, linkedToCall: false));

            // A link with no saved comment cannot happen, but must still not claim
            // success.
            Assert.Equal("LeadPanelCommentFailed",
                LeadCallPanelPresenter.CommentStatusKey(saved: false, linkedToCall: true));
        }

        // ── Card text ────────────────────────────────────────────────────────

        [Fact]
        public void LeadHeadline_IsIdAndName()
        {
            Assert.Equal("#4821 · Иванов Иван", LeadCallPanelPresenter.LeadHeadline(SampleLead()));
        }

        [Fact]
        public void LeadSubline_AppendsStageOnlyWhenPresent()
        {
            Assert.Equal("Контакт установлен · Квалификация",
                LeadCallPanelPresenter.LeadSubline("Контакт установлен", "Квалификация"));
            Assert.Equal("Контакт установлен",
                LeadCallPanelPresenter.LeadSubline("Контакт установлен", null));
            Assert.Equal("Контакт установлен",
                LeadCallPanelPresenter.LeadSubline("Контакт установлен", "  "));
            Assert.Equal("Квалификация",
                LeadCallPanelPresenter.LeadSubline(null, "Квалификация"));
            Assert.Equal("", LeadCallPanelPresenter.LeadSubline(null, null));
        }

        [Fact]
        public void LeadHeadline_OmitsTheSeparatorWhenTheNameIsMissing()
        {
            var unnamed = SampleLead();
            unnamed.Name = "";

            Assert.Equal("#4821", LeadCallPanelPresenter.LeadHeadline(unnamed));
        }

        // ── Lead status labels ───────────────────────────────────────────────

        [Theory]
        [InlineData("new", "LeadStatusNew")]
        [InlineData("contacted", "LeadStatusContacted")]
        [InlineData("qualified", "LeadStatusQualified")]
        [InlineData("proposal_sent", "LeadStatusProposalSent")]
        [InlineData("negotiating", "LeadStatusNegotiating")]
        [InlineData("converted", "LeadStatusConverted")]
        [InlineData("rejected", "LeadStatusRejected")]
        [InlineData("lost", "LeadStatusLost")]
        [InlineData("overdue", "LeadStatusOverdue")]
        [InlineData("stalled", "LeadStatusStalled")]
        [InlineData("no_answer", "LeadStatusNoAnswer")]
        public void LeadStatusKey_MapsEveryBackendStatus(string status, string expectedKey)
        {
            Assert.Equal(expectedKey, LeadCallPanelPresenter.LeadStatusKey(status));
        }

        /// <summary>Unknown status → null, so the caller shows the raw value rather
        /// than a blank, exactly as the CRM's statusLabel() falls back.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("brand_new_status")]
        [InlineData("NEW")]
        public void LeadStatusKey_IsNullForAnythingElse(string? status)
        {
            Assert.Null(LeadCallPanelPresenter.LeadStatusKey(status));
        }

        // ── Conflict card ────────────────────────────────────────────────────

        [Fact]
        public void ConflictHeadline_JoinsIdAndName()
        {
            Assert.Equal("#4821 · Иванов Иван",
                LeadCallPanelPresenter.ConflictHeadline(4821, "Иванов Иван", "сообщение"));
        }

        /// <summary>`existingLeadName` really is nullable on the backend
        /// (`existing?.name ?? null`) — «#4821 ·» with a dangling separator is a bug.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ConflictHeadline_OmitsTheSeparatorWithoutAName(string? name)
        {
            Assert.Equal("#4821", LeadCallPanelPresenter.ConflictHeadline(4821, name, "сообщение"));
        }

        [Fact]
        public void ConflictHeadline_FallsBackToTheMessageWithoutAnId()
        {
            Assert.Equal("У клиента уже есть открытый лид",
                LeadCallPanelPresenter.ConflictHeadline(null, null, "У клиента уже есть открытый лид"));
            Assert.Equal("", LeadCallPanelPresenter.ConflictHeadline(null, null, null));
        }

        // ── A conflict card outranks any later refresh ───────────────────────

        /// <summary>
        /// The 409 PROVED the lead exists. A refresh that fails must not replace the
        /// card with «не удалось проверить», and a refresh saying `none` must not
        /// re-offer the create the 409 just refused.
        /// </summary>
        [Fact]
        public void ConflictCard_SurvivesAFailedRefresh()
        {
            var state = LeadCallPanelPresenter.SelectState(
                "992900112233", LeadCallContextResult.Failed("HTTP 500"), conflictShown: true);

            Assert.Equal(LeadPanelState.ConflictLead, state);
            Assert.True(LeadCallPanelPresenter.ShowsLeadCard(state));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(state));
        }

        [Fact]
        public void ConflictCard_SurvivesARefreshThatSaysNoLead()
        {
            var state = LeadCallPanelPresenter.SelectState(
                "992900112233",
                Loaded(LeadCallStates.None, canCreateLead: true),
                conflictShown: true);

            Assert.Equal(LeadPanelState.ConflictLead, state);
            Assert.True(LeadCallPanelPresenter.ShowsLeadCard(state));
            // The create button must NOT come back — this is the outcome that
            // previously returned a permanently disabled button.
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(state));
        }

        [Fact]
        public void ConflictCard_SurvivesAnUnavailableRefreshAndAStillLoadingOne()
        {
            Assert.Equal(LeadPanelState.ConflictLead, LeadCallPanelPresenter.SelectState(
                "992900112233", Loaded(LeadCallStates.Unavailable), conflictShown: true));

            Assert.Equal(LeadPanelState.ConflictLead, LeadCallPanelPresenter.SelectState(
                "992900112233", null, conflictShown: true));
        }

        /// <summary>The one thing a refresh MAY do: upgrade to the full card, which
        /// is what brings the owner, transfer and comment box in.</summary>
        [Fact]
        public void ConflictCard_IsUpgradedByAFullRefresh()
        {
            var state = LeadCallPanelPresenter.SelectState(
                "992900112233",
                Loaded(LeadCallStates.Active, lead: SampleLead(), owner: Owner(), canTransfer: true),
                conflictShown: true);

            Assert.Equal(LeadPanelState.ActiveLead, state);
        }

        [Fact]
        public void WithoutAConflict_TheRefreshDecidesNormally()
        {
            Assert.Equal(LeadPanelState.OfferCreate, LeadCallPanelPresenter.SelectState(
                "992900112233", Loaded(LeadCallStates.None, canCreateLead: true), conflictShown: false));

            Assert.Equal(LeadPanelState.Unavailable, LeadCallPanelPresenter.SelectState(
                "992900112233", LeadCallContextResult.Failed("boom"), conflictShown: false));
        }

        // ── Withheld / anonymous caller number ───────────────────────────────

        [Theory]
        [InlineData("992900112233")]
        [InlineData("100")]
        [InlineData("+992 90 011-22-33")]
        [InlineData("anonymous1")]
        public void LookupablePhone_AcceptsAnythingWithADigit(string phone)
        {
            Assert.True(LeadCallPanelPresenter.IsLookupablePhone(phone));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("anonymous")]
        [InlineData("Неизвестный")]
        public void LookupablePhone_RejectsWhatTheEndpointWould400(string? phone)
        {
            Assert.False(LeadCallPanelPresenter.IsLookupablePhone(phone));
        }

        /// <summary>A withheld number renders nothing — not an orange «не удалось
        /// проверить» with a «Повторить» that can never succeed.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("anonymous")]
        public void WithheldNumber_RendersNothing(string? phone)
        {
            var state = LeadCallPanelPresenter.SelectState(phone, null, conflictShown: false);

            Assert.Equal(LeadPanelState.Hidden, state);
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(state));
            Assert.False(LeadCallPanelPresenter.ShowsLeadCard(state));
        }

        // ── Per-call cache key ───────────────────────────────────────────────

        /// <summary>
        /// A call-back from the same number is a DIFFERENT call: reusing its cached
        /// «no lead» would offer a create for a caller who has since acquired one.
        /// </summary>
        [Fact]
        public void CallKey_DistinguishesTwoCallsFromTheSameNumber()
        {
            var first = LeadCallPanelPresenter.BuildCallKey(
                "992900112233", new DateTime(2026, 8, 4, 10, 0, 0));
            var second = LeadCallPanelPresenter.BuildCallKey(
                "992900112233", new DateTime(2026, 8, 4, 10, 2, 0));

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void CallKey_IsStableWithinOneCall()
        {
            var startedAt = new DateTime(2026, 8, 4, 10, 0, 0);

            Assert.Equal(
                LeadCallPanelPresenter.BuildCallKey("992900112233", startedAt),
                LeadCallPanelPresenter.BuildCallKey("992900112233", startedAt));
        }

        [Fact]
        public void CallKey_DistinguishesDifferentNumbers()
        {
            var startedAt = new DateTime(2026, 8, 4, 10, 0, 0);

            Assert.NotEqual(
                LeadCallPanelPresenter.BuildCallKey("992900112233", startedAt),
                LeadCallPanelPresenter.BuildCallKey("992900445566", startedAt));
        }

        /// <summary>
        /// SipService leaves ActiveCallStartedAt null before answer and resets it on
        /// hangup, while ActiveCallerId is never cleared. A placeholder key would
        /// therefore be SHARED between a pre-answer view and a later call to the
        /// same number, handing over a stale «no lead» and a stale comment draft.
        /// No timestamp means no key at all, and no caching.
        /// </summary>
        [Fact]
        public void CallKey_IsNullWithoutAStartTime()
        {
            Assert.Null(LeadCallPanelPresenter.BuildCallKey("992900112233", null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void CallKey_IsNullWithoutANumber(string? number)
        {
            Assert.Null(LeadCallPanelPresenter.BuildCallKey(
                number, new DateTime(2026, 8, 4, 10, 0, 0)));
        }

        // ── URL launch guard ─────────────────────────────────────────────────

        [Theory]
        [InlineData("https://crm.proffi.io/leads/4821")]
        [InlineData("http://localhost:4200/leads/1")]
        public void LaunchableUrl_AcceptsHttpAndHttps(string url)
        {
            Assert.True(LeadCallPanelPresenter.IsLaunchableUrl(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("/leads/4821")]
        [InlineData("crm.proffi.io/leads/4821")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("ftp://example.com/x")]
        [InlineData(@"\\server\share\evil.exe")]
        public void LaunchableUrl_RefusesEverythingElse(string? url)
        {
            Assert.False(LeadCallPanelPresenter.IsLaunchableUrl(url));
        }
    }
}
