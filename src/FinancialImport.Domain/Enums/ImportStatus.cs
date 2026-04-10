namespace FinancialImport.Domain.Enums;

public enum ImportStatus
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
