using System.Security.Authentication;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Security;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Security;

public sealed class ApplicationAuthService : IApplicationAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ILoginAuditContextAccessor _auditContext;

    public ApplicationAuthService(
        AppDbContext dbContext,
        PasswordHasher hasher,
        IClock clock,
        ILoginAuditContextAccessor auditContext)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _clock = clock;
        _auditContext = auditContext;
    }

    public async Task<ApplicationUserSession> SignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(u => u.Profiles)
                .ThenInclude(up => up.Profile)
            .Include(u => u.Profiles)
                .ThenInclude(up => up.Profile)
                    .ThenInclude(p => p.Permissions)
                        .ThenInclude(pp => pp.Permission)
            .Include(u => u.AllowedCompanies)
            .SingleOrDefaultAsync(u => u.Login == login, cancellationToken);

        if (user == null)
        {
            AddAudit(null, login, false, "Usuário não encontrado");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new AuthenticationException("Usuário ou senha inválidos.");
        }

        if (!user.IsActive || user.IsBlocked)
        {
            AddAudit(user.Id, login, false, "Usuário inativo ou bloqueado");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new AuthenticationException("Usuário inativo ou bloqueado.");
        }

        if (user.PasswordSalt == null || !_hasher.Verify(password, user.PasswordSalt, user.PasswordHash))
        {
            AddAudit(user.Id, login, false, "Senha inválida");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new AuthenticationException("Usuário ou senha inválidos.");
        }

        user.LastLoginAt = _clock.Now;
        AddAudit(user.Id, login, true, null);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var profiles = user.Profiles
            .Select(p => p.Profile?.Name)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct()
            .ToArray();

        var permissions = user.Profiles
            .SelectMany(p => p.Profile?.Permissions ?? new List<ProfilePermission>())
            .Select(pp => pp.Permission?.Code)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct()
            .ToArray();

        var allowedCompanies = user.AllowedCompanies
            .Where(c => c.IsActive)
            .Select(c => c.CompanyDb)
            .Distinct()
            .ToArray();

        return new ApplicationUserSession
        {
            UserId = user.Id,
            Login = user.Login,
            Name = user.Name,
            Profiles = profiles,
            Permissions = permissions,
            AllowedCompanies = allowedCompanies
        };
    }

    private void AddAudit(long? userId, string login, bool success, string? reason)
    {
        var audit = new LoginAudit
        {
            UserId = userId,
            LoginProvided = login,
            Success = success,
            IpAddress = _auditContext.IpAddress,
            UserAgent = _auditContext.UserAgent,
            OccurredAt = _clock.Now,
            FailureReason = reason
        };

        _dbContext.LoginAudits.Add(audit);
    }
}
