using System.Security.Claims;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Logging;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class CompanyController : Controller
{
    private readonly ISapCompanyDiscoveryService _discoveryService;
    private readonly ISapCompanySessionService _sessionService;
    private readonly ISystemSettingsService _settings;
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogger _audit;

    public CompanyController(
        ISapCompanyDiscoveryService discoveryService,
        ISapCompanySessionService sessionService,
        ISystemSettingsService settings,
        AppDbContext dbContext,
        IAuditLogger audit)
    {
        _discoveryService = discoveryService;
        _sessionService = sessionService;
        _settings = settings;
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companies = await _discoveryService.GetAvailableCompaniesAsync(cancellationToken);

        var isGlobalAdmin = User.HasClaim("global_admin", "True") || User.HasClaim("global_admin", "true");
        if (!isGlobalAdmin)
        {
            var userIdClaim = User.FindFirst(ClaimConstants.UserId)?.Value;
            if (long.TryParse(userIdClaim, out var userId))
            {
                var allowedDbs = await _dbContext.Set<Domain.Entities.UserCompanyPermission>()
                    .AsNoTracking()
                    .Where(p => p.UserId == userId && p.IsActive)
                    .Select(p => p.CompanyDb)
                    .ToListAsync(cancellationToken);

                var allowedSet = new HashSet<string>(allowedDbs, StringComparer.OrdinalIgnoreCase);
                companies = companies.Where(c => allowedSet.Contains(c.CompanyDb)).ToList();
            }
        }

        return View(companies);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(string companyDb, CancellationToken cancellationToken)
    {
        var userLogin = User.FindFirst("login")?.Value ?? "desconhecido";

        // The SAP Service Layer login can take 5-10s. We deliberately do NOT pass
        // the request cancellation token to it: if the user clicks the card again
        // (impatience) or navigates away mid-login, a cancelled login leaves the
        // session store in a half-dirty state AND throws a generic 500 that hides
        // the real cause. A fresh CancellationToken bounded by a 60s timeout gives
        // SAP plenty of headroom while still preventing runaway requests.
        using var sapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        SapLoginResult result;
        try
        {
            result = await _sessionService.SignInCompanyAsync(
                companyDb,
                _settings.Get("Sap:UserName") ?? "",
                _settings.Get("Sap:Password") ?? "",
                sapTimeout.Token);
        }
        catch (OperationCanceledException) when (sapTimeout.IsCancellationRequested)
        {
            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Error,
                Category = LogCategories.Audit,
                Source = nameof(CompanyController),
                Operation = "SelecionarEmpresa",
                Message = $"Timeout ao selecionar empresa '{companyDb}' por '{userLogin}' (60s). SAP nao respondeu a tempo.",
                CompanyDb = companyDb
            }, CancellationToken.None);

            TempData["Error"] = "Tempo esgotado ao conectar no SAP. Tente novamente.";
            var timeoutCompanies = await _discoveryService.GetAvailableCompaniesAsync(CancellationToken.None);
            return View(nameof(Index), timeoutCompanies);
        }

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
            }, CancellationToken.None);

            TempData["Error"] = result.ErrorMessage ?? "Falha ao conectar na empresa selecionada.";
            var failureCompanies = await _discoveryService.GetAvailableCompaniesAsync(CancellationToken.None);
            return View(nameof(Index), failureCompanies);
        }

        // Re-issue the authentication cookie so the company_db / company_name
        // claims reflect the new selection. Without this the UI keeps showing
        // the previous company in the header and Historico/Import would still
        // be scoped to the old company (via the claim lookup in controllers).
        var companies = await _discoveryService.GetAvailableCompaniesAsync(CancellationToken.None);
        var selected = companies.FirstOrDefault(c =>
            c.CompanyDb.Equals(companyDb, StringComparison.OrdinalIgnoreCase));
        var newCompanyName = selected?.CompanyName ?? companyDb;

        var existingClaims = User.Claims
            .Where(c => c.Type != ClaimConstants.CompanyDb && c.Type != ClaimConstants.CompanyName)
            .ToList();
        existingClaims.Add(new Claim(ClaimConstants.CompanyDb, companyDb));
        existingClaims.Add(new Claim(ClaimConstants.CompanyName, newCompanyName));

        var identity = new ClaimsIdentity(existingClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Info,
            Category = LogCategories.Audit,
            Source = nameof(CompanyController),
            Operation = "SelecionarEmpresa",
            Message = $"Empresa '{companyDb}' ({newCompanyName}) selecionada por '{userLogin}'. Sessao SAP estabelecida.",
            CompanyDb = companyDb
        }, CancellationToken.None);

        return RedirectToAction("Index", "Home");
    }
}
