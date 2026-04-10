namespace FinancialImport.Domain.Entities;

/// <summary>
/// Per-consumer deduplication record. A unique index on
/// (Consumer, MessageId) prevents the same message from being processed
/// twice by the same handler, even if the broker redelivers it.
/// </summary>
public sealed class InboxMessage
{
    public long Id { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}
