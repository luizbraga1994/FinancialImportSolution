using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

/// <summary>
/// A spreadsheet of accounts-receivable settlements ("Baixa de Notas de Saída").
/// Each file groups many <see cref="ReceivableSettlementLine"/> rows that are
/// dispatched to SAP Business One as Incoming Payments (ORCT). Mirrors
/// <see cref="ImportFile"/> so the existing preview/confirm/dispatch pipeline
/// can be reused with the same idempotency and auditing guarantees.
/// </summary>
public sealed class ReceivableSettlementFile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string LayoutDetected { get; set; } = string.Empty;
    public SettlementStatus Status { get; set; }
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int SettledLines { get; set; }
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
    public ICollection<ReceivableSettlementLine> Lines { get; set; } = new List<ReceivableSettlementLine>();
    public ICollection<IncomingPaymentDispatch> Dispatches { get; set; } = new List<IncomingPaymentDispatch>();
}
