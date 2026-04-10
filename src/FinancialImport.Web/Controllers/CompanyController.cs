using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _discoveryService;
    private readonly ISapCompanySessionService _sessionService;
    private readonly ISystemSettingsService _settings;

    public CompanyController(
        ISapCompanyDiscoveryService discoveryService,
        ISapCompanySessionService sessionService,
        ISystemSettingsService settings)
    {
        _discoveryService = discoveryService;
        _sessionService = sessionService;
        _settings = settings;
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
        var result = await _sessionService.SignInCompanyAsync(
            companyDb,
            _settings.Get("Sap:UserName") ?? "",
            _settings.Get("Sap:Password") ?? "",
            cancellationToken);

        if (!result.Success)
        {
            TempData["Error"] = result.ErrorMessage ?? "Falha ao conectar na empresa selecionada.";
            var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
            return View(nameof(Index), companies);
        }

        return RedirectToAction("Index", "Home");
    }
}
