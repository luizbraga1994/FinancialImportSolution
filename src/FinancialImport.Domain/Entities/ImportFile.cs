using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

public sealed class ImportFile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string LayoutDetected { get; set; } = string.Empty;
    public string? BranchDefault { get; set; }
    public bool UseBranchFromFile { get; set; }
    public ImportStatus Status { get; set; }
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int ImportedLines { get; set; }
    public int DuplicatedLines { get; set; }
    public int LinesWithError { get; set; }

    /// <summary>UTC timestamp for when the file was accepted by preview.</summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>UTC timestamp of the last update to this record (for audit).</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>UTC timestamp for when processing started.</summary>
    public DateTime? ProcessingStartedAtUtc { get; set; }

    /// <summary>UTC timestamp for when processing completed (success or failure).</summary>
    public DateTime? ProcessingCompletedAtUtc { get; set; }

    /// <summary>
    /// Correlation id that was active when the file was first uploaded.
    /// All log entries and broker messages related to this file share it.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Optional version marker for optimistic concurrency control.</summary>
    public int RowVersion { get; set; }

    public User? User { get; set; }
    public ICollection<ImportLine> Lines { get; set; } = new List<ImportLine>();
    public ICollection<JournalEntryDispatch> Dispatches { get; set; } = new List<JournalEntryDispatch>();
}
