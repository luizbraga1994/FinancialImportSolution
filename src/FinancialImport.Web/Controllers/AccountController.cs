using System.Security.Claims;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

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
    private readonly AppDbContext _dbContext;
    private readonly ILoginAuditContextAccessor _loginAuditContextAccessor;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IApplicationAuthService authService,
        ISapCompanyDiscoveryService companyDiscovery,
        AppDbContext dbContext,
        ILoginAuditContextAccessor loginAuditContextAccessor,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _companyDiscovery = companyDiscovery;
        _dbContext = dbContext;
        _loginAuditContextAccessor = loginAuditContextAccessor;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        var model = new LoginViewModel { ReturnUrl = returnUrl };
        return View(model);
    }

    /// <summary>
    /// AJAX endpoint: returns companies the user has access to.
    /// Crosses HANA companies with UserCompanyPermission. GlobalAdmin sees all.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/Account/Companies")]
    public async Task<IActionResult> GetCompaniesForUser([FromQuery] string login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
            return Json(new { success = false, message = "Informe o login." });

        try
        {
            // 1. Find user and their allowed companies
            var user = await _dbContext.Users
                .AsNoTracking()
                .Include(u => u.AllowedCompanies)
                .SingleOrDefaultAsync(u => u.Login == login, cancellationToken);

            if (user == null)
            {
                // Don't reveal if user exists or not - return empty
                return Json(new { success = true, companies = Array.Empty<object>(), count = 0 });
            }

            // 2. Get all companies from HANA
            IReadOnlyCollection<SapCompanyInfo> hanaCompanies;
            try
            {
                hanaCompanies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao consultar HANA para listar empresas no login.");
                return Json(new { success = false, message = "Nao foi possivel conectar ao HANA para listar as empresas." });
            }

            var activeHanaCompanies = hanaCompanies.Where(c => c.IsActive).ToList();

            // 3. Filter: GlobalAdmin sees all, regular user sees only allowed
            List<object> result;
            if (user.IsGlobalAdmin)
            {
                result = activeHanaCompanies
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new { value = c.CompanyDb, text = $"{c.CompanyName} ({c.CompanyDb})" })
                    .ToList<object>();
            }
            else
            {
                var allowedDbs = user.AllowedCompanies
                    .Where(c => c.IsActive)
                    .Select(c => c.CompanyDb)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                result = activeHanaCompanies
                    .Where(c => allowedDbs.Contains(c.CompanyDb))
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new { value = c.CompanyDb, text = $"{c.CompanyName} ({c.CompanyDb})" })
                    .ToList<object>();
            }

            return Json(new { success = true, companies = result, count = result.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar empresas para login '{Login}'.", login);
            return Json(new { success = false, message = "Erro ao buscar empresas." });
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Login) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(string.Empty, "Preencha login e senha.");
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.CompanyDb))
        {
            ModelState.AddModelError(string.Empty, "Selecione uma base/empresa.");
            return View(model);
        }

        try
        {
            // 1. Authenticate user
            var session = await _authService.SignInAsync(model.Login, model.Password, cancellationToken);

            // 2. Validate company access
            if (!session.IsGlobalAdmin)
            {
                var hasAccess = session.AllowedCompanies
                    .Any(c => c.Equals(model.CompanyDb, StringComparison.OrdinalIgnoreCase));

                if (!hasAccess)
                {
                    ModelState.AddModelError(string.Empty, "Voce nao tem acesso a esta base/empresa.");
                    return View(model);
                }
            }

            // 3. Resolve company name from HANA
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
                // Fallback: use CompanyDb as name
            }

            // 4. Build claims
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
                claims.Add(new Claim(ClaimConstants.Permission, permission));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal);

            _logger.LogInformation("Usuario '{Login}' autenticado na base '{CompanyDb}'.", model.Login, model.CompanyDb);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

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
