using FinancialImport.Domain.Entities;

namespace FinancialImport.Application.Outbox;

/// <summary>
/// Port used by producers to persist outbox messages inside the same
/// transaction as the business change. The OutboxDispatcher worker then
/// polls, claims and publishes pending messages to the broker.
/// </summary>
public interface IOutboxRepository
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default);

    Task MarkDispatchedAsync(long id, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(long id, string error, int nextAttemptDelaySeconds, CancellationToken cancellationToken = default);

    Task MarkDeadLetteredAsync(long id, string error, CancellationToken cancellationToken = default);
}
