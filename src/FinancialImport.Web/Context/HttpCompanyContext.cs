using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Web.Context;

// Removido ClaimConstants daqui para evitar duplicidade - usar o do arquivo separado

public sealed class HttpCompanyContext : ICompanyContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserContext _userContext;
    private readonly ISapSessionStore _sessionStore;
    private SapSessionContext? _sapSession;
    private bool _sapLoaded;
    private readonly ILogger<HttpCompanyContext>? _logger;

    public HttpCompanyContext(
        IHttpContextAccessor httpContextAccessor,
        IUserContext userContext,
        ISapSessionStore sessionStore,
        ILogger<HttpCompanyContext>? logger = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _userContext = userContext;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public string? CompanyDb
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(Web.ClaimConstants.CompanyDb)?.Value;
            if (!string.IsNullOrWhiteSpace(claim))
            {
                _logger?.LogDebug("CompanyDb obtido do claim: '{CompanyDb}'", claim);
                return claim;
            }

            var session = GetSapSession();
            var sessionDb = session?.CompanyDb;
            _logger?.LogDebug("CompanyDb obtido da sessao SAP: '{CompanyDb}'", sessionDb);
            return sessionDb;
        }
    }

    public string? CompanyName
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(Web.ClaimConstants.CompanyName)?.Value;
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
        if (userId == null)
        {
            _logger?.LogWarning("GetSapSession: UserId is null, nao e possivel obter sessao SAP.");
            return null;
        }

        try
        {
            _sapSession = _sessionStore.GetActiveSessionAsync(userId.Value, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (_sapSession == null)
            {
                _logger?.LogWarning("GetSapSession: Nenhuma sessao SAP ativa encontrada para UserId {UserId}.", userId);
            }
            else
            {
                _logger?.LogDebug("GetSapSession: Sessao SAP encontrada para UserId {UserId}, CompanyDb: '{CompanyDb}'", userId, _sapSession.CompanyDb);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "GetSapSession: Erro ao obter sessao SAP para UserId {UserId}.", userId);
        }

        return _sapSession;
    }
}