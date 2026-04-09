namespace FinancialImport.Domain.Entities;

/// <summary>
/// Rich log entry persisted by the database audit sink. Separates
/// technical, functional, audit, integration, messaging and security
/// logs via the <see cref="Category"/> discriminator so operators can
/// filter by intent in addition to severity.
/// </summary>
public sealed class SystemLog
{
    public long Id { get; set; }

    /// <summary>Timestamp in UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Info / Warning / Error / Critical / Audit / Debug.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Functional category (Audit / Integration / Messaging / ...).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Class or service name that emitted the log.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Operation identifier (e.g. "ImportPreview", "SapDispatch").</summary>
    public string? Operation { get; set; }

    public long? UserId { get; set; }
    public string? CompanyDb { get; set; }

    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? MessageId { get; set; }

    public string? SapSessionId { get; set; }
    public long? ImportFileId { get; set; }
    public long? ImportLineId { get; set; }
    public string? BusinessKey { get; set; }

    public string? StatusBefore { get; set; }
    public string? StatusAfter { get; set; }
    public long? DurationMs { get; set; }

    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? StackTrace { get; set; }

    public string? MachineName { get; set; }
    public string? Environment { get; set; }
    public string? Application { get; set; }
}
