using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class StatusState
    {
        [JsonPropertyName("paused")]
        public bool Paused { get; set; }

        [JsonPropertyName("reason_paused")]
        public string? ReasonPaused { get; set; }
    }

    public class PauseRequest
    {
        [JsonPropertyName("paused")]
        public bool Paused { get; set; }

        [JsonPropertyName("reason_paused")]
        public string? ReasonPaused { get; set; }
    }
}
