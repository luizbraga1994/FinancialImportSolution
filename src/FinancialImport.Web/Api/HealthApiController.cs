using FinancialImport.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Api;

[ApiController]
[Route("api/v1/health")]
[AllowAnonymous]
public sealed class HealthApiController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public HealthApiController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Check(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.Database.CanConnectAsync(cancellationToken);
            return Ok(new { Status = "Healthy", Database = "Connected", Timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { Status = "Unhealthy", Database = "Disconnected", Error = ex.Message, Timestamp = DateTime.UtcNow });
        }
    }
}
