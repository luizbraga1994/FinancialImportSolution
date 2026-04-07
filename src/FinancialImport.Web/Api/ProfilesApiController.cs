using FinancialImport.Domain.Constants;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Web.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Api;

[ApiController]
[Route("api/v1/profiles")]
[Authorize(Policy = PermissionCodes.GerenciarPerfis)]
public sealed class ProfilesApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ProfilesApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<ProfileDto>>>> List(CancellationToken cancellationToken)
    {
        var profiles = await _dbContext.Profiles
            .Include(p => p.Permissions).ThenInclude(pp => pp.Permission)
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var dtos = profiles.Select(MapToDto).ToArray();
        return Ok(ApiResponse<IReadOnlyCollection<ProfileDto>>.Ok(dtos));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProfileDto>>> Get(long id, CancellationToken cancellationToken)
    {
        var profile = await _dbContext.Profiles
            .Include(p => p.Permissions).ThenInclude(pp => pp.Permission)
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile == null)
            return NotFound(ApiResponse<ProfileDto>.Fail("Perfil nao encontrado."));

        return Ok(ApiResponse<ProfileDto>.Ok(MapToDto(profile)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProfileDto>>> Create(
        [FromBody] CreateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.Profiles.AnyAsync(p => p.Name == request.Name, cancellationToken))
            return BadRequest(ApiResponse<ProfileDto>.Fail("Nome de perfil ja existe."));

        var profile = new Profile
        {
            Name = request.Name,
            Description = request.Description,
            IsActive = true
        };

        _dbContext.Profiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var permissionId in request.PermissionIds)
        {
            _dbContext.ProfilePermissions.Add(new ProfilePermission
            {
                ProfileId = profile.Id,
                PermissionId = permissionId
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await _dbContext.Profiles
            .Include(p => p.Permissions).ThenInclude(pp => pp.Permission)
            .SingleAsync(p => p.Id == profile.Id, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = profile.Id }, ApiResponse<ProfileDto>.Ok(MapToDto(created)));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProfileDto>>> Update(
        long id,
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.Profiles
            .Include(p => p.Permissions)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile == null)
            return NotFound(ApiResponse<ProfileDto>.Fail("Perfil nao encontrado."));

        profile.Name = request.Name;
        profile.Description = request.Description;
        profile.IsActive = request.IsActive;

        _dbContext.ProfilePermissions.RemoveRange(profile.Permissions);
        foreach (var permissionId in request.PermissionIds)
        {
            _dbContext.ProfilePermissions.Add(new ProfilePermission
            {
                ProfileId = profile.Id,
                PermissionId = permissionId
            });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await _dbContext.Profiles
            .Include(p => p.Permissions).ThenInclude(pp => pp.Permission)
            .SingleAsync(p => p.Id == profile.Id, cancellationToken);

        return Ok(ApiResponse<ProfileDto>.Ok(MapToDto(updated)));
    }

    private static ProfileDto MapToDto(Profile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        IsActive = profile.IsActive,
        Permissions = profile.Permissions
            .Where(pp => pp.Permission != null)
            .Select(pp => new PermissionDto
            {
                Id = pp.Permission!.Id,
                Code = pp.Permission.Code,
                Name = pp.Permission.Name,
                Description = pp.Permission.Description,
                Group = pp.Permission.Group,
                IsActive = pp.Permission.IsActive
            }).ToArray()
    };
}
