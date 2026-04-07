using FinancialImport.Domain.Constants;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Data;

public sealed class DatabaseSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        AppDbContext dbContext,
        PasswordHasher hasher,
        IClock clock,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);
        await SeedProfilesAsync(cancellationToken);
        await SeedGlobalAdminAsync(cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var permissions = new (string Code, string Name, string Group)[]
        {
            (PermissionCodes.ImportarLancamentos, "Importar Lancamentos", "Importacao"),
            (PermissionCodes.VisualizarHistorico, "Visualizar Historico", "Importacao"),
            (PermissionCodes.ReprocessarImportacao, "Reprocessar Importacao", "Importacao"),
            (PermissionCodes.TrocarCompany, "Trocar Company", "Empresa"),
            (PermissionCodes.GerenciarUsuarios, "Gerenciar Usuarios", "Administracao"),
            (PermissionCodes.GerenciarPerfis, "Gerenciar Perfis", "Administracao"),
            (PermissionCodes.GerenciarPermissoes, "Gerenciar Permissoes", "Administracao"),
            (PermissionCodes.VisualizarLogs, "Visualizar Logs", "Sistema"),
        };

        foreach (var (code, name, group) in permissions)
        {
            if (!await _dbContext.Permissions.AnyAsync(p => p.Code == code, cancellationToken))
            {
                _dbContext.Permissions.Add(new Permission
                {
                    Code = code,
                    Name = name,
                    Group = group,
                    IsActive = true
                });
                _logger.LogInformation("Permissao '{Code}' criada.", code);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedProfilesAsync(CancellationToken cancellationToken)
    {
        if (!await _dbContext.Profiles.AnyAsync(p => p.Name == "Administrador", cancellationToken))
        {
            var adminProfile = new Profile
            {
                Name = "Administrador",
                Description = "Perfil com acesso total ao sistema",
                IsActive = true
            };
            _dbContext.Profiles.Add(adminProfile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var allPermissions = await _dbContext.Permissions.Where(p => p.IsActive).ToListAsync(cancellationToken);
            foreach (var permission in allPermissions)
            {
                _dbContext.ProfilePermissions.Add(new ProfilePermission
                {
                    ProfileId = adminProfile.Id,
                    PermissionId = permission.Id
                });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Perfil 'Administrador' criado com {Count} permissoes.", allPermissions.Count);
        }

        if (!await _dbContext.Profiles.AnyAsync(p => p.Name == "Operador", cancellationToken))
        {
            var operatorProfile = new Profile
            {
                Name = "Operador",
                Description = "Perfil operacional para importacoes",
                IsActive = true
            };
            _dbContext.Profiles.Add(operatorProfile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var operatorPermissions = await _dbContext.Permissions
                .Where(p => p.IsActive && (
                    p.Code == PermissionCodes.ImportarLancamentos ||
                    p.Code == PermissionCodes.VisualizarHistorico ||
                    p.Code == PermissionCodes.TrocarCompany))
                .ToListAsync(cancellationToken);

            foreach (var permission in operatorPermissions)
            {
                _dbContext.ProfilePermissions.Add(new ProfilePermission
                {
                    ProfileId = operatorProfile.Id,
                    PermissionId = permission.Id
                });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Perfil 'Operador' criado com {Count} permissoes.", operatorPermissions.Count);
        }
    }

    private async Task SeedGlobalAdminAsync(CancellationToken cancellationToken)
    {
        var adminLogin = _configuration["AdminSeed:Login"] ?? "admin";
        var adminPassword = _configuration["AdminSeed:Password"] ?? "Admin@123";
        var adminEmail = _configuration["AdminSeed:Email"] ?? "admin@financialimport.local";
        var adminName = _configuration["AdminSeed:Name"] ?? "Administrador Global";

        if (await _dbContext.Users.AnyAsync(u => u.Login == adminLogin, cancellationToken))
        {
            _logger.LogInformation("Usuario admin '{Login}' ja existe. Seed ignorado.", adminLogin);
            return;
        }

        var (hash, salt) = _hasher.HashPassword(adminPassword);

        var adminUser = new User
        {
            Login = adminLogin,
            Name = adminName,
            Email = adminEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = true,
            IsBlocked = false,
            IsGlobalAdmin = true,
            CreatedAt = _clock.Now,
            CreatedBy = "SYSTEM"
        };

        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var adminProfile = await _dbContext.Profiles.FirstOrDefaultAsync(p => p.Name == "Administrador", cancellationToken);
        if (adminProfile != null)
        {
            _dbContext.UserProfiles.Add(new UserProfile
            {
                UserId = adminUser.Id,
                ProfileId = adminProfile.Id
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Usuario admin global '{Login}' criado com sucesso.", adminLogin);
    }
}
