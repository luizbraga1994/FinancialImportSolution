using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapChartOfAccountsService
{
    /// <summary>
    /// Fetches the chart of accounts from SAP and caches it per company.
    /// Returns a dictionary mapping partial codes to the full SAP account code.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetAccountCodesAsync(
        SapSessionContext session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a partial account code to the full SAP code (with check digit).
    /// Returns the original code if no match is found.
    /// </summary>
    string ResolveAccountCode(string partialCode, IReadOnlyDictionary<string, string> accounts);
}
