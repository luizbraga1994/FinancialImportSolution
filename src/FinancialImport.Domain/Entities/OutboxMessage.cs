namespace FinancialImport.Domain.Entities;

/// <summary>
/// Transactional outbox record. Persisted in the same unit of work as
/// the business change that produced it, then asynchronously published
/// to the broker by the OutboxDispatcher background worker.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    /// <summary>Logical channel (topic/queue name) used by the dispatcher.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Fully qualified CLR type of the payload for deserialization.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Unique envelope id — used for broker-side deduplication.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Correlation id copied from the ambient CorrelationContext.</summary>
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }

    /// <summary>JSON-serialized envelope ready to be published.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Broker family: "rabbitmq" for commands, "kafka" for integration events.
    /// Keeping it explicit avoids rescanning the channel name at dispatch time.
    /// </summary>
    public string Broker { get; set; } = string.Empty;

    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? ClaimedUntilUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public long? UserId { get; set; }
    public string? CompanyDb { get; set; }
}

public enum OutboxMessageStatus
{
    Pending = 0,
    InFlight = 1,
    Dispatched = 2,
    Failed = 3,
    DeadLettered = 4
}
