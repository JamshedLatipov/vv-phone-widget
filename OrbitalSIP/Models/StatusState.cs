using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class StatusState
    {
        [JsonPropertyName("manualStatus")]
        public string? ManualStatus { get; set; }

        [JsonPropertyName("manualReason")]
        public string? ManualReason { get; set; }

        [JsonPropertyName("chatConnected")]
        public bool ChatConnected { get; set; }

        [JsonPropertyName("supervisorPausedBy")]
        public int? SupervisorPausedBy { get; set; }

        [JsonIgnore]
        public bool IsSupervisorPaused => SupervisorPausedBy.HasValue;

        // Compat: UI code that reads .Paused keeps working.
        [JsonIgnore]
        public bool Paused => ManualStatus != null || SupervisorPausedBy.HasValue;

        // Compat: the status name doubles as the "reason" the UI already keys off.
        [JsonIgnore]
        public string? ReasonPaused => ManualStatus;

        [JsonIgnore]
        public string EffectiveStatus =>
            SupervisorPausedBy.HasValue ? "supervisor-paused" : (ManualStatus ?? "online");
    }

    public class SetPresenceRequest
    {
        [JsonPropertyName("manualStatus")]
        public string? ManualStatus { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
