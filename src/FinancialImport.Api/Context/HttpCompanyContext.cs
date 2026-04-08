using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;

namespace FinancialImport.Api.Context;

public sealed class HttpCompanyContext : ICompanyContext
{
    private readonly IUserContext _userContext;
    private readonly ISapSessionStore _sessionStore;
    private SapSessionContext? _session;
    private bool _loaded;

    public HttpCompanyContext(IUserContext userContext, ISapSessionStore sessionStore)
    {
        _userContext = userContext;
        _sessionStore = sessionStore;
    }

    public string? CompanyDb => GetSession()?.CompanyDb;
    public string? CompanyName => GetSession()?.CompanyName;
    public string? SapUserName => GetSession()?.SapUserName;

    private SapSessionContext? GetSession()
    {
        if (_loaded) return _session;
        _loaded = true;

        var userId = _userContext.UserId;
        if (userId == null) return null;

        _session = _sessionStore.GetActiveSessionAsync(userId.Value, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();

        return _session;
    }
}
