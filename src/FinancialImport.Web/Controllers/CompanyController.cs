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

        var result = await _sessionService.SignInCompanyAsync(
            companyDb,
            _settings.Get("Sap:UserName") ?? "",
            _settings.Get("Sap:Password") ?? "",
            cancellationToken);

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
            }, cancellationToken);

            TempData["Error"] = result.ErrorMessage ?? "Falha ao conectar na empresa selecionada.";
            var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
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
        }, cancellationToken);

        return RedirectToAction("Index", "Home");
    }
}
