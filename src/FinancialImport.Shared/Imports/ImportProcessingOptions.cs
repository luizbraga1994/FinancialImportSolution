namespace FinancialImport.Shared.Imports;

/// <summary>
/// All the knobs that used to be hardcoded inside the import pipeline —
/// file size, memo/reference truncation, grouping strategy, batch size,
/// retry attempts etc.
/// </summary>
public sealed class ImportProcessingOptions
{
    public const string SectionName = "Imports:Processing";

    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = { ".csv", ".txt", ".xlsx" };

    public int BatchSizeLines { get; set; } = 500;
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// When true, the <c>ConfirmAsync</c> call enqueues a
    /// <c>ProcessImportCommand</c> on RabbitMQ and returns immediately.
    /// The <c>ImportProcessWorker</c> then runs the actual SAP dispatch
    /// asynchronously. When false, the legacy synchronous behavior
    /// (processing inline on the HTTP request thread) is preserved.
    /// </summary>
    public bool UseAsyncConfirmation { get; set; } = false;

    /// <summary>SAP Journal Entry Memo maximum length.</summary>
    public int MemoMaxLength { get; set; } = 254;

    /// <summary>SAP Journal Entry Reference maximum length.</summary>
    public int ReferenceMaxLength { get; set; } = 27;

    /// <summary>SAP Journal Entry line memo maximum length.</summary>
    public int LineMemoMaxLength { get; set; } = 254;

    /// <summary>
    /// Fields used to build the business key for deduplication. The
    /// <c>SeqLancamento</c> (the external control identifier recently
    /// added) is always included when present to avoid false positives.
    /// </summary>
    public DeduplicationFields DeduplicationKey { get; set; } = new();

    /// <summary>
    /// Fields used to group lines into a single SAP Journal Entry.
    /// </summary>
    public string[] JournalEntryGroupingFields { get; set; } =
    {
        "Reference",
        "PostingDate",
        "DueDate",
        "DocumentDate",
        "SeqLancamento"
    };

    public decimal JournalBalanceTolerance { get; set; } = 0.01m;

    /// <summary>
    /// Upper bound on the acceptable difference between the latest posting
    /// date and today. Lines outside this window are still allowed but
    /// flagged in the audit log.
    /// </summary>
    public int PostingDateFutureToleranceDays { get; set; } = 1;
}

public sealed class DeduplicationFields
{
    public bool IncludeCompanyDb { get; set; } = true;
    public bool IncludeReference { get; set; } = true;
    public bool IncludeAccounts { get; set; } = true;
    public bool IncludeDates { get; set; } = true;
    public bool IncludeAmount { get; set; } = true;
    public bool IncludeMemo { get; set; } = true;
    public bool IncludeBranch { get; set; } = true;

    /// <summary>
    /// Include the SeqLancamento column. When the source file provides a
    /// control identifier, including it ensures that similar lines with
    /// distinct IDs are NOT flagged as duplicates.
    /// </summary>
    public bool IncludeSeqLancamento { get; set; } = true;
}
