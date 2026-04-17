using System.Net.Http.Json;
using System.Text.Json;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Integration.Sap.Services;

public sealed class SapJournalEntryService : ISapJournalEntryService
{
    private static readonly JsonSerializerOptions SapJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SapJournalEntryService> _logger;

    public SapJournalEntryService(
        IHttpClientFactory httpClientFactory,
        ILogger<SapJournalEntryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SapResult> CreateJournalEntryAsync(SapSessionContext session, SapJournalEntry payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SapServiceLayer");
            var request = new HttpRequestMessage(HttpMethod.Post, "JournalEntries");
            request.Headers.Add("B1SESSION", session.SessionId);
            if (!string.IsNullOrWhiteSpace(session.RouteId))
            {
                request.Headers.Add("ROUTEID", session.RouteId);
            }
            request.Content = JsonContent.Create(payload, mediaType: null, SapJsonOptions);

            _logger.LogDebug("SAP JournalEntry request para {CompanyDb}: {Payload}",
                session.CompanyDb, JsonSerializer.Serialize(payload, SapJsonOptions));

            var response = await client.SendAsync(request, cancellationToken);
            var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorMsg = ExtractSapError(rawResponse);
                _logger.LogWarning("SAP session expired for {CompanyDb}: {Error}", session.CompanyDb, errorMsg);
                return SapResult.SessionExpired(errorMsg, rawResponse);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = ExtractSapError(rawResponse);
                _logger.LogWarning("SAP JournalEntry falhou para {CompanyDb}: {Status} - {Error}",
                    session.CompanyDb, response.StatusCode, errorMsg);
                return SapResult.Fail(errorMsg, rawResponse);
            }

            _logger.LogInformation("SAP JournalEntry criado com sucesso para {CompanyDb}", session.CompanyDb);
            return SapResult.Ok(rawResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar JournalEntry no SAP para {CompanyDb}", session.CompanyDb);
            return SapResult.Fail($"Erro de comunicacao: {ex.Message}");
        }
    }

    private static string ExtractSapError(string rawResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                // v2 format: "message": "Error text"
                if (msg.ValueKind == JsonValueKind.String)
                    return msg.GetString() ?? rawResponse;

                // v1 format: "message": { "value": "Error text" }
                if (msg.ValueKind == JsonValueKind.Object &&
                    msg.TryGetProperty("value", out var val))
                    return val.GetString() ?? rawResponse;
            }
        }
        catch (JsonException) { }
        return rawResponse;
    }
}
