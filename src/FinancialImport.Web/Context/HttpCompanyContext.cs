using System.Security.Claims;
using FinancialImport.Application.Abstractions;

namespace FinancialImport.Web.Context;

public sealed class HttpCompanyContext : ICompanyContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCompanyContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CompanyDb => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.CompanyDb);

    public string? CompanyName => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.CompanyName);

    public string? SapUserName => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.SapUserName);
}
