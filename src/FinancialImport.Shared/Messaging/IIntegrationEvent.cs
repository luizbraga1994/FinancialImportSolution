namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Marker contract for integration events that are published to the
/// event backbone (Kafka). Integration events are append-only, immutable
/// and represent "something that happened" — they are safe to consume
/// by other bounded contexts and external systems.
/// </summary>
public interface IIntegrationEvent
{
    string EventId { get; }
    DateTime OccurredAtUtc { get; }
    string EventType { get; }
    string SchemaVersion { get; }
}

/// <summary>
/// Marker contract for asynchronous commands handled by workers (RabbitMQ).
/// Commands are imperative ("do something"), consumed by exactly one
/// handler, and must be processed idempotently.
/// </summary>
public interface IAsyncCommand
{
    string CommandId { get; }
    string CommandType { get; }
}
