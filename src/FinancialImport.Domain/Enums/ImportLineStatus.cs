namespace FinancialImport.Domain.Enums;

public enum ImportLineStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    Duplicated = 3,
    Imported = 4,
    SapError = 5
}
