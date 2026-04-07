using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Web.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Api;

[ApiController]
[Route("api/v1/branch-mappings")]
[Authorize]
public sealed class BranchMappingApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public BranchMappingApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<BranchMappingDto>>>> List(
        [FromQuery] string? companyDb = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.BranchMappings.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(companyDb))
            query = query.Where(b => b.CompanyDb == companyDb);

        var items = await query
            .OrderBy(b => b.CompanyDb).ThenBy(b => b.FileBranchCode)
            .Select(b => new BranchMappingDto
            {
                Id = b.Id,
                CompanyDb = b.CompanyDb,
                FileBranchCode = b.FileBranchCode,
                BplId = b.BplId,
                BranchName = b.BranchName,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyCollection<BranchMappingDto>>.Ok(items));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BranchMappingDto>>> Create(
        [FromBody] CreateBranchMappingRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new BranchMapping
        {
            CompanyDb = request.CompanyDb,
            FileBranchCode = request.FileBranchCode,
            BplId = request.BplId,
            BranchName = request.BranchName,
            IsActive = true
        };

        _dbContext.BranchMappings.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), null, ApiResponse<BranchMappingDto>.Ok(new BranchMappingDto
        {
            Id = entity.Id,
            CompanyDb = entity.CompanyDb,
            FileBranchCode = entity.FileBranchCode,
            BplId = entity.BplId,
            BranchName = entity.BranchName,
            IsActive = entity.IsActive
        }));
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.BranchMappings.FindAsync(new object[] { id }, cancellationToken);
        if (entity == null)
            return NotFound(ApiResponse.Fail("Mapeamento nao encontrado."));

        entity.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse.Ok());
    }
}

public sealed class BranchMappingDto
{
    public long Id { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string FileBranchCode { get; set; } = string.Empty;
    public int BplId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CreateBranchMappingRequest
{
    public string CompanyDb { get; set; } = string.Empty;
    public string FileBranchCode { get; set; } = string.Empty;
    public int BplId { get; set; }
    public string BranchName { get; set; } = string.Empty;
}
