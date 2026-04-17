using FinancialImport.Application.Idempotency;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Correlation;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Messaging.Idempotency;

/// <summary>
/// EF Core implementation of <see cref="IIdempotencyStore"/>. Uses the
/// unique index on (Consumer, MessageId) to atomically reject duplicate
/// processing: the first handler to insert wins, concurrent inserts
/// fail with a <c>DbUpdateException</c> which we interpret as "already
/// processed".
/// </summary>
public sealed class EfIdempotencyStore : IIdempotencyStore
{
    private readonly AppDbContext _dbContext;
    private readonly ICorrelationContextAccessor _correlation;

    public EfIdempotencyStore(AppDbContext dbContext, ICorrelationContextAccessor correlation)
    {
        _dbContext = dbContext;
        _correlation = correlation;
    }

    public async Task<bool> TryRegisterAsync(
        string consumer,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(consumer))
            throw new ArgumentException("Consumer is required.", nameof(consumer));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("MessageId is required.", nameof(messageId));

        var exists = await _dbContext.InboxMessages
            .AnyAsync(m => m.Consumer == consumer && m.MessageId == messageId, cancellationToken);
        if (exists) return false;

        _dbContext.InboxMessages.Add(new InboxMessage
        {
            Consumer = consumer,
            MessageId = messageId,
            ProcessedAtUtc = DateTime.Now,
            CorrelationId = _correlation.Current?.CorrelationId
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent handler registered the same message first.
            // Clear the tracked entity so future SaveChanges calls don't
            // fail, and treat the message as already processed.
            foreach (var entry in _dbContext.ChangeTracker.Entries<InboxMessage>()
                .Where(e => e.Entity.MessageId == messageId && e.Entity.Consumer == consumer)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
            return false;
        }
    }

    public Task<bool> WasProcessedAsync(
        string consumer,
        string messageId,
        CancellationToken cancellationToken = default)
        => _dbContext.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.Consumer == consumer && m.MessageId == messageId, cancellationToken);
}
