using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class CdrEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("calldate")]
        public DateTime CallDate { get; set; }

        [JsonPropertyName("clid")]
        public string? Clid { get; set; }

        [JsonPropertyName("src")]
        public string? Src { get; set; }

        [JsonPropertyName("dst")]
        public string? Dst { get; set; }

        [JsonPropertyName("duration")]
        public int Duration { get; set; }

        [JsonPropertyName("billsec")]
        public int BillSec { get; set; }

        [JsonPropertyName("disposition")]
        public string? Disposition { get; set; }

        [JsonPropertyName("direction")]
        public string? Direction { get; set; }

        [JsonPropertyName("caller")]
        public string? Caller { get; set; }

        [JsonPropertyName("destination")]
        public string? Destination { get; set; }

        [JsonPropertyName("uniqueid")]
        public string? UniqueId { get; set; }
    }

    public class CdrResponse
    {
        [JsonPropertyName("data")]
        public List<CdrEntry>? Data { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }
    }
}
