using System.Security.Claims;
using FinancialImport.Application.Sap;
using FinancialImport.Web.Context;
using FinancialImport.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public sealed class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _companyDiscovery;
    private readonly ISapCompanySessionService _sapSessionService;
    public CompanyController(
        ISapCompanyDiscoveryService companyDiscovery,
        ISapCompanySessionService sapSessionService)
    {
        _companyDiscovery = companyDiscovery;
        _sapSessionService = sapSessionService;
    }

    [HttpGet]
    public async Task<IActionResult> Select(CancellationToken cancellationToken)
    {
        var companies = (await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken))
            .Where(c => c.IsActive)
            .ToArray();
        var allowed = User.FindAll("company").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowed.Count > 0)
        {
            companies = companies.Where(c => allowed.Contains(c.CompanyDb)).ToArray();
        }

        return View(new CompanySelectionViewModel
        {
            Companies = companies
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(CompanySelectionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            return View(model);
        }

        var allowed = User.FindAll("company").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count > 0 && !allowed.Contains(model.CompanyDb))
        {
            model.Error = "Company não permitida para o usuário.";
            model.Companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            return View(model);
        }

        var result = await _sapSessionService.SignInCompanyAsync(model.CompanyDb, model.SapUserName, model.SapPassword, cancellationToken);
        if (!result.Success || result.Session == null)
        {
            model.Error = result.ErrorMessage ?? "Falha ao autenticar no SAP.";
            model.Companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            return View(model);
        }

        var companies = (await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken))
            .Where(c => c.IsActive)
            .ToArray();
        var companyName = companies.FirstOrDefault(c => c.CompanyDb.Equals(model.CompanyDb, StringComparison.OrdinalIgnoreCase))?.CompanyName
            ?? model.CompanyDb;

        await RefreshClaimsAsync(result.Session, companyName);
        return RedirectToAction("Index", "Dashboard");
    }

    private async Task RefreshClaimsAsync(FinancialImport.Application.Models.SapSessionContext session, string companyName)
    {
        var identity = User.Identity as ClaimsIdentity;
        if (identity == null)
        {
            return;
        }

        var claims = identity.Claims.Where(c => c.Type is not (ClaimConstants.CompanyDb or ClaimConstants.CompanyName or ClaimConstants.SapUserName)).ToList();

        claims.Add(new Claim(ClaimConstants.CompanyDb, session.CompanyDb));
        claims.Add(new Claim(ClaimConstants.CompanyName, companyName));
        claims.Add(new Claim(ClaimConstants.SapUserName, session.SapUserName));

        var newIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(newIdentity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }
}
