using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Sap;

public sealed class SapSessionStore : ISapSessionStore
{
    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;

    public SapSessionStore(AppDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<SapSessionContext?> GetActiveSessionAsync(long userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CompanyUserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderByDescending(s => s.LoginAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null || session.ExpiresAt <= _clock.Now)
        {
            return null;
        }

        return new SapSessionContext
        {
            CompanyDb = session.CompanyDb,
            CompanyName = session.CompanyName,
            SessionId = session.SessionId,
            RouteId = session.RouteId,
            ExpiresAt = session.ExpiresAt,
            SapUserName = session.SapUserName
        };
    }

    public async Task UpsertSessionAsync(long userId, SapSessionContext session, CancellationToken cancellationToken = default)
    {
        var activeSessions = await _dbContext.CompanyUserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var active in activeSessions)
        {
            active.IsActive = false;
        }

        var entity = new CompanyUserSession
        {
            UserId = userId,
            CompanyDb = session.CompanyDb,
            CompanyName = session.CompanyName,
            SapUserName = session.SapUserName,
            SessionId = session.SessionId,
            RouteId = session.RouteId,
            ExpiresAt = session.ExpiresAt,
            IsActive = true,
            LoginAt = _clock.Now
        };

        _dbContext.CompanyUserSessions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateSessionAsync(long userId, CancellationToken cancellationToken = default)
    {
        var activeSessions = await _dbContext.CompanyUserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var active in activeSessions)
        {
            active.IsActive = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
