using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapCompanyDiscoveryService
{
    Task<IReadOnlyCollection<SapCompanyInfo>> GetAvailableCompaniesAsync(CancellationToken cancellationToken = default);
}
