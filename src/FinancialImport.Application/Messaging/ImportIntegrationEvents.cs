using FinancialImport.Shared.Messaging;

namespace FinancialImport.Application.Messaging;

/// <summary>
/// Integration event published whenever a new ImportFile has been
/// accepted (validated) by the application. Downstream consumers
/// (analytics, notification, dashboards) use this to hydrate their own
/// stores.
/// </summary>
public sealed class ImportValidatedEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "import.validated";
    public string SchemaVersion => "1";

    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public long UserId { get; init; }
    public string LayoutDetected { get; init; } = string.Empty;
    public int TotalLines { get; init; }
    public int ValidLines { get; init; }
    public int InvalidLines { get; init; }
    public int DuplicatedLines { get; init; }
    public string FileHash { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
}

public sealed class ImportConfirmedEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "import.confirmed";
    public string SchemaVersion => "1";

    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public long UserId { get; init; }
}

public sealed class ImportProcessedEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "import.processed";
    public string SchemaVersion => "1";

    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public int Imported { get; init; }
    public int SapErrors { get; init; }
    public int Duplicated { get; init; }
    public int Invalid { get; init; }
    public string Status { get; init; } = string.Empty;
    public long DurationMs { get; init; }
}

public sealed class ImportFailedEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "import.failed";
    public string SchemaVersion => "1";

    public long? ImportFileId { get; init; }
    public string? CompanyDb { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
}

public sealed class SapDispatchFailedEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "sap.dispatch.failed";
    public string SchemaVersion => "1";

    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public string GroupKey { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
}

public sealed class SapDispatchSucceededEvent : IIntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string EventType => "sap.dispatch.succeeded";
    public string SchemaVersion => "1";

    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public string GroupKey { get; init; } = string.Empty;
    public int? DocEntry { get; init; }
    public long DurationMs { get; init; }
}
