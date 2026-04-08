using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Integration.Sap.Options;
using FinancialImport.Shared.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Integration.Sap.Services;

public sealed class SapCompanySessionService : ISapCompanySessionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISapSessionStore _sessionStore;
    private readonly IUserContext _userContext;
    private readonly SapServiceLayerOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<SapCompanySessionService> _logger;

    public SapCompanySessionService(
        IHttpClientFactory httpClientFactory,
        ISapSessionStore sessionStore,
        IUserContext userContext,
        IOptions<SapServiceLayerOptions> options,
        IClock clock,
        ILogger<SapCompanySessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionStore = sessionStore;
        _userContext = userContext;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SapLoginResult> SignInCompanyAsync(string companyDb, string sapUserName, string sapPassword, CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");

        try
        {
            var client = _httpClientFactory.CreateClient("SapServiceLayer");
            var payload = new
            {
                CompanyDB = companyDb,
                UserName = sapUserName,
                Password = sapPassword
            };

            var response = await client.PostAsJsonAsync("/b1s/v1/Login", payload, cancellationToken);
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SAP Login falhou para {CompanyDb}/{User}: {Status} - {Body}", companyDb, sapUserName, response.StatusCode, rawResponse);
                return SapLoginResult.Fail($"SAP retornou {(int)response.StatusCode}: {ExtractSapError(rawResponse)}");
            }

            var sessionId = ExtractSessionId(response, rawResponse);
            var routeId = ExtractRouteId(response);

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return SapLoginResult.Fail("Nao foi possivel obter SessionId do SAP.");
            }

            var session = new SapSessionContext
            {
                CompanyDb = companyDb,
                CompanyName = companyDb,
                SessionId = sessionId,
                RouteId = routeId,
                ExpiresAt = _clock.Now.AddMinutes(_options.SessionDurationMinutes),
                SapUserName = sapUserName
            };

            await _sessionStore.UpsertSessionAsync(userId, session, cancellationToken);

            _logger.LogInformation("SAP Login OK para {CompanyDb}/{User}", companyDb, sapUserName);
            return SapLoginResult.Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar no SAP Service Layer para {CompanyDb}/{User}", companyDb, sapUserName);
            return SapLoginResult.Fail($"Erro de comunicacao com SAP: {ex.Message}");
        }
    }

    public async Task SignOutCompanyAsync(CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId;
        if (userId == null) return;

        var session = await _sessionStore.GetActiveSessionAsync(userId.Value, cancellationToken);
        if (session == null) return;

        try
        {
            var client = _httpClientFactory.CreateClient("SapServiceLayer");
            var request = new HttpRequestMessage(HttpMethod.Post, "/b1s/v1/Logout");
            request.Headers.Add("B1SESSION", session.SessionId);
            if (!string.IsNullOrWhiteSpace(session.RouteId))
            {
                request.Headers.Add("ROUTEID", session.RouteId);
            }
            await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao fazer logout SAP para user {UserId}", userId);
        }

        await _sessionStore.DeactivateSessionAsync(userId.Value, cancellationToken);
    }

    public async Task<SapSessionContext?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId;
        if (userId == null) return null;
        return await _sessionStore.GetActiveSessionAsync(userId.Value, cancellationToken);
    }

    private static string ExtractSessionId(HttpResponseMessage response, string rawResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("SessionId", out var sid))
                return sid.GetString() ?? string.Empty;
        }
        catch (JsonException) { }

        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("B1SESSION=", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie.Split('=', ';')[1];
                }
            }
        }

        return string.Empty;
    }

    private static string? ExtractRouteId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                if (cookie.StartsWith("ROUTEID=", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie.Split('=', ';')[1];
                }
            }
        }
        return null;
    }

    private static string ExtractSapError(string rawResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("value", out var val))
            {
                return val.GetString() ?? rawResponse;
            }
        }
        catch (JsonException) { }
        return rawResponse;
    }
}
