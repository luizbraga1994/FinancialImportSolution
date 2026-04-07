using System.Security.Claims;
using FinancialImport.Application.Abstractions;

namespace FinancialImport.Web.Context;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.UserId);
            return long.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Login => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.Login);

    public string? Name => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimConstants.Name);
}
