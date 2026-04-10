namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Generic envelope for every message (command or event) that flows
/// through the messaging infrastructure. Keeps protocol-level concerns
/// (correlation, idempotency, versioning) out of the payload contracts.
/// </summary>
public sealed class MessageEnvelope<TPayload> where TPayload : class
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString("N");
    public string MessageType { get; init; } = typeof(TPayload).FullName!;
    public string SchemaVersion { get; init; } = "1";

    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? CausationId { get; init; }
    public long? UserId { get; init; }
    public string? CompanyDb { get; init; }

    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    public TPayload Payload { get; init; } = default!;

    /// <summary>
    /// Number of delivery attempts so far. Incremented by consumers when
    /// a retry is scheduled via the retry/DLQ pipeline.
    /// </summary>
    public int AttemptCount { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new();
}

/// <summary>
/// Non-generic base for publisher APIs that want to accept any envelope.
/// </summary>
public interface IHasMessageMetadata
{
    string MessageId { get; }
    string MessageType { get; }
    string CorrelationId { get; }
    string? CausationId { get; }
    DateTime OccurredAtUtc { get; }
}
