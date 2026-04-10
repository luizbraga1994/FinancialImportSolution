using System.Collections.Concurrent;
using System.Text.Json;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
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
    private readonly ILogger<SapChartOfAccountsService> _logger;

    // Cache per company — survives across requests within the same scope
    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public SapChartOfAccountsService(
        IHttpClientFactory httpClientFactory,
        ILogger<SapChartOfAccountsService> logger)
    {
        _httpClientFactory = httpClientFactory;
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
        int skip = 0;
        const int top = 5000;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"ChartOfAccounts?$select=Code,Name&$top={top}&$skip={skip}");
            request.Headers.Add("B1SESSION", session.SessionId);
            if (!string.IsNullOrWhiteSpace(session.RouteId))
                request.Headers.Add("ROUTEID", session.RouteId);

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch ChartOfAccounts: {Status}", response.StatusCode);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var values = doc.RootElement.GetProperty("value");
            if (values.GetArrayLength() == 0) break;

            foreach (var item in values.EnumerateArray())
            {
                var code = item.GetProperty("Code").GetString();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    // Map both the full code and the code without check digit
                    accounts[code] = code;
                    var dashIdx = code.LastIndexOf('-');
                    if (dashIdx > 0)
                    {
                        var withoutCheckDigit = code[..dashIdx];
                        // Only add if not already mapped (avoid collisions)
                        accounts.TryAdd(withoutCheckDigit, code);
                    }
                }
            }

            skip += top;
            if (values.GetArrayLength() < top) break;
        }

        _logger.LogInformation("ChartOfAccounts loaded: {Count} accounts for {CompanyDb}.",
            accounts.Count, session.CompanyDb);

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
