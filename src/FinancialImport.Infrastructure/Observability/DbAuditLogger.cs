using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Logging;
using Microsoft.Extensions.Hosting;

namespace FinancialImport.Infrastructure.Observability;

/// <summary>
/// Persists an <see cref="AuditLogEntry"/> into the LogSistema table,
/// automatically enriching it with correlation context, hostname and
/// environment. This class is the single write path for database
/// logs, so filters in the UI can rely on consistent field population.
/// </summary>
public sealed class DbAuditLogger : IAuditLogger
{
    private readonly AppDbContext _dbContext;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly IHostEnvironment _environment;
    private readonly string _machineName;

    public DbAuditLogger(
        AppDbContext dbContext,
        ICorrelationContextAccessor correlation,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _correlation = correlation;
        _environment = environment;
        _machineName = System.Environment.MachineName;
    }

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var ctx = _correlation.Current;

        var record = new SystemLog
        {
            OccurredAt       = entry.OccurredAtUtc == default ? DateTime.Now : entry.OccurredAtUtc,
            Level            = string.IsNullOrEmpty(entry.Level) ? LogSeverities.Info : entry.Level,
            Category         = string.IsNullOrEmpty(entry.Category) ? LogCategories.Technical : entry.Category,
            Source           = entry.Source,
            Operation        = entry.Operation,
            Message          = entry.Message,
            Details          = entry.Details,
            StackTrace       = entry.StackTrace,
            CorrelationId    = entry.CorrelationId ?? ctx?.CorrelationId,
            CausationId      = entry.CausationId ?? ctx?.CausationId,
            MessageId        = entry.MessageId,
            UserId           = entry.UserId ?? ctx?.UserId,
            CompanyDb        = entry.CompanyDb ?? ctx?.CompanyDb,
            SapSessionId     = entry.SapSessionId,
            ImportFileId     = entry.ImportFileId,
            ImportLineId     = entry.ImportLineId,
            BusinessKey      = entry.BusinessKey,
            StatusBefore     = entry.StatusBefore,
            StatusAfter      = entry.StatusAfter,
            DurationMs       = entry.DurationMs,
            MachineName      = entry.MachineName ?? _machineName,
            Environment      = entry.Environment ?? _environment.EnvironmentName,
            Application      = entry.Application ?? _environment.ApplicationName
        };

        _dbContext.SystemLogs.Add(record);

        // IMPORTANT: audit log writes must be durable — they must NOT be aborted
        // when the request's cancellation token fires (user navigates away, HTTP
        // request timeout, etc). Otherwise error audits never get persisted,
        // which is the worst time to lose them.
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Last-resort swallow: if the audit write itself fails (DB unreachable,
            // etc.) we do not want to mask the original error that triggered the
            // audit. The entry is also in Serilog console logs via ILogger calls
            // at the call sites, so nothing is lost in practice.
            _dbContext.ChangeTracker.Clear();
        }
    }
}
