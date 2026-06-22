namespace FinancialImport.Application.Settlements;

public interface ISettlementService
{
    /// <summary>
    /// Validates the uploaded spreadsheet, resolves each line's open invoice in
    /// SAP, persists the settlement file and its lines, and returns a preview
    /// with counters. Transactional.
    /// </summary>
    Task<SettlementPreviewResult> PreviewAsync(SettlementFileContext context, CancellationToken cancellationToken = default);

    /// <summary>Confirms a validated settlement file, dispatching Incoming Payments to SAP.</summary>
    Task<SettlementConfirmResult> ConfirmAsync(long settlementFileId, CancellationToken cancellationToken = default);

    /// <summary>Re-runs the confirmation for an already processed file (idempotent).</summary>
    Task<SettlementConfirmResult> ReprocessAsync(long settlementFileId, CancellationToken cancellationToken = default);
}

public sealed class SettlementPreviewResult
{
    public long SettlementFileId { get; init; }
    public string LayoutDetected { get; init; } = string.Empty;
    public IReadOnlyCollection<SettlementLancamento> Lines { get; init; } = Array.Empty<SettlementLancamento>();
    public IReadOnlyCollection<string> Errors { get; init; } = Array.Empty<string>();
    public int ValidLines { get; init; }
    public int InvalidLines { get; init; }
    public int DuplicatedLines { get; init; }
    public int TotalLines => Lines.Count;
    public string? CorrelationId { get; init; }

    public bool IsDuplicateFile { get; init; }
    public string? ExistingFileStatus { get; init; }
}

public sealed class SettlementConfirmResult
{
    public long SettlementFileId { get; init; }
    public bool Accepted { get; init; }
    public bool IsAsync { get; init; }
    public string? CorrelationId { get; init; }
    public SettlementProcessResult? SynchronousResult { get; init; }
    public string? Error { get; init; }
}

public sealed class SettlementProcessResult
{
    public long SettlementFileId { get; init; }
    public int Settled { get; init; }
    public int Duplicated { get; init; }
    public int Invalid { get; init; }
    public int SapErrors { get; init; }
    public long DurationMs { get; init; }
    public string Status { get; init; } = string.Empty;
}
