namespace FinancialImport.Shared.Logging;

/// <summary>
/// Persistent audit sink abstraction. The infrastructure implementation
/// writes each entry to the database (LogSistema) with automatic
/// correlation/causation enrichment.
/// </summary>
public interface IAuditLogger
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
