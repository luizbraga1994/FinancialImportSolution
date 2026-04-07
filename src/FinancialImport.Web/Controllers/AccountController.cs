using System.Security.Claims;
using FinancialImport.Application.Security;
using FinancialImport.Application.Sap;
using FinancialImport.Web.Context;
using FinancialImport.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IApplicationAuthService _authService;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IApplicationAuthService authService,
        ISapCompanySessionService sapSessionService,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _sapSessionService = sapSessionService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var session = await _authService.SignInAsync(model.Login, model.Password, cancellationToken);

            var claims = new List<Claim>
            {
                new(ClaimConstants.UserId, session.UserId.ToString()),
                new(ClaimConstants.Login, session.Login),
                new(ClaimConstants.Name, session.Name)
            };

            foreach (var profile in session.Profiles)
            {
                claims.Add(new Claim(ClaimTypes.Role, profile));
            }

            foreach (var permission in session.Permissions)
            {
                claims.Add(new Claim("permission", permission));
            }

            foreach (var company in session.AllowedCompanies)
            {
                claims.Add(new Claim("company", company));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return RedirectToAction("Select", "Company");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha no login.");
            model.Error = "Falha ao autenticar. Verifique suas credenciais.";
            return View(model);
        }
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _sapSessionService.SignOutCompanyAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public IActionResult Denied()
    {
        return View();
    }
}
