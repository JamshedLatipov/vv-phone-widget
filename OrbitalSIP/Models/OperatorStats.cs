using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class OperatorStats
    {
        [JsonPropertyName("totalCalls")]
        public int TotalCalls { get; set; }

        [JsonPropertyName("answeredCalls")]
        public int AnsweredCalls { get; set; }

        [JsonPropertyName("missedCalls")]
        public int MissedCalls { get; set; }

        [JsonPropertyName("incomingCalls")]
        public int IncomingCalls { get; set; }

        [JsonPropertyName("incomingAnswered")]
        public int IncomingAnswered { get; set; }

        [JsonPropertyName("outgoingCalls")]
        public int OutgoingCalls { get; set; }
        [JsonPropertyName("outgoingAnswered")]
        public int OutgoingAnswered { get; set; }


        [JsonPropertyName("avgDuration")]
        public int AvgDuration { get; set; }

        [JsonPropertyName("totalTalkTime")]
        public int TotalTalkTime { get; set; }
    }

    public class OperatorDetailsResponse
    {
        [JsonPropertyName("stats")]
        public OperatorStats? Stats { get; set; }
    }
}
