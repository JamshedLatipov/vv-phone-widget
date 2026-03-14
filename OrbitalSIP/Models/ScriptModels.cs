using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class ScriptCategory
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    public class CallScript
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("categoryId")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("parentId")]
        public string? ParentId { get; set; }

        [JsonPropertyName("steps")]
        public List<string>? Steps { get; set; }

        [JsonPropertyName("questions")]
        public List<string>? Questions { get; set; }

        [JsonPropertyName("tips")]
        public List<string>? Tips { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("category")]
        public ScriptCategory? Category { get; set; }

        [JsonPropertyName("children")]
        public List<CallScript>? Children { get; set; }
    }

    public class CdrLogRequest
    {
        [JsonPropertyName("asteriskUniqueId")]
        public string? AsteriskUniqueId { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; } = "";

        [JsonPropertyName("callType")]
        public string? CallType { get; set; }

        [JsonPropertyName("scriptBranch")]
        public string? ScriptBranch { get; set; }
    }
}
