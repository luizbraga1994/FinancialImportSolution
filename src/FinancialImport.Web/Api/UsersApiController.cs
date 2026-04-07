using FinancialImport.Application.Abstractions;
using FinancialImport.Domain.Constants;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Shared.Abstractions;
using FinancialImport.Web.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Api;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = PermissionCodes.GerenciarUsuarios)]
public sealed class UsersApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _hasher;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public UsersApiController(AppDbContext dbContext, PasswordHasher hasher, IUserContext userContext, IClock clock)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _userContext = userContext;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Users
            .Include(u => u.Profiles).ThenInclude(up => up.Profile)
            .Include(u => u.AllowedCompanies)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Login.Contains(search) || u.Name.Contains(search) || u.Email.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(u => u.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = users.Select(MapToDto).ToArray();

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        }));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Get(long id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profiles).ThenInclude(up => up.Profile)
            .Include(u => u.AllowedCompanies)
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
            return NotFound(ApiResponse<UserDto>.Fail("Usuario nao encontrado."));

        return Ok(ApiResponse<UserDto>.Ok(MapToDto(user)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.AnyAsync(u => u.Login == request.Login, cancellationToken))
            return BadRequest(ApiResponse<UserDto>.Fail("Login ja existe."));

        if (await _dbContext.Users.AnyAsync(u => u.Email == request.Email, cancellationToken))
            return BadRequest(ApiResponse<UserDto>.Fail("Email ja existe."));

        var (hash, salt) = _hasher.HashPassword(request.Password);

        var user = new User
        {
            Login = request.Login,
            Name = request.Name,
            Email = request.Email,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = true,
            IsBlocked = false,
            IsGlobalAdmin = request.IsGlobalAdmin,
            CreatedAt = _clock.Now,
            CreatedBy = _userContext.Login ?? "SYSTEM"
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var profileId in request.ProfileIds)
        {
            _dbContext.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileId = profileId });
        }

        foreach (var companyDb in request.AllowedCompanyDbs)
        {
            _dbContext.UserCompanyPermissions.Add(new UserCompanyPermission
            {
                UserId = user.Id,
                CompanyDb = companyDb,
                IsActive = true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await _dbContext.Users
            .Include(u => u.Profiles).ThenInclude(up => up.Profile)
            .Include(u => u.AllowedCompanies)
            .SingleAsync(u => u.Id == user.Id, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = user.Id }, ApiResponse<UserDto>.Ok(MapToDto(created)));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(
        long id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profiles)
            .Include(u => u.AllowedCompanies)
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user == null)
            return NotFound(ApiResponse<UserDto>.Fail("Usuario nao encontrado."));

        user.Name = request.Name;
        user.Email = request.Email;
        user.IsActive = request.IsActive;
        user.IsBlocked = request.IsBlocked;
        user.IsGlobalAdmin = request.IsGlobalAdmin;

        _dbContext.UserProfiles.RemoveRange(user.Profiles);
        foreach (var profileId in request.ProfileIds)
        {
            _dbContext.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileId = profileId });
        }

        _dbContext.UserCompanyPermissions.RemoveRange(user.AllowedCompanies);
        foreach (var companyDb in request.AllowedCompanyDbs)
        {
            _dbContext.UserCompanyPermissions.Add(new UserCompanyPermission
            {
                UserId = user.Id,
                CompanyDb = companyDb,
                IsActive = true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await _dbContext.Users
            .Include(u => u.Profiles).ThenInclude(up => up.Profile)
            .Include(u => u.AllowedCompanies)
            .SingleAsync(u => u.Id == user.Id, cancellationToken);

        return Ok(ApiResponse<UserDto>.Ok(MapToDto(updated)));
    }

    [HttpPost("{id:long}/change-password")]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        long id,
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null)
            return NotFound(ApiResponse.Fail("Usuario nao encontrado."));

        var (hash, salt) = _hasher.HashPassword(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse.Ok());
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Deactivate(long id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user == null)
            return NotFound(ApiResponse.Fail("Usuario nao encontrado."));

        user.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse.Ok());
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        Login = user.Login,
        Name = user.Name,
        Email = user.Email,
        IsActive = user.IsActive,
        IsBlocked = user.IsBlocked,
        IsGlobalAdmin = user.IsGlobalAdmin,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        Profiles = user.Profiles.Select(p => p.Profile?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToArray(),
        AllowedCompanies = user.AllowedCompanies.Where(c => c.IsActive).Select(c => c.CompanyDb).ToArray()
    };
}
