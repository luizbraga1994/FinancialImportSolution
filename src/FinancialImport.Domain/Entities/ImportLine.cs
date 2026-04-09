using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

public sealed class ImportLine
{
    public long Id { get; set; }
    public long ImportFileId { get; set; }

    /// <summary>Raw hash of the full source JSON. Used only for change tracking.</summary>
    public string LineHash { get; set; } = string.Empty;

    /// <summary>
    /// Deduplication key hash. Built from the fields configured in
    /// <c>Imports:Processing:DeduplicationKey</c>, ALWAYS including
    /// <see cref="SeqLancamento"/> when present so similar records with
    /// distinct external ids are never flagged as duplicates.
    /// </summary>
    public string BusinessKeyHash { get; set; } = string.Empty;

    /// <summary>External control identifier (new field). Used both for
    /// deduplication and for grouping journal entries.</summary>
    public string? SeqLancamento { get; set; }

    public string Reference { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string ContraAccountCode { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime DocumentDate { get; set; }
    public decimal Amount { get; set; }
    public decimal? CreditAmount { get; set; }
    public decimal? DebitAmount { get; set; }
    public string LineMemo { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public ImportLineStatus Status { get; set; }
    public string? ValidationMessage { get; set; }
    public string? SapReturnMessage { get; set; }
    public int? SapDocEntry { get; set; }
    public string? SourceJson { get; set; }

    /// <summary>
    /// Hash of the grouping key, linking this line to a single
    /// <see cref="JournalEntryDispatch"/>.
    /// </summary>
    public string? GroupKeyHash { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ImportFile? ImportFile { get; set; }
}
