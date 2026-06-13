using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    public class FlowDefinition
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("activeVersionId")]
        public string? ActiveVersionId { get; set; }
    }

    public class FlowNodeOption
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    public class FlowNode
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("script")]
        public string? Script { get; set; }

        [JsonPropertyName("answerType")]
        public string? AnswerType { get; set; }

        [JsonPropertyName("options")]
        public List<FlowNodeOption>? Options { get; set; }

        [JsonPropertyName("required")]
        public bool? Required { get; set; }

        [JsonPropertyName("allowComment")]
        public bool? AllowComment { get; set; }
    }

    public class FlowGraph
    {
        [JsonPropertyName("startNodeKey")]
        public string? StartNodeKey { get; set; }

        [JsonPropertyName("nodes")]
        public List<FlowNode>? Nodes { get; set; }

        // edges ignored intentionally — server drives navigation
        [JsonPropertyName("edges")]
        public JsonElement? Edges { get; set; }
    }

    public class FlowRun
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("flowId")]
        public string? FlowId { get; set; }

        [JsonPropertyName("subjectType")]
        public string? SubjectType { get; set; }

        [JsonPropertyName("subjectId")]
        public string? SubjectId { get; set; }

        [JsonPropertyName("context")]
        public JsonElement? Context { get; set; }
    }

    public class StartRunResponse
    {
        [JsonPropertyName("run")]
        public FlowRun? Run { get; set; }

        [JsonPropertyName("graph")]
        public FlowGraph? Graph { get; set; }

        [JsonPropertyName("context")]
        public JsonElement? Context { get; set; }

        [JsonPropertyName("currentNodeKey")]
        public string? CurrentNodeKey { get; set; }
    }

    public class AnswerResponse
    {
        [JsonPropertyName("verificationResult")]
        public string? VerificationResult { get; set; }

        [JsonPropertyName("nextNodeKey")]
        public string? NextNodeKey { get; set; }

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }

        [JsonPropertyName("verdict")]
        public JsonElement? Verdict { get; set; }
    }

    public class RunAnswerRow
    {
        [JsonPropertyName("nodeKey")]
        public string? NodeKey { get; set; }

        [JsonPropertyName("value")]
        public JsonElement? Value { get; set; }

        [JsonPropertyName("supersededAt")]
        public string? SupersededAt { get; set; }
    }

    public class RunStateResponse
    {
        [JsonPropertyName("run")]
        public FlowRun? Run { get; set; }

        [JsonPropertyName("graph")]
        public FlowGraph? Graph { get; set; }

        [JsonPropertyName("answers")]
        public List<RunAnswerRow>? Answers { get; set; }

        [JsonPropertyName("currentNodeKey")]
        public string? CurrentNodeKey { get; set; }
    }
}
