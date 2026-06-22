namespace FinancialImport.Domain.Entities;

/// <summary>
/// Physical record that tracks a single SAP Incoming Payment dispatch attempt.
/// Each grouped settlement line produces exactly one dispatch, identified by a
/// stable GroupKeyHash. The combination (SettlementFileId, GroupKeyHash) has a
/// unique index so retries and crashes can never post the same payment twice.
/// Mirrors <see cref="JournalEntryDispatch"/>.
/// </summary>
public sealed class IncomingPaymentDispatch
{
    public long Id { get; set; }

    public long SettlementFileId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>Stable hash that identifies the grouped incoming payment.</summary>
    public string GroupKeyHash { get; set; } = string.Empty;

    /// <summary>Human readable group key (docpn|serial|series|model).</summary>
    public string GroupKey { get; set; } = string.Empty;

    public IncomingPaymentDispatchStatus Status { get; set; } = IncomingPaymentDispatchStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }

    public int? SapDocEntry { get; set; }
    public string? SapResponseSummary { get; set; }
    public string? LastError { get; set; }

    public string? CorrelationId { get; set; }

    public ReceivableSettlementFile? SettlementFile { get; set; }
}

public enum IncomingPaymentDispatchStatus
{
    Pending = 0,
    InFlight = 1,
    Dispatched = 2,
    Failed = 3,
    DeadLettered = 4
}
