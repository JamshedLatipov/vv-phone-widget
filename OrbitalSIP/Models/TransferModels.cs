using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    /// <summary>Оператор, которому можно отдать звонок. `Extension` — набираемый номер.</summary>
    public sealed record TransferOperatorTarget(
        [property: JsonPropertyName("extension")] string Extension,
        [property: JsonPropertyName("fullName")] string FullName);

    /// <summary>
    /// Очередь. <c>DisabledReason</c> непустой означает, что бэк её нашёл, но
    /// перевести туда нельзя — например, имя очереди не проходит по проводу
    /// АТС. Такую строку показываем серой, а не прячем: короткий список без
    /// объяснения читается как «очередей нет».
    /// </summary>
    public sealed record TransferQueueTarget(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("disabledReason")] string? DisabledReason);

    public sealed record TransferTargets(
        [property: JsonPropertyName("operators")] IReadOnlyList<TransferOperatorTarget> Operators,
        [property: JsonPropertyName("queues")] IReadOnlyList<TransferQueueTarget> Queues);

    /// <summary>Что именно оператор выбрал. Определяет, каким путём уйдёт перевод.</summary>
    public enum TransferTargetKind
    {
        /// <summary>Номер оператора. При недоступном бэке уходит локальным SIP REFER.</summary>
        Extension,

        /// <summary>Имя очереди. Локального фолбэка нет — REFER на имя очереди уедет в никуда.</summary>
        Queue,
    }

    /// <summary>
    /// Исход попытки перевода. <c>ChannelUnresolved</c> отделён от <c>Failed</c>
    /// намеренно: именно он, и только он, включает SIP-фолбэк для оператора.
    /// </summary>
    public enum TransferOutcome
    {
        Ok,
        Failed,
        ChannelUnresolved,
    }

    public sealed record TransferResult(TransferOutcome Outcome, string? Error);

    /// <summary>
    /// Что оператор выбрал и по какому номеру бэк найдёт живой канал.
    /// `CallerNumber` — номер второй стороны; по нему резолвится channelId.
    /// </summary>
    public sealed record TransferRequest(TransferTargetKind Kind, string Value, string CallerNumber);
}
