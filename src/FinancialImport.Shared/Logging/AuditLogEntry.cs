namespace FinancialImport.Shared.Logging;

/// <summary>
/// Rich log entry persisted in the database by the audit sink. All fields
/// are optional so the same type can be used for technical, functional,
/// audit, integration, messaging and security logs.
/// </summary>
public sealed class AuditLogEntry
{
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string Level { get; init; } = LogSeverities.Info;
    public string Category { get; init; } = LogCategories.Technical;
    public string Source { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? StackTrace { get; init; }

    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? MessageId { get; init; }

    public long? UserId { get; init; }
    public string? CompanyDb { get; init; }
    public string? SapSessionId { get; init; }
    public long? ImportFileId { get; init; }
    public long? ImportLineId { get; init; }

    public string? BusinessKey { get; init; }
    public string? StatusBefore { get; init; }
    public string? StatusAfter { get; init; }

    public long? DurationMs { get; init; }
    public string? MachineName { get; init; }
    public string? Environment { get; init; }
    public string? Application { get; init; }
}
