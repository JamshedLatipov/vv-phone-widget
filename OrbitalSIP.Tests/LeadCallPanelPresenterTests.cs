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

        [Fact]
        public void CreateButton_AppearsOnlyForOfferCreate()
        {
            Assert.True(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.OfferCreate));

            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Loading));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.ActiveLead));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Hidden));
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(LeadPanelState.Unavailable));
        }

        /// <summary>End-to-end over the two paths that most look like "no lead".</summary>
        [Fact]
        public void FailedLookup_NeverShowsCreateButton()
        {
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(LeadCallContextResult.Failed("boom"))));

            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(
                LeadCallPanelPresenter.SelectState(Loaded(LeadCallStates.Unavailable))));

            // Still loading — we do not yet know, so we do not offer.
            Assert.False(LeadCallPanelPresenter.ShowsCreateButton(
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

        /// <summary>A reason added server-side later must still say something —
        /// a disabled button with no explanation is worse than a generic one.</summary>
        [Fact]
        public void TransferBlockedKey_FallsBackForUnknownReason()
        {
            Assert.Equal(
                LeadCallPanelPresenter.TransferBlockedUnknownKey,
                LeadCallPanelPresenter.TransferBlockedKey("supervisor_ate_the_phone"));
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
            Assert.Equal("contacted · Квалификация",
                LeadCallPanelPresenter.LeadSubline(SampleLead()));
            Assert.Equal("contacted",
                LeadCallPanelPresenter.LeadSubline(SampleLead(stageName: null)));
            Assert.Equal("contacted",
                LeadCallPanelPresenter.LeadSubline(SampleLead(stageName: "  ")));
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
