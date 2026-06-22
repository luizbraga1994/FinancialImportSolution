namespace FinancialImport.Domain.Enums;

/// <summary>
/// Lifecycle status of a receivable settlement file ("Baixa de Notas de Saída").
/// Mirrors <see cref="ImportStatus"/> so the settlement pipeline reuses the
/// same preview -> confirm -> dispatch semantics as the import pipeline.
/// </summary>
public enum SettlementStatus
{
    Pending = 0,
    Validated = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Rejected = 5,
    PartiallyCompleted = 6,
    Cancelled = 7
}
