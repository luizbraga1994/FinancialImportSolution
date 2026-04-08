using FinancialImport.Application.Sap;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Controllers;

public class UserFormViewModel
{
    public long Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }
    public bool IsGlobalAdmin { get; set; }

    // Profile checkboxes
    public List<ProfileCheckItem> AvailableProfiles { get; set; } = new();

    // Company checkboxes
    public List<CompanyCheckItem> AvailableCompanies { get; set; } = new();
}

public class ProfileCheckItem
{
    public long ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSelected { get; set; }
}

public class CompanyCheckItem
{
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

[Authorize]
public class UserController : Controller
{
    private readonly AppDbContext _dbContext;
    private readonly ISapCompanyDiscoveryService _companyDiscovery;
    private readonly PasswordHasher _passwordHasher;
    private readonly ILogger<UserController> _logger;

    public UserController(
        AppDbContext dbContext,
        ISapCompanyDiscoveryService companyDiscovery,
        PasswordHasher passwordHasher,
        ILogger<UserController> logger)
    {
        _dbContext = dbContext;
        _companyDiscovery = companyDiscovery;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    private bool IsGlobalAdmin()
    {
        return User.HasClaim("global_admin", "True") || User.HasClaim("global_admin", "true");
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Profiles).ThenInclude(up => up.Profile)
            .Include(u => u.AllowedCompanies)
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);

        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var model = new UserFormViewModel { IsActive = true };
        await PopulateCheckLists(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        if (string.IsNullOrWhiteSpace(model.Login) || string.IsNullOrWhiteSpace(model.Name) ||
            string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(string.Empty, "Preencha todos os campos obrigatorios.");
            await PopulateCheckLists(model, cancellationToken);
            return View(model);
        }

        // Check duplicates
        var exists = await _dbContext.Users.AnyAsync(u =>
            u.Login == model.Login || u.Email == model.Email, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Login ou Email ja cadastrado.");
            await PopulateCheckLists(model, cancellationToken);
            return View(model);
        }

        var (hash, salt) = _passwordHasher.HashPassword(model.Password);

        var user = new User
        {
            Login = model.Login.Trim(),
            Name = model.Name.Trim(),
            Email = model.Email.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = model.IsActive,
            IsBlocked = model.IsBlocked,
            IsGlobalAdmin = model.IsGlobalAdmin,
            CreatedAt = DateTime.Now,
            CreatedBy = User.FindFirst("login")?.Value ?? "system"
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Save profiles
        var selectedProfileIds = model.AvailableProfiles
            .Where(p => p.IsSelected)
            .Select(p => p.ProfileId)
            .ToList();

        foreach (var profileId in selectedProfileIds)
        {
            _dbContext.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                ProfileId = profileId
            });
        }

        // Save company permissions
        var selectedCompanies = model.AvailableCompanies
            .Where(c => c.IsSelected)
            .Select(c => c.CompanyDb)
            .ToList();

