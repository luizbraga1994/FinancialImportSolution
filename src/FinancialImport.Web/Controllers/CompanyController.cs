using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using FinancialImport.Shared.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _discoveryService;
    private readonly ISapCompanySessionService _sessionService;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;

    public CompanyController(
        ISapCompanyDiscoveryService discoveryService,
        ISapCompanySessionService sessionService,
        ISystemSettingsService settings,
        IAuditLogger audit)
    {
        _discoveryService = discoveryService;
        _sessionService = sessionService;
        _settings = settings;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
        return View(companies);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string companyDb, CancellationToken cancellationToken)
    {
        var userLogin = User.FindFirst("login")?.Value ?? "desconhecido";

        // The SAP Service Layer login can take 5-10s. We deliberately do NOT pass
        // the request cancellation token to it: if the user clicks the card again
        // (impatience) or navigates away mid-login, a cancelled login leaves the
        // session store in a half-dirty state AND throws a generic 500 that hides
        // the real cause. A fresh CancellationToken bounded by a 60s timeout gives
        // SAP plenty of headroom while still preventing runaway requests.
        using var sapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        SapLoginResult result;
        try
        {
            result = await _sessionService.SignInCompanyAsync(
                companyDb,
                _settings.Get("Sap:UserName") ?? "",
                _settings.Get("Sap:Password") ?? "",
                sapTimeout.Token);
        }
        catch (OperationCanceledException) when (sapTimeout.IsCancellationRequested)
        {
            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Error,
                Category = LogCategories.Audit,
                Source = nameof(CompanyController),
                Operation = "SelecionarEmpresa",
                Message = $"Timeout ao selecionar empresa '{companyDb}' por '{userLogin}' (60s). SAP nao respondeu a tempo.",
                CompanyDb = companyDb
            }, CancellationToken.None);

            TempData["Error"] = "Tempo esgotado ao conectar no SAP. Tente novamente.";
            var companies = await _discoveryService.GetAvailableCompaniesAsync(CancellationToken.None);
            return View(nameof(Index), companies);
        }

        if (!result.Success)
        {
            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Warning,
                Category = LogCategories.Audit,
                Source = nameof(CompanyController),
                Operation = "SelecionarEmpresa",
                Message = $"Falha ao selecionar empresa '{companyDb}' por '{userLogin}': {result.ErrorMessage}",
                CompanyDb = companyDb
            }, CancellationToken.None);

            TempData["Error"] = result.ErrorMessage ?? "Falha ao conectar na empresa selecionada.";
            var companies = await _discoveryService.GetAvailableCompaniesAsync(CancellationToken.None);
            return View(nameof(Index), companies);
        }

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Info,
            Category = LogCategories.Audit,
            Source = nameof(CompanyController),
            Operation = "SelecionarEmpresa",
            Message = $"Empresa '{companyDb}' selecionada por '{userLogin}'. Sessao SAP estabelecida.",
            CompanyDb = companyDb
        }, CancellationToken.None);

        return RedirectToAction("Index", "Home");
    }
}
