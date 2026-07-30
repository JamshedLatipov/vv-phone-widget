using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class CallInfoResponse
    {
        [JsonPropertyName("sections")]
        public List<CallInfoSection> Sections { get; set; } = new();
    }

    public class CallInfoSection
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("ui")]
        public CallInfoUi Ui { get; set; } = new();

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }
    }

    public class CallInfoUi
    {
        /// <summary>"details" (single object + <see cref="Fields"/>) or "table"
        /// (array of records + <see cref="Columns"/>). Mirrors SectionConfig on
        /// the backend (apps/back/.../dto/call-info.dto.ts).</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public List<CallInfoField> Fields { get; set; } = new();

        /// <summary>Column descriptors for `type: "table"` sections — loans,
        /// accounts, deposits. Without these the whole section is invisible.</summary>
        [JsonPropertyName("columns")]
        public List<CallInfoField> Columns { get; set; } = new();
    }

    public class CallInfoField
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        /// <summary>Optional value type from the integration contract:
        /// string/number/date/datetime/enum/boolean.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Optional raw-value → display-text map (e.g. loan status codes).</summary>
        [JsonPropertyName("enumMap")]
        public Dictionary<string, string>? EnumMap { get; set; }
    }
}
