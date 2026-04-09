using FinancialImport.Application.Outbox;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Messaging.Outbox;

/// <summary>
/// EF Core-backed transactional outbox. Enqueue is scoped to the
/// caller's DbContext so it participates in the same transaction as
/// the business write (the outbox guarantee). The claim/mark methods
/// use dedicated scopes and short-lived transactions so the dispatcher
/// does not starve the writer.
/// </summary>
public sealed class OutboxRepository : IOutboxRepository
{
    private readonly AppDbContext _dbContext;

    public OutboxRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.MessageId))
            throw new ArgumentException("MessageId is required.", nameof(message));
        if (string.IsNullOrWhiteSpace(message.Channel))
            throw new ArgumentException("Channel is required.", nameof(message));
        if (string.IsNullOrWhiteSpace(message.Broker))
            throw new ArgumentException("Broker is required.", nameof(message));

        // Deliberately do NOT SaveChanges here: the caller decides when
        // the business transaction commits, ensuring atomicity.
        _dbContext.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var claimUntil = nowUtc.Add(claimTimeout);

        // Candidate set: pending or previously-inflight messages whose
        // claim has expired. Ordered by creation time (FIFO).
        var candidates = await _dbContext.OutboxMessages
            .Where(m =>
                (m.Status == OutboxMessageStatus.Pending
                    && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= nowUtc))
                || (m.Status == OutboxMessageStatus.InFlight
                    && m.ClaimedUntilUtc != null
                    && m.ClaimedUntilUtc <= nowUtc))
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return Array.Empty<OutboxMessage>();

        foreach (var message in candidates)
        {
            message.Status = OutboxMessageStatus.InFlight;
            message.ClaimedUntilUtc = claimUntil;
            message.AttemptCount += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return candidates;
    }

    public async Task MarkDispatchedAsync(long id, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages.FirstOrDefaultAsync(
            m => m.Id == id, cancellationToken);
        if (message == null) return;

        message.Status = OutboxMessageStatus.Dispatched;
        message.DispatchedAtUtc = DateTime.UtcNow;
        message.ClaimedUntilUtc = null;
        message.NextAttemptAtUtc = null;
        message.LastError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        long id,
        string error,
        int nextAttemptDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages.FirstOrDefaultAsync(
            m => m.Id == id, cancellationToken);
        if (message == null) return;

        message.Status = OutboxMessageStatus.Pending;
        message.ClaimedUntilUtc = null;
        message.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(nextAttemptDelaySeconds);
        message.LastError = Truncate(error, 2000);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeadLetteredAsync(
        long id,
        string error,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.OutboxMessages.FirstOrDefaultAsync(
            m => m.Id == id, cancellationToken);
        if (message == null) return;

        message.Status = OutboxMessageStatus.DeadLettered;
        message.ClaimedUntilUtc = null;
        message.NextAttemptAtUtc = null;
        message.LastError = Truncate(error, 2000);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max
            ? value
            : value.Substring(0, max - 3) + "...";
}
