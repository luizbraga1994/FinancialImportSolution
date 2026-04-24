using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FinancialImport.Web.Controllers;

public class BranchViewModel
{
    public int BPLId { get; set; }
    public string BPLName { get; set; } = string.Empty;
    public string? AliasName { get; set; }
    public string? DefaultCurrency { get; set; }
    public bool Disabled { get; set; }
}

[Authorize(Policy = "visualizar_filiais")]
public class BranchController : Controller
{
    private readonly ISapSessionStore _sapSessionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BranchController> _logger;

    public BranchController(
        ISapSessionStore sapSessionStore,
        IHttpClientFactory httpClientFactory,
        ILogger<BranchController> logger)
    {
        _sapSessionStore = sapSessionStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companyDb = User.FindFirst("company_db")?.Value;
        var companyName = User.FindFirst("company_name")?.Value;

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            TempData["Error"] = "Selecione uma empresa antes de consultar filiais.";
            return RedirectToAction("Index", "Company");
        }

        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            TempData["Error"] = "Sessao invalida.";
            return RedirectToAction("Index", "Home");
        }

        var session = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
        if (session == null || !session.CompanyDb.Equals(companyDb, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Sem sessao SAP ativa. Selecione a empresa novamente.";
            return RedirectToAction("Index", "Company");
        }

        var branches = new List<BranchViewModel>();
        try
        {
            var client = _httpClientFactory.CreateClient("SapServiceLayer");
            var request = new HttpRequestMessage(HttpMethod.Get,
                "BusinessPlaces?$select=BPLID,BPLName,AliasName,Disabled&$orderby=BPLID");
            request.Headers.Add("B1SESSION", session.SessionId);
            if (!string.IsNullOrWhiteSpace(session.RouteId))
                request.Headers.Add("ROUTEID", session.RouteId);

            var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var values))
                {
                    foreach (var item in values.EnumerateArray())
                    {
                        branches.Add(new BranchViewModel
                        {
                            BPLId = item.TryGetProperty("BPLID", out var id) ? id.GetInt32() : 0,
                            BPLName = item.TryGetProperty("BPLName", out var name) ? name.GetString() ?? "" : "",
                            AliasName = item.TryGetProperty("AliasName", out var alias) ? alias.GetString() : null,
                            DefaultCurrency = item.TryGetProperty("DefaultCurrency", out var curr) ? curr.GetString() : null,
                            Disabled = item.TryGetProperty("Disabled", out var dis) && dis.GetString() == "tYES"
                        });
                    }
                }
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to fetch BusinessPlaces: {Status} - {Body}", response.StatusCode, body);
                TempData["Error"] = "Nao foi possivel carregar as filiais do SAP.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching BusinessPlaces from SAP for company {CompanyDb}", companyDb);
            TempData["Error"] = "Erro ao consultar filiais no SAP.";
        }

        ViewBag.CompanyDb = companyDb;
        ViewBag.CompanyName = companyName ?? companyDb;
        return View(branches);
    }
}
