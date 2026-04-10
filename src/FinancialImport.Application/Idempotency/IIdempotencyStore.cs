namespace FinancialImport.Application.Idempotency;

/// <summary>
/// Per-consumer deduplication store for message handlers. Stores the
/// MessageId of every successfully processed message and refuses to
/// re-execute a handler for the same id. Backed by a unique index on
/// the MessagesInbox table in the database.
/// </summary>
public interface IIdempotencyStore
{
    Task<bool> TryRegisterAsync(
        string consumer,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<bool> WasProcessedAsync(
        string consumer,
        string messageId,
        CancellationToken cancellationToken = default);
}
