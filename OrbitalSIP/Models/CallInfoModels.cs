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
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public List<CallInfoField> Fields { get; set; } = new();
    }

    public class CallInfoField
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }
}
