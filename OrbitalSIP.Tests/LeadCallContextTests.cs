using System.Text.Json;
using OrbitalSIP.Models;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the wire contract for GET /api/leads/call-context. The regression
    /// these exist to stop: `none` and `unavailable` BOTH carry `lead: null`, so a
    /// widget that branches on the lead being null offers «Создать лид» on a number
    /// that provably has an open one — the 409 this whole feature removes. The
    /// discriminator is `leadState`, and these assert it survives the wire.
    /// </summary>
    public class LeadCallContextTests
    {
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static LeadCallContext Parse(string json) =>
            JsonSerializer.Deserialize<LeadCallContext>(json, ReadOptions)!;

        [Fact]
        public void ActiveState_CarriesLeadOwnerAndActions()
        {
            var context = Parse("""
            {
              "contactId": "3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11",
              "leadState": "active",
              "lead": {
                "id": 4821,
                "name": "Иванов Иван",
                "status": "contacted",
                "stageName": "Квалификация",
                "createdAt": "2026-08-01T09:15:00.000Z",
                "url": "https://crm.proffi.io/leads/4821"
              },
              "owner": {
                "userId": 42,
                "fullName": "Петров Пётр",
                "extensionNumber": "1042",
                "sipRegistered": true,
                "onCall": false,
                "manualStatus": null,
                "supervisorPaused": false
              },
              "actions": {
                "canCreateLead": false,
                "canOpenLead": true,
                "canTransferToOwner": true,
                "canComment": true,
                "transferBlockedReason": null
              }
            }
            """);

            Assert.Equal(LeadCallStates.Active, context.LeadState);
            Assert.True(context.HasActiveLead);
            Assert.False(context.HasNoLead);
            Assert.False(context.LeadLookupUnavailable);

            Assert.Equal("3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11", context.ContactId);

            Assert.NotNull(context.Lead);
            Assert.Equal(4821, context.Lead!.Id);
            Assert.Equal("Иванов Иван", context.Lead.Name);
            Assert.Equal("contacted", context.Lead.Status);
            Assert.Equal("Квалификация", context.Lead.StageName);
            Assert.Equal("https://crm.proffi.io/leads/4821", context.Lead.Url);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 1, 9, 15, 0, TimeSpan.Zero),
                context.Lead.CreatedAt!.Value.ToUniversalTime());

            Assert.NotNull(context.Owner);
            Assert.Equal(42, context.Owner!.UserId);
            Assert.Equal("Петров Пётр", context.Owner.FullName);
            Assert.Equal("1042", context.Owner.ExtensionNumber);
            Assert.True(context.Owner.SipRegistered);
            Assert.False(context.Owner.OnCall);
            Assert.Null(context.Owner.ManualStatus);
            Assert.False(context.Owner.SupervisorPaused);

            Assert.False(context.Actions.CanCreateLead);
            Assert.True(context.Actions.CanOpenLead);
            Assert.True(context.Actions.CanTransferToOwner);
            Assert.True(context.Actions.CanComment);
            Assert.Null(context.Actions.TransferBlockedReason);
        }

        [Fact]
        public void ActiveState_CarriesBlockedTransferReasonAndSupervisorPause()
        {
            var context = Parse("""
            {
              "contactId": "3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11",
              "leadState": "active",
              "lead": {
                "id": 4821,
                "name": "Иванов Иван",
                "status": "new",
                "stageName": null,
                "createdAt": "2026-08-01T09:15:00.000Z",
                "url": "https://crm.proffi.io/leads/4821"
              },
              "owner": {
                "userId": 42,
                "fullName": "Петров Пётр",
                "extensionNumber": "1042",
                "sipRegistered": true,
                "onCall": false,
                "manualStatus": null,
                "supervisorPaused": true
              },
              "actions": {
                "canCreateLead": false,
                "canOpenLead": true,
                "canTransferToOwner": false,
                "canComment": true,
                "transferBlockedReason": "paused"
              }
            }
            """);

            Assert.True(context.HasActiveLead);
            Assert.Null(context.Lead!.StageName);
            Assert.True(context.Owner!.SupervisorPaused);
            Assert.False(context.Actions.CanTransferToOwner);
            Assert.Equal(TransferBlockedReasons.Paused, context.Actions.TransferBlockedReason);
        }

        [Fact]
        public void NoneState_HasNullLeadAndOffersCreate()
        {
            var context = Parse("""
            {
              "contactId": null,
              "leadState": "none",
              "lead": null,
              "owner": null,
              "actions": {
                "canCreateLead": true,
                "canOpenLead": false,
                "canTransferToOwner": false,
                "canComment": false,
                "transferBlockedReason": null
              }
            }
            """);

            Assert.Equal(LeadCallStates.None, context.LeadState);
            Assert.False(context.HasActiveLead);
            Assert.True(context.HasNoLead);
            Assert.False(context.LeadLookupUnavailable);

            Assert.Null(context.ContactId);
            Assert.Null(context.Lead);
            Assert.Null(context.Owner);
            Assert.True(context.Actions.CanCreateLead);
        }

        /// <summary>
        /// The КЦ case: cc_operator/cc_manager hold `lead-call:read` but not
        /// `leads:create`, so the server says «no lead» AND «you may not create
        /// one». The two are independent — the create button hangs off the action,
        /// not off the state.
        /// </summary>
        [Fact]
        public void NoneState_WithoutCreatePermission_ForbidsCreate()
        {
            var context = Parse("""
            {
              "contactId": null,
              "leadState": "none",
              "lead": null,
              "owner": null,
              "actions": {
                "canCreateLead": false,
                "canOpenLead": false,
                "canTransferToOwner": false,
                "canComment": false,
                "transferBlockedReason": null
              }
            }
            """);

            Assert.True(context.HasNoLead);
            Assert.False(context.Actions.CanCreateLead);
        }

        [Fact]
        public void UnavailableState_HasNullLeadButIsNotNone()
        {
            var context = Parse("""
            {
              "contactId": "3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11",
              "leadState": "unavailable",
              "lead": null,
              "owner": null,
              "actions": {
                "canCreateLead": false,
                "canOpenLead": false,
                "canTransferToOwner": false,
                "canComment": false,
                "transferBlockedReason": null
              }
            }
            """);

            Assert.Equal(LeadCallStates.Unavailable, context.LeadState);
            Assert.False(context.HasActiveLead);
            // The whole point: lead is null here too, but this is NOT «no lead».
            Assert.Null(context.Lead);
            Assert.False(context.HasNoLead);
            Assert.True(context.LeadLookupUnavailable);
            Assert.False(context.Actions.CanCreateLead);
        }

        /// <summary>A state this build does not know must land in the fail-closed
        /// residual, never in «no lead».</summary>
        [Fact]
        public void UnknownState_FailsClosed()
        {
            var context = Parse("""
            { "contactId": null, "leadState": "quantum", "lead": null, "owner": null,
              "actions": { "canCreateLead": false, "canOpenLead": false,
                           "canTransferToOwner": false, "canComment": false,
                           "transferBlockedReason": null } }
            """);

            Assert.False(context.HasNoLead);
            Assert.False(context.HasActiveLead);
            Assert.True(context.LeadLookupUnavailable);
        }

        /// <summary>A body missing `leadState` entirely (contract drift) must not
        /// read as «no lead» — the default is the fail-closed state.</summary>
        [Fact]
        public void MissingState_FailsClosed()
        {
            var context = Parse("""{ "contactId": null, "lead": null, "owner": null }""");

            Assert.Equal(LeadCallStates.Unavailable, context.LeadState);
            Assert.False(context.HasNoLead);
            Assert.True(context.LeadLookupUnavailable);
            Assert.NotNull(context.Actions);
            Assert.False(context.Actions.CanCreateLead);
        }

        /// <summary>`active` with a null lead is a server bug, but it must not read
        /// as an active lead the UI can render.</summary>
        [Fact]
        public void ActiveStateWithoutLead_IsNotTreatedAsActive()
        {
            var context = Parse("""{ "leadState": "active", "lead": null }""");

            Assert.False(context.HasActiveLead);
            Assert.False(context.HasNoLead);
            Assert.True(context.LeadLookupUnavailable);
        }
    }
}
