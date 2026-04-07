using FinancialImport.Application.Abstractions;

namespace FinancialImport.Web.Context;

public sealed class HttpLoginAuditContextAccessor : ILoginAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpLoginAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