        foreach (var companyDb in selectedCompanies)
        {
            _dbContext.UserCompanyPermissions.Add(new UserCompanyPermission
            {
                UserId = user.Id,
                CompanyDb = companyDb,
                IsActive = true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Usuario '{Login}' criado por '{Admin}'.", user.Login, user.CreatedBy);
        TempData["Success"] = $"Usuario '{user.Name}' criado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var user = await _dbContext.Users
            .Include(u => u.Profiles)
            .Include(u => u.AllowedCompanies)
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
            return NotFound();

        var model = new UserFormViewModel
        {
            Id = user.Id,
            Login = user.Login,
            Name = user.Name,
            Email = user.Email,
            IsActive = user.IsActive,
            IsBlocked = user.IsBlocked,
            IsGlobalAdmin = user.IsGlobalAdmin
        };

        await PopulateCheckLists(model, cancellationToken);

        // Mark selected profiles
        var userProfileIds = user.Profiles.Select(p => p.ProfileId).ToHashSet();
        foreach (var p in model.AvailableProfiles)
            p.IsSelected = userProfileIds.Contains(p.ProfileId);

        // Mark selected companies
        var userCompanyDbs = user.AllowedCompanies
            .Where(c => c.IsActive)
            .Select(c => c.CompanyDb)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in model.AvailableCompanies)
            c.IsSelected = userCompanyDbs.Contains(c.CompanyDb);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        if (string.IsNullOrWhiteSpace(model.Login) || string.IsNullOrWhiteSpace(model.Name) ||
            string.IsNullOrWhiteSpace(model.Email))
        {
            ModelState.AddModelError(string.Empty, "Preencha todos os campos obrigatorios.");
            await PopulateCheckLists(model, cancellationToken);
            return View(model);
        }

        var user = await _dbContext.Users
            .Include(u => u.Profiles)
            .Include(u => u.AllowedCompanies)
            .SingleOrDefaultAsync(u => u.Id == model.Id, cancellationToken);

        if (user == null)
            return NotFound();

        // Check duplicate login/email (excluding self)
        var duplicate = await _dbContext.Users.AnyAsync(u =>
            u.Id != model.Id && (u.Login == model.Login || u.Email == model.Email), cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError(string.Empty, "Login ou Email ja cadastrado por outro usuario.");
            await PopulateCheckLists(model, cancellationToken);
            return View(model);
        }

        user.Login = model.Login.Trim();
        user.Name = model.Name.Trim();
        user.Email = model.Email.Trim();
        user.IsActive = model.IsActive;
        user.IsBlocked = model.IsBlocked;
        user.IsGlobalAdmin = model.IsGlobalAdmin;

        // Update password if provided
        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var (hash, salt) = _passwordHasher.HashPassword(model.Password);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
        }

        // Sync profiles: remove old, add new
        _dbContext.UserProfiles.RemoveRange(user.Profiles);

        var selectedProfileIds = model.AvailableProfiles
            .Where(p => p.IsSelected)
            .Select(p => p.ProfileId)
            .ToList();

        foreach (var profileId in selectedProfileIds)
        {
            _dbContext.UserProfiles.Add(new UserProfile
            {
                UserId = user.Id,
                ProfileId = profileId
            });
        }

        // Sync companies: remove old, add new
        _dbContext.UserCompanyPermissions.RemoveRange(user.AllowedCompanies);

        var selectedCompanies = model.AvailableCompanies
            .Where(c => c.IsSelected)
            .Select(c => c.CompanyDb)
            .ToList();

        foreach (var companyDb in selectedCompanies)
        {
            _dbContext.UserCompanyPermissions.Add(new UserCompanyPermission
            {
                UserId = user.Id,
                CompanyDb = companyDb,
                IsActive = true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Usuario '{Login}' atualizado por '{Admin}'.", user.Login,
            User.FindFirst("login")?.Value ?? "system");
        TempData["Success"] = $"Usuario '{user.Name}' atualizado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(long id, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var status = user.IsActive ? "ativado" : "desativado";
        TempData["Success"] = $"Usuario '{user.Name}' {status}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(long id, string newPassword, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "A senha deve ter no minimo 6 caracteres.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null) return NotFound();

        var (hash, salt) = _passwordHasher.HashPassword(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"Senha do usuario '{user.Name}' redefinida com sucesso.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task PopulateCheckLists(UserFormViewModel model, CancellationToken cancellationToken)
    {
        // Load profiles
        var profiles = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        if (model.AvailableProfiles.Count == 0)
        {
            model.AvailableProfiles = profiles.Select(p => new ProfileCheckItem
            {
                ProfileId = p.Id,
                Name = p.Name,
                Description = p.Description
            }).ToList();
        }

        // Load companies from HANA
        try
        {
            var hanaCompanies = await _companyDiscovery.GetAvailableCompaniesAsync(cancellationToken);
            var activeCompanies = hanaCompanies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToList();

            if (model.AvailableCompanies.Count == 0)
            {
                model.AvailableCompanies = activeCompanies.Select(c => new CompanyCheckItem
                {
                    CompanyDb = c.CompanyDb,
                    CompanyName = $"{c.CompanyName} ({c.CompanyDb})"
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel carregar empresas do HANA.");
            // Keep existing list (may be empty on create, or preserved on re-render)
        }
    }
}
