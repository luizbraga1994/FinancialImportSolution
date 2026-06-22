namespace FinancialImport.Domain.Enums;

/// <summary>
/// Status of a single settlement line (one outgoing invoice being cleared
/// via a SAP Incoming Payment). Mirrors <see cref="ImportLineStatus"/>.
/// </summary>
public enum SettlementLineStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    Duplicated = 3,
    Settled = 4,
    SapError = 5,
    Excluded = 6,

    /// <summary>The invoice could not be matched against an open SAP invoice.</summary>
    InvoiceNotFound = 7
}
