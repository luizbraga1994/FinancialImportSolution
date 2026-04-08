using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using Microsoft.AspNetCore.Http;

namespace FinancialImport.Web.Context;

public sealed class HttpCompanyContext : ICompanyContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserContext _userContext;
    private readonly ISapSessionStore _sessionStore;
    private SapSessionContext? _sapSession;
    private bool _sapLoaded;

    public HttpCompanyContext(
        IHttpContextAccessor httpContextAccessor,
        IUserContext userContext,
        ISapSessionStore sessionStore)
    {
        _httpContextAccessor = httpContextAccessor;
        _userContext = userContext;
        _sessionStore = sessionStore;
    }

    public string? CompanyDb
    {
        get
        {
            // First try claims (set at login)
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimConstants.CompanyDb)?.Value;
            if (!string.IsNullOrWhiteSpace(claim)) return claim;

            // Fallback to SAP session
            return GetSapSession()?.CompanyDb;
        }
    }

    public string? CompanyName
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimConstants.CompanyName)?.Value;
            if (!string.IsNullOrWhiteSpace(claim)) return claim;

            return GetSapSession()?.CompanyName;
        }
    }

    public string? SapUserName => GetSapSession()?.SapUserName;

    private SapSessionContext? GetSapSession()
    {
        if (_sapLoaded) return _sapSession;
        _sapLoaded = true;

        var userId = _userContext.UserId;
        if (userId == null) return null;

        _sapSession = _sessionStore.GetActiveSessionAsync(userId.Value, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();

        return _sapSession;
    }
}
