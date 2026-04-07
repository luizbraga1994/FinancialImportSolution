using System.Security.Claims;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Security;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

public record LoginViewModel(string Login, string Password, string? ReturnUrl);

public class AccountController : Controller
{
    private readonly IApplicationAuthService _authService;
    private readonly ILoginAuditContextAccessor _loginAuditContextAccessor;

    public AccountController(
        IApplicationAuthService authService,
        ILoginAuditContextAccessor loginAuditContextAccessor)
    {
        _authService = authService;
        _loginAuditContextAccessor = loginAuditContextAccessor;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var session = await _authService.SignInAsync(model.Login, model.Password);

            var claims = new List<Claim>
            {
                new(ClaimConstants.UserId, session.UserId.ToString()),
                new(ClaimConstants.Login, session.Login),
                new(ClaimConstants.Name, session.Name),
                new(ClaimConstants.GlobalAdmin, session.IsGlobalAdmin.ToString()),
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

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
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
}
