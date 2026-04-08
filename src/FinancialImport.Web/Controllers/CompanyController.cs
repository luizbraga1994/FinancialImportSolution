using FinancialImport.Application.Sap;
using FinancialImport.Integration.Sap.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _discoveryService;
    private readonly ISapCompanySessionService _sessionService;
    private readonly SapServiceLayerOptions _sapOptions;

    public CompanyController(
        ISapCompanyDiscoveryService discoveryService,
        ISapCompanySessionService sessionService,
        IOptions<SapServiceLayerOptions> sapOptions)
    {
        _discoveryService = discoveryService;
        _sessionService = sessionService;
        _sapOptions = sapOptions.Value;
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
            companyDb, _sapOptions.UserName, _sapOptions.Password, cancellationToken);

        if (!result.Success)
        {
            TempData["Error"] = result.ErrorMessage ?? "Falha ao conectar na empresa selecionada.";
            var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
            return View(nameof(Index), companies);
        }

        return RedirectToAction("Index", "Home");
    }
}
