using System.Text.Json;
using System.Text.Json.Serialization;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests
{
    /// <summary>
    /// Guards the 409 handling for POST /api/leads and the wire shape of the
    /// call-comment payload. Before this, a 409 was indistinguishable from any
    /// other failure, so «у клиента уже есть открытый лид» surfaced as a generic
    /// error and the widget could not show the lead that already exists.
    /// </summary>
    public class LeadServiceTests
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void AlreadyOpenConflict_ParsesExistingLeadIdAndMessage()
        {
            var result = LeadService.ParseAlreadyOpenConflict("""
            {
              "statusCode": 409,
              "message": "У клиента уже есть открытый лид",
              "error": "LEAD_ALREADY_OPEN",
              "errors": {
                "existingLeadId": 4821,
                "existingLeadName": "Иванов Иван",
                "status": "contacted",
                "assignedTo": "42"
              }
            }
            """);

            Assert.NotNull(result);
            Assert.True(result!.AlreadyOpen);
            Assert.False(result.Success);
            Assert.Equal(4821, result.ExistingLeadId);
            Assert.Equal("У клиента уже есть открытый лид", result.Message);
        }

        /// <summary>A 409 from some other unique constraint must keep taking the
        /// generic failure path — log + error banner — not be shown as «lead
        /// already open» pointing at nothing.</summary>
        [Fact]
        public void OtherConflict_IsNotTreatedAsAlreadyOpen()
        {
            var result = LeadService.ParseAlreadyOpenConflict("""
            {
              "statusCode": 409,
              "message": "Запись с такими данными уже существует",
              "error": "Conflict",
              "errors": { "existingLeadId": 4821 }
            }
            """);

            Assert.Null(result);
        }

        [Fact]
        public void ConflictWithoutErrorField_IsNotTreatedAsAlreadyOpen()
        {
            Assert.Null(LeadService.ParseAlreadyOpenConflict("""
            { "statusCode": 409, "message": "У клиента уже есть открытый лид" }
            """));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<html><body>502 Bad Gateway</body></html>")]
        [InlineData("{\"error\": \"LEAD_ALREADY_OPEN\",")]
        [InlineData("[1,2,3]")]
        [InlineData("\"LEAD_ALREADY_OPEN\"")]
        [InlineData("null")]
        public void MalformedConflictBody_ReturnsNullWithoutThrowing(string body)
        {
            Assert.Null(LeadService.ParseAlreadyOpenConflict(body));
        }

        [Fact]
        public void NullConflictBody_ReturnsNullWithoutThrowing()
        {
            Assert.Null(LeadService.ParseAlreadyOpenConflict(null));
        }

        /// <summary>The conflict is still a conflict when the structured payload is
        /// partial: the widget must at least be able to say «уже есть открытый лид»
        /// even if it cannot deep-link to it.</summary>
        [Fact]
        public void AlreadyOpenConflict_ToleratesMissingOrOddErrorsPayload()
        {
            var noErrors = LeadService.ParseAlreadyOpenConflict("""
            { "statusCode": 409, "message": "У клиента уже есть открытый лид",
              "error": "LEAD_ALREADY_OPEN" }
            """);

            Assert.NotNull(noErrors);
            Assert.True(noErrors!.AlreadyOpen);
            Assert.Null(noErrors.ExistingLeadId);
            Assert.Equal("У клиента уже есть открытый лид", noErrors.Message);

            var stringId = LeadService.ParseAlreadyOpenConflict("""
            { "error": "LEAD_ALREADY_OPEN", "errors": { "existingLeadId": "4821" } }
            """);

            Assert.NotNull(stringId);
            Assert.True(stringId!.AlreadyOpen);
            Assert.Null(stringId.ExistingLeadId);
            Assert.Null(stringId.Message);
        }

        /// <summary>`error` is matched exactly — a lookalike must not pass.</summary>
        [Fact]
        public void SimilarErrorCode_IsNotTreatedAsAlreadyOpen()
        {
            Assert.Null(LeadService.ParseAlreadyOpenConflict(
                """{ "error": "lead_already_open" }"""));
            Assert.Null(LeadService.ParseAlreadyOpenConflict(
                """{ "error": "LEAD_ALREADY_OPENED" }"""));
        }

        [Fact]
        public void AddCallCommentRequest_UsesBackendFieldNames()
        {
            var payload = new AddCallCommentRequest
            {
                Comment = "Клиент просит перезвонить после 18:00",
                CallLogId = "3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11",
                CallUniqueId = "1754212345.678",
            };

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, WriteOptions));
            var root = doc.RootElement;

            Assert.Equal("Клиент просит перезвонить после 18:00", root.GetProperty("comment").GetString());
            Assert.Equal("3f6a2c1e-0b7d-4a9c-8e21-2b9f0d5c6a11", root.GetProperty("callLogId").GetString());
            Assert.Equal("1754212345.678", root.GetProperty("callUniqueId").GetString());
        }

        /// <summary>Unset call links are omitted, never sent as null — mirrors
        /// CreateTaskRequest, and keeps @IsUUID off an absent value.</summary>
        [Fact]
        public void AddCallCommentRequest_OmitsNullLinks()
        {
            var payload = new AddCallCommentRequest { Comment = "Без привязки к звонку" };

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, WriteOptions));
            var root = doc.RootElement;

            Assert.Equal("Без привязки к звонку", root.GetProperty("comment").GetString());
            Assert.False(root.TryGetProperty("callLogId", out _));
            Assert.False(root.TryGetProperty("callUniqueId", out _));
        }
    }
}
