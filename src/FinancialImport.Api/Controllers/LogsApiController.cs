using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Api.Controllers;

[ApiController]
[Route("api/v1/logs")]
[Authorize(Policy = PermissionCodes.VisualizarLogs)]
public sealed class LogsApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public LogsApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("system")]
    public async Task<ActionResult<ApiResponse<PagedResult<SystemLogDto>>>> SystemLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? level = null,
        [FromQuery] string? companyDb = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SystemLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(l => l.Level == level);
        if (!string.IsNullOrWhiteSpace(companyDb))
            query = query.Where(l => l.CompanyDb == companyDb);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new SystemLogDto
            {
                Id = l.Id,
                OccurredAt = l.OccurredAt,
                Level = l.Level,
                Source = l.Source,
                UserId = l.UserId,
                CompanyDb = l.CompanyDb,
                CorrelationId = l.CorrelationId,
                Message = l.Message,
                Details = l.Details
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<PagedResult<SystemLogDto>>.Ok(new PagedResult<SystemLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        }));
    }

    [HttpGet("audit")]
    public async Task<ActionResult<ApiResponse<PagedResult<LoginAuditDto>>>> AuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LoginAudits.AsNoTracking().AsQueryable();

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoginAuditDto
            {
                Id = l.Id,
                UserId = l.UserId,
                LoginProvided = l.LoginProvided,
                Success = l.Success,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                OccurredAt = l.OccurredAt,
                FailureReason = l.FailureReason
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<PagedResult<LoginAuditDto>>.Ok(new PagedResult<LoginAuditDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        }));
    }
}

public sealed class SystemLogDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public long? UserId { get; set; }
    public string? CompanyDb { get; set; }
    public string? CorrelationId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public sealed class LoginAuditDto
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string LoginProvided { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? FailureReason { get; set; }
}
