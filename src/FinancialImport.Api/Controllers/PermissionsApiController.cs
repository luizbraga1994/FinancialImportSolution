using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Api.Controllers;

[ApiController]
[Route("api/v1/permissions")]
[Authorize(Policy = PermissionCodes.GerenciarPermissoes)]
public sealed class PermissionsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public PermissionsApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PermissionDto>>>> List(CancellationToken cancellationToken)
    {
        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Group).ThenBy(p => p.Name)
            .Select(p => new PermissionDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                Group = p.Group,
                IsActive = p.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyCollection<PermissionDto>>.Ok(permissions));
    }
}
