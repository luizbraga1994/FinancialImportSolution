using System.Collections.Concurrent;
using System.Text.Json;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Integration.Sap.Services;

/// <summary>
/// Fetches and caches SAP Business Partner card codes per company so that
/// letter-containing account codes from import files can be verified as
/// valid Business Partners before a JournalEntry is posted.
/// </summary>
public sealed class SapBusinessPartnerService : ISapBusinessPartnerService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SapBusinessPartnerService> _logger;

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public SapBusinessPartnerService(
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ILogger<SapBusinessPartnerService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IReadOnlySet<string>> GetCardCodesAsync(
        SapSessionContext session, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(session.CompanyDb, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < CacheTtl)
        {
            _logger.LogDebug("BusinessPartners cache hit for {CompanyDb} ({Count} codes).",
                session.CompanyDb, cached.CardCodes.Count);
            return cached.CardCodes;
        }

        _logger.LogInformation("Fetching BusinessPartners from SAP for {CompanyDb}...", session.CompanyDb);

        var cardCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var client = _httpClientFactory.CreateClient("SapServiceLayer");

        const int pageSize = 500;
        string? nextUrl = "BusinessPartners?$select=CardCode";
        int pageCount = 0;
        var reloginAttempted = false;

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Add("B1SESSION", session.SessionId);
            if (!string.IsNullOrWhiteSpace(session.RouteId))
                request.Headers.Add("ROUTEID", session.RouteId);
            request.Headers.Add("Prefer", $"odata.maxpagesize={pageSize}");

            var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !reloginAttempted)
            {
                reloginAttempted = true;
                _logger.LogInformation("SAP session expired while fetching BusinessPartners for {CompanyDb}. Re-authenticating...", session.CompanyDb);

                using var scope = _serviceProvider.CreateScope();
                var sessionService = scope.ServiceProvider.GetService<ISapCompanySessionService>();
                var settings = scope.ServiceProvider.GetService<ISystemSettingsService>();

                if (sessionService != null && settings != null)
                {
                    var relogin = await sessionService.SignInCompanyAsync(
                        session.CompanyDb,
                        settings.Get("Sap:UserName") ?? "",
                        settings.Get("Sap:Password") ?? "",
                        cancellationToken);

                    if (relogin.Success && relogin.Session != null)
                    {
                        session = relogin.Session;
                        continue;
                    }

                    _logger.LogWarning("Re-authentication failed: {Error}", relogin.ErrorMessage);
                }
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to fetch BusinessPartners page {Page}: {Status} - {Body}",
                    pageCount, response.StatusCode, body);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var values))
                break;

            foreach (var item in values.EnumerateArray())
            {
                var code = item.GetProperty("CardCode").GetString();
                if (!string.IsNullOrWhiteSpace(code))
                    cardCodes.Add(code);
            }

            pageCount++;

            nextUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl)
                ? nl.GetString()
                : null;
        }

        _logger.LogInformation("BusinessPartners loaded: {Count} codes ({Pages} pages) for {CompanyDb}.",
            cardCodes.Count, pageCount, session.CompanyDb);

        _cache[session.CompanyDb] = new CacheEntry(cardCodes, DateTime.UtcNow);
        return cardCodes;
    }

    private sealed record CacheEntry(HashSet<string> CardCodes, DateTime LoadedAt);
}
