using FinancialImport.Application.Imports;

namespace FinancialImport.Infrastructure.Imports;

internal static class SapAccountCodeHelper
{
    internal static bool IsBusinessPartner(string? code)
        => AccountCodeRules.IsBusinessPartner(code);
}
