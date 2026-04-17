using System.Collections.Concurrent;
using System.Text.Json;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Integration.Sap.Services;

/// <summary>
/// Fetches and caches the SAP chart of accounts per company so that
/// partial account codes from the import file (e.g. "1612001100002")
/// can be resolved to the full SAP code with check digit
/// (e.g. "1612001100002-0") before creating journal entries.
/// </summary>
public sealed class SapChartOfAccountsService : ISapChartOfAccountsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SapChartOfAccountsService> _logger;

    // Cache per company — survives across requests within the same scope
    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public SapChartOfAccountsService(
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider,
        ILogger<SapChartOfAccountsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAccountCodesAsync(
        SapSessionContext session, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(session.CompanyDb, out var cached) &&
            DateTime.UtcNow - cached.LoadedAt < CacheTtl)
        {
            _logger.LogDebug("ChartOfAccounts cache hit for {CompanyDb} ({Count} accounts).",
                session.CompanyDb, cached.Accounts.Count);
            return cached.Accounts;
        }

        _logger.LogInformation("Fetching ChartOfAccounts from SAP for {CompanyDb}...", session.CompanyDb);

        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var client = _httpClientFactory.CreateClient("SapServiceLayer");

        // SAP Service Layer v2 uses @odata.nextLink pagination. The default
        // page size is small (~20), so we ask for the maximum the server
        // supports via the Prefer header. If the server returns nextLink,
        // we follow it until there are no more pages.
        const int pageSize = 500;
        string? nextUrl = $"ChartOfAccounts?$select=Code,Name";
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

            // Session expired (typical when the stored session was created hours ago).
            // Try to re-authenticate once using SAP credentials from settings, then retry.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !reloginAttempted)
            {
                reloginAttempted = true;
                _logger.LogInformation("SAP session expired while fetching ChartOfAccounts for {CompanyDb}. Re-authenticating...", session.CompanyDb);

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
                        continue; // retry the same nextUrl with the new session
                    }

                    _logger.LogWarning("Re-authentication failed: {Error}", relogin.ErrorMessage);
                }
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to fetch ChartOfAccounts page {Page}: {Status} - {Body}",
                    pageCount, response.StatusCode, body);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var values))
                break;

            foreach (var item in values.EnumerateArray())
            {
                var code = item.GetProperty("Code").GetString();
                if (string.IsNullOrWhiteSpace(code)) continue;

                // Map the full code (e.g. "1612001100002-0")
                accounts[code] = code;

                // Also map the code without check digit (e.g. "1612001100002" → "1612001100002-0")
                var dashIdx = code.LastIndexOf('-');
                if (dashIdx > 0)
                {
                    var withoutCheckDigit = code[..dashIdx];
                    accounts.TryAdd(withoutCheckDigit, code);
                }
            }

            pageCount++;

            // Follow @odata.nextLink if present; otherwise stop.
            nextUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl)
                ? nl.GetString()
                : null;
        }

        _logger.LogInformation("ChartOfAccounts loaded: {Count} accounts ({Pages} pages) for {CompanyDb}.",
            accounts.Count, pageCount, session.CompanyDb);

        _cache[session.CompanyDb] = new CacheEntry(accounts, DateTime.UtcNow);
        return accounts;
    }

    public string ResolveAccountCode(string partialCode, IReadOnlyDictionary<string, string> accounts)
    {
        if (string.IsNullOrWhiteSpace(partialCode)) return partialCode;

        // Exact match (already has check digit)
        if (accounts.TryGetValue(partialCode, out var resolved))
            return resolved;

        return partialCode;
    }

    private sealed record CacheEntry(Dictionary<string, string> Accounts, DateTime LoadedAt);
}
