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
}
