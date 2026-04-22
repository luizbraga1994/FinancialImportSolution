using System.Security.Claims;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Application.Settings;
using FinancialImport.Shared.Logging;
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

public class ChangePasswordViewModel
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountController : Controller
{
    private readonly IApplicationAuthService _authService;
    private readonly ISapCompanyDiscoveryService _companyDiscovery;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly ISystemSettingsService _settings;
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _hasher;
    private readonly IAuditLogger _audit;
    private readonly ILoginAuditContextAccessor _loginAuditContextAccessor;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IApplicationAuthService authService,
        ISapCompanyDiscoveryService companyDiscovery,
        ISapCompanySessionService sapSessionService,
        ISystemSettingsService settings,
        AppDbContext dbContext,
        PasswordHasher hasher,
        IAuditLogger audit,
        ILoginAuditContextAccessor loginAuditContextAccessor,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _companyDiscovery = companyDiscovery;
        _sapSessionService = sapSessionService;
        _settings = settings;
        _dbContext = dbContext;
        _hasher = hasher;
        _audit = audit;
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
                return Json(new { success = true, userExists = false, companies = Array.Empty<object>(), count = 0 });
            }

            if (!user.IsActive)
            {
                return Json(new { success = true, userExists = false, companies = Array.Empty<object>(), count = 0 });
            }

            // 2. Get all companies from HANA (optional — falls back to DB-only if HANA unavailable)
            IReadOnlyCollection<SapCompanyInfo> hanaCompanies = Array.Empty<SapCompanyInfo>();
            bool hanaAvailable = true;
            try
            {
                hanaCompanies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HANA indisponivel durante login de '{Login}' — usando apenas base local.", login);
                hanaAvailable = false;
            }

            // 3. Filter: GlobalAdmin sees all, regular user sees only allowed
            List<object> result;

            if (!hanaAvailable)
            {
                if (user.IsGlobalAdmin)
                {
                    // GlobalAdmin without HANA: return empty list so the UI offers manual entry
                    return Json(new
                    {
                        success = true,
                        userExists = true,
                        companies = Array.Empty<object>(),
                        count = 0,
                        hanaUnavailable = true,
                        hanaWarning = "HANA indisponivel. Digite o codigo da base manualmente."
                    });
                }

                // Regular user: show their allowed companies from the local DB (no HANA names)
                result = user.AllowedCompanies
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CompanyDb)
                    .Select(c => (object)new { value = c.CompanyDb, text = c.CompanyDb })
                    .ToList();

                return Json(new
                {
                    success = true,
                    userExists = true,
                    companies = result,
                    count = result.Count,
                    hanaUnavailable = true,
                    hanaWarning = "HANA indisponivel. Exibindo apenas bases configuradas no sistema."
                });
            }

            var activeHanaCompanies = hanaCompanies.Where(c => c.IsActive).ToList();

            if (user.IsGlobalAdmin)
            {
                result = activeHanaCompanies
                    .OrderBy(c => c.CompanyName)
                    .Select(c => (object)new { value = c.CompanyDb, text = $"{c.CompanyName} ({c.CompanyDb})" })
                    .ToList();
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
                    .Select(c => (object)new { value = c.CompanyDb, text = $"{c.CompanyName} ({c.CompanyDb})" })
                    .ToList();
            }

            return Json(new { success = true, userExists = true, companies = result, count = result.Count });
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
            HttpContext.User = principal;

            _logger.LogInformation("Usuario '{Login}' autenticado na base '{CompanyDb}'.", model.Login, model.CompanyDb);

            // 5. Auto-establish SAP Service Layer session (so the user
            //    can confirm imports immediately without visiting Empresas).
            try
            {
                var sapResult = await _sapSessionService.SignInCompanyAsync(
                    model.CompanyDb,
                    _settings.Get("Sap:UserName") ?? "",
                    _settings.Get("Sap:Password") ?? "",
                    cancellationToken);

                if (sapResult.Success)
                    _logger.LogInformation("Sessao SAP estabelecida para '{CompanyDb}'.", model.CompanyDb);
                else
                    _logger.LogWarning("Falha ao conectar ao SAP para '{CompanyDb}': {Error}", model.CompanyDb, sapResult.ErrorMessage);
            }
            catch (Exception sapEx)
            {
                // SAP connection failure should NOT block login — the user
                // can still browse/upload. They'll see a warning at confirm time.
                _logger.LogWarning(sapEx, "Nao foi possivel estabelecer sessao SAP durante login para '{CompanyDb}'.", model.CompanyDb);
            }

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

    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword))
        {
            ModelState.AddModelError(string.Empty, "Preencha todos os campos.");
            return View(model);
        }

        if (model.NewPassword != model.ConfirmPassword)
        {
            ModelState.AddModelError(string.Empty, "A nova senha e a confirmacao nao conferem.");
            return View(model);
        }

        if (model.NewPassword.Length < 6)
        {
            ModelState.AddModelError(string.Empty, "A nova senha deve ter no minimo 6 caracteres.");
            return View(model);
        }

        var loginClaim = User.FindFirst(ClaimConstants.Login)?.Value;
        if (string.IsNullOrWhiteSpace(loginClaim))
        {
            ModelState.AddModelError(string.Empty, "Sessao invalida. Faca login novamente.");
            return View(model);
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Login == loginClaim, cancellationToken);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Usuario nao encontrado.");
            return View(model);
        }

        if (!_hasher.Verify(model.CurrentPassword, user.PasswordSalt!, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Senha atual incorreta.");
            return View(model);
        }

        var (hash, salt) = _hasher.HashPassword(model.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Info,
            Category = LogCategories.Audit,
            Source = nameof(AccountController),
            Operation = "AlterarSenha",
            Message = $"Senha alterada pelo usuario '{loginClaim}'."
        }, cancellationToken);

        TempData["Success"] = "Senha alterada com sucesso.";
        return RedirectToAction("Index", "Home");
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
