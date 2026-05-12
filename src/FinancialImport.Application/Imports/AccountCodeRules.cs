namespace FinancialImport.Application.Imports;

/// <summary>
/// SAP-specific rule: a code that contains at least one letter is a Business
/// Partner (ShortName in Service Layer). Codes composed exclusively of digits
/// and dots are G/L accounts (AccountCode).
/// </summary>
public static class AccountCodeRules
{
    public static bool IsBusinessPartner(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.Any(char.IsLetter);
}
