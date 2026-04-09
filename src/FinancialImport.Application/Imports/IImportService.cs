using FinancialImport.Application.Models;

namespace FinancialImport.Application.Imports;

public interface IImportService
{
    /// <summary>
    /// Validates the uploaded file, persists it and its lines, and
    /// returns a preview with layout detection + counters. The method is
    /// transactional: everything is committed atomically, so a crash
    /// between steps leaves the database in a consistent state.
    /// </summary>
    Task<ImportPreviewResult> PreviewAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a previously-validated file. Depending on the processing
    /// mode (synchronous or asynchronous), this either pushes the
    /// journal entries to SAP inline or enqueues a command for the
    /// worker to process in background.
    /// </summary>
    Task<ImportConfirmResult> ConfirmAsync(
        long importFileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the confirmation for an already imported file. Uses the
    /// same idempotency guarantees so the SAP ledger is not duplicated.
    /// </summary>
    Task<ImportConfirmResult> ReprocessAsync(
        long importFileId,
        CancellationToken cancellationToken = default);
}

public sealed class ImportPreviewResult
{
    public long ImportFileId { get; init; }
    public string LayoutDetected { get; init; } = string.Empty;
    public IReadOnlyCollection<LancamentoContabilImportado> Lines { get; init; } = Array.Empty<LancamentoContabilImportado>();
    public IReadOnlyCollection<string> Errors { get; init; } = Array.Empty<string>();
    public int ValidLines { get; init; }
    public int InvalidLines { get; init; }
    public int DuplicatedLines { get; init; }
    public int TotalLines => Lines.Count;
    public string? CorrelationId { get; init; }
}

public sealed class ImportConfirmResult
{
    public long ImportFileId { get; init; }
    public bool Accepted { get; init; }
    public bool IsAsync { get; init; }
    public string? CorrelationId { get; init; }
    public ImportProcessResult? SynchronousResult { get; init; }
    public string? Error { get; init; }
}

public sealed class ImportProcessResult
{
    public long ImportFileId { get; init; }
    public int Imported { get; init; }
    public int Duplicated { get; init; }
    public int Invalid { get; init; }
    public int SapErrors { get; init; }
    public long DurationMs { get; init; }
    public string Status { get; init; } = string.Empty;
}
