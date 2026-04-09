namespace FinancialImport.Domain.Entities;

/// <summary>
/// Physical record that tracks a single SAP JournalEntry dispatch
/// attempt. Each grouped set of lines produces exactly one dispatch,
/// identified by a stable GroupKeyHash that is ALSO sent to SAP as the
/// Reference so SAP rejects duplicates naturally. The combination
/// (CompanyDb, GroupKeyHash) has a unique index so retries and crashes
/// can never enqueue the same dispatch twice.
/// </summary>
public sealed class JournalEntryDispatch
{
    public long Id { get; set; }

    public long ImportFileId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>Stable hash that identifies the grouped journal entry.</summary>
    public string GroupKeyHash { get; set; } = string.Empty;

    /// <summary>Human readable group key (reference|posting|due|doc|seq).</summary>
    public string GroupKey { get; set; } = string.Empty;

    public JournalDispatchStatus Status { get; set; } = JournalDispatchStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }

    public int? SapDocEntry { get; set; }
    public string? SapResponseSummary { get; set; }
    public string? LastError { get; set; }

    public string? CorrelationId { get; set; }

    public ImportFile? ImportFile { get; set; }
}

public enum JournalDispatchStatus
{
    Pending = 0,
    InFlight = 1,
    Dispatched = 2,
    Failed = 3,
    DeadLettered = 4
}
