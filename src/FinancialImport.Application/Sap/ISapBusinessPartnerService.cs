using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapBusinessPartnerService
{
    /// <summary>
    /// Fetches all Business Partner card codes from SAP and caches them per company.
    /// Returns a case-insensitive set of valid CardCode values.
    /// </summary>
    Task<IReadOnlySet<string>> GetCardCodesAsync(
        SapSessionContext session, CancellationToken cancellationToken = default);
}
