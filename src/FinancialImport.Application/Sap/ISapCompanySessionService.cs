using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapCompanySessionService
{
    Task<SapLoginResult> SignInCompanyAsync(string companyDb, string sapUserName, string sapPassword, CancellationToken cancellationToken = default);
    Task SignOutCompanyAsync(CancellationToken cancellationToken = default);
    Task<SapSessionContext?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);
}
