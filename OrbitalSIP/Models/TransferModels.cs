using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    /// <summary>An operator the call can be handed to. `Extension` is the dialable number.</summary>
    public sealed record TransferOperatorTarget(
        [property: JsonPropertyName("extension")] string Extension,
        [property: JsonPropertyName("fullName")] string FullName);

    /// <summary>
    /// A queue. A non-empty <c>DisabledReason</c> means the backend found the
    /// queue but a transfer to it is not possible — for example, the queue's
    /// name does not survive the trip over the PBX wire. Such a row is shown
    /// greyed out rather than hidden: a short list with no explanation reads
    /// as «no queues».
    /// </summary>
    public sealed record TransferQueueTarget(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("disabledReason")] string? DisabledReason);

    /// <summary>
    /// The transfer-targets response. Both lists are typed nullable even
    /// though the endpoint is expected to always send them: the wire can
    /// omit a key, or send it as an explicit `null`, and on this target
    /// framework System.Text.Json does not consult C#'s nullable-reference
    /// annotations during deserialization (`RespectNullableAnnotations` is a
    /// .NET 9+ option; this project targets net8.0) — so the annotation
    /// alone does not stop a non-nullable-looking `IReadOnlyList&lt;T&gt;`
    /// parameter from binding straight to a null reference.
    /// TransferTargetsPresenter (in OrbitalSIP.Services) treats a null list
    /// as empty everywhere it reads one.
    /// </summary>
    public sealed record TransferTargets(
        [property: JsonPropertyName("operators")] IReadOnlyList<TransferOperatorTarget>? Operators,
        [property: JsonPropertyName("queues")] IReadOnlyList<TransferQueueTarget>? Queues);

    /// <summary>What the operator picked. Determines which path the transfer takes.</summary>
    public enum TransferTargetKind
    {
        /// <summary>An operator's number. When the backend is unavailable, it goes out as a local SIP REFER.</summary>
        Extension,

        /// <summary>A queue name. There is no local fallback — a REFER to a queue name would go nowhere.</summary>
        Queue,
    }

    /// <summary>
    /// The outcome of a transfer attempt. <c>ChannelUnresolved</c> is kept
    /// separate from <c>Failed</c> deliberately: it, and only it, is what
    /// turns on the SIP fallback for an operator target.
    /// </summary>
    public enum TransferOutcome
    {
        Ok,
        Failed,
        ChannelUnresolved,
    }

    public sealed record TransferResult(TransferOutcome Outcome, string? Error);

    /// <summary>
    /// What the operator picked, and the number the backend uses to resolve
    /// a live channel. `CallerNumber` is the other party's number; the
    /// channelId is resolved from it.
    /// </summary>
    public sealed record TransferRequest(TransferTargetKind Kind, string Value, string CallerNumber);
}
