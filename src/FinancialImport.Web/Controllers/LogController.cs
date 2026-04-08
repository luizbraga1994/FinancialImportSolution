using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Web.Controllers;

public class LogIndexViewModel
{
    public string Tab { get; set; } = "system";
    public string? Level { get; set; }
    public string? Search { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public List<SystemLog> SystemLogs { get; set; } = new();
    public List<LoginAudit> LoginAudits { get; set; } = new();
}

[Authorize]
public class LogController : Controller
{
    private readonly AppDbContext _dbContext;

    public LogController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private bool IsGlobalAdmin()
    {
        return User.HasClaim("global_admin", "True") || User.HasClaim("global_admin", "true");
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string tab = "system",
        string? level = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!IsGlobalAdmin())
            return RedirectToAction("AccessDenied", "Account");

        const int pageSize = 50;
        if (page < 1) page = 1;

        var model = new LogIndexViewModel
        {
            Tab = string.IsNullOrWhiteSpace(tab) ? "system" : tab.ToLowerInvariant(),
            Level = level,
            Search = search,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize
        };

        if (model.Tab == "login")
        {
            var query = _dbContext.LoginAudits
                .AsNoTracking()
                .Include(l => l.User)
                .AsQueryable();

            if (from.HasValue)
                query = query.Where(l => l.OccurredAt >= from.Value);
            if (to.HasValue)
                query = query.Where(l => l.OccurredAt <= to.Value.AddDays(1));
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(l => l.LoginProvided.Contains(search) ||
                                         (l.IpAddress != null && l.IpAddress.Contains(search)) ||
                                         (l.FailureReason != null && l.FailureReason.Contains(search)));
            if (!string.IsNullOrWhiteSpace(level))
            {
                if (level.Equals("success", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.Success);
                else if (level.Equals("failure", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => !l.Success);
            }

            model.TotalCount = await query.CountAsync(cancellationToken);
            model.LoginAudits = await query
                .OrderByDescending(l => l.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var query = _dbContext.SystemLogs.AsNoTracking().AsQueryable();

            if (from.HasValue)
                query = query.Where(l => l.OccurredAt >= from.Value);
            if (to.HasValue)
                query = query.Where(l => l.OccurredAt <= to.Value.AddDays(1));
            if (!string.IsNullOrWhiteSpace(level))
                query = query.Where(l => l.Level == level);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(l => l.Message.Contains(search) ||
                                         l.Source.Contains(search) ||
                                         (l.CorrelationId != null && l.CorrelationId.Contains(search)));

            model.TotalCount = await query.CountAsync(cancellationToken);
            model.SystemLogs = await query
                .OrderByDescending(l => l.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        return View(model);
    }
}
