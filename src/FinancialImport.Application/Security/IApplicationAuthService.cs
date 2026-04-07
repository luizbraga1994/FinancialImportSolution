using FinancialImport.Application.Models;

namespace FinancialImport.Application.Security;

public interface IApplicationAuthService
{
    Task<ApplicationUserSession> SignInAsync(string login, string password, CancellationToken cancellationToken = default);
}
