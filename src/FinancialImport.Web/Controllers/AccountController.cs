using System.Security.Claims;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FinancialImport.Web.Controllers;

public class LoginViewModel
{
    public string Login { get; set; } = string.Empty;
    public string CompanyDb { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
    public List<SelectListItem> Companies { get; set; } = new();
}

public class AccountController : Controller
{
    private readonly IApplicationAuthService _authService;
    private readonly ISapCompanyDiscoveryService _companyDiscovery;
    private readonly ILoginAuditContextAccessor _loginAuditContextAccessor;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IApplicationAuthService authService,
        ISapCompanyDiscoveryService companyDiscovery,
        ILoginAuditContextAccessor loginAuditContextAccessor,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _companyDiscovery = companyDiscovery;
        _loginAuditContextAccessor = loginAuditContextAccessor;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var model = new LoginViewModel { ReturnUrl = returnUrl };
        await LoadCompaniesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Login) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(string.Empty, "Preencha login e senha.");
            await LoadCompaniesAsync(model, cancellationToken);
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.CompanyDb))
        {
            ModelState.AddModelError(string.Empty, "Selecione uma base/empresa.");
            await LoadCompaniesAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            var session = await _authService.SignInAsync(model.Login, model.Password, cancellationToken);

            // Find company name from the selected CompanyDb
            var companyName = model.CompanyDb;
            try
            {
                var companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
                var selected = companies.FirstOrDefault(c =>
                    c.CompanyDb.Equals(model.CompanyDb, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    companyName = selected.CompanyName;
            }
            catch
            {
                // If HANA is unavailable, use CompanyDb as name
            }

            var claims = new List<Claim>
            {
                new(ClaimConstants.UserId, session.UserId.ToString()),
                new(ClaimConstants.Login, session.Login),
                new(ClaimConstants.Name, session.Name),
                new(ClaimConstants.GlobalAdmin, session.IsGlobalAdmin.ToString()),
                new(ClaimConstants.CompanyDb, model.CompanyDb),
                new(ClaimConstants.CompanyName, companyName),
            };

            foreach (var permission in session.Permissions)
            {
                claims.Add(new Claim(ClaimConstants.Permission, permission));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            _logger.LogInformation("Usuario '{Login}' autenticado na base '{CompanyDb}'.", model.Login, model.CompanyDb);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadCompaniesAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task LoadCompaniesAsync(LoginViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            var companies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            model.Companies = companies
                .Where(c => c.IsActive)
                .OrderBy(c => c.CompanyName)
                .Select(c => new SelectListItem
                {
                    Value = c.CompanyDb,
                    Text = $"{c.CompanyName} ({c.CompanyDb})",
                    Selected = c.CompanyDb.Equals(model.CompanyDb, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel carregar empresas do HANA para tela de login.");
            model.Companies = new List<SelectListItem>();
            ViewData["CompanyError"] = "Nao foi possivel carregar as empresas. Verifique a conexao com o HANA.";
        }
    }
}
