using FinancialImport.Application.Sap;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _discoveryService;
    private readonly ISapCompanySessionService _sessionService;

    public CompanyController(
        ISapCompanyDiscoveryService discoveryService,
        ISapCompanySessionService sessionService)
    {
        _discoveryService = discoveryService;
        _sessionService = sessionService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
        return View(companies);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string companyDb, string sapUser, string sapPassword, CancellationToken cancellationToken)
    {
        var result = await _sessionService.SignInCompanyAsync(companyDb, sapUser, sapPassword, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Failed to connect to the selected company.");
            var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);
            return View(nameof(Index), companies);
        }

        return RedirectToAction("Index", "Home");
    }
}
