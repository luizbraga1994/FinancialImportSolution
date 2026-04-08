using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Controllers;

public class ProfileFormViewModel
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<PermissionCheckItem> AvailablePermissions { get; set; } = new();
}

public class PermissionCheckItem
{
    public long PermissionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool IsSelected { get; set; }
}

[Authorize]
public class ProfileController : Controller
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(AppDbContext dbContext, ILogger<ProfileController> logger)
    {
        _dbContext = dbContext;
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

        var profiles = await _dbContext.Profiles
            .AsNoTracking()
            .Include(p => p.Permissions).ThenInclude(pp => pp.Permission)
            .Include(p => p.Users)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return View(profiles);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var model = new ProfileFormViewModel { IsActive = true };
        await PopulatePermissions(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProfileFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(string.Empty, "Informe o nome do perfil.");
            await PopulatePermissions(model, cancellationToken);
            return View(model);
        }

        var exists = await _dbContext.Profiles.AnyAsync(p => p.Name == model.Name, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Ja existe um perfil com esse nome.");
            await PopulatePermissions(model, cancellationToken);
            return View(model);
        }

        var profile = new Profile
        {
            Name = model.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            IsActive = model.IsActive
        };

        _dbContext.Profiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var selectedPermissionIds = model.AvailablePermissions
            .Where(p => p.IsSelected)
            .Select(p => p.PermissionId)
            .ToList();

        foreach (var permissionId in selectedPermissionIds)
        {
            _dbContext.ProfilePermissions.Add(new ProfilePermission
            {
                ProfileId = profile.Id,
                PermissionId = permissionId
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Perfil '{Profile}' criado por '{Admin}'.", profile.Name,
            User.FindFirst("login")?.Value ?? "system");
        TempData["Success"] = $"Perfil '{profile.Name}' criado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var profile = await _dbContext.Profiles
            .Include(p => p.Permissions)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile == null)
            return NotFound();

        var model = new ProfileFormViewModel
        {
            Id = profile.Id,
            Name = profile.Name,
            Description = profile.Description,
            IsActive = profile.IsActive
        };

        await PopulatePermissions(model, cancellationToken);

        var currentPermissionIds = profile.Permissions.Select(pp => pp.PermissionId).ToHashSet();
        foreach (var p in model.AvailablePermissions)
            p.IsSelected = currentPermissionIds.Contains(p.PermissionId);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProfileFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(string.Empty, "Informe o nome do perfil.");
            await PopulatePermissions(model, cancellationToken);
            return View(model);
        }

        var profile = await _dbContext.Profiles
            .Include(p => p.Permissions)
            .SingleOrDefaultAsync(p => p.Id == model.Id, cancellationToken);

        if (profile == null)
            return NotFound();

        var duplicate = await _dbContext.Profiles
            .AnyAsync(p => p.Id != model.Id && p.Name == model.Name, cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError(string.Empty, "Ja existe outro perfil com esse nome.");
            await PopulatePermissions(model, cancellationToken);
            return View(model);
        }

        profile.Name = model.Name.Trim();
        profile.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        profile.IsActive = model.IsActive;

        _dbContext.ProfilePermissions.RemoveRange(profile.Permissions);

        var selectedPermissionIds = model.AvailablePermissions
            .Where(p => p.IsSelected)
            .Select(p => p.PermissionId)
            .ToList();

        foreach (var permissionId in selectedPermissionIds)
        {
            _dbContext.ProfilePermissions.Add(new ProfilePermission
            {
                ProfileId = profile.Id,
                PermissionId = permissionId
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Perfil '{Profile}' atualizado por '{Admin}'.", profile.Name,
            User.FindFirst("login")?.Value ?? "system");
        TempData["Success"] = $"Perfil '{profile.Name}' atualizado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(long id, CancellationToken cancellationToken)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        var profile = await _dbContext.Profiles.FindAsync(new object[] { id }, cancellationToken);
        if (profile == null) return NotFound();

        profile.IsActive = !profile.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var status = profile.IsActive ? "ativado" : "desativado";
        TempData["Success"] = $"Perfil '{profile.Name}' {status}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulatePermissions(ProfileFormViewModel model, CancellationToken cancellationToken)
    {
        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        if (model.AvailablePermissions.Count == 0)
        {
            model.AvailablePermissions = permissions.Select(p => new PermissionCheckItem
            {
                PermissionId = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                Group = p.Group
            }).ToList();
        }
    }
}
