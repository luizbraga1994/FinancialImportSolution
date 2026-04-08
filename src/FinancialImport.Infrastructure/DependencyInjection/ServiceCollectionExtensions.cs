using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Hashing;
using FinancialImport.Infrastructure.Imports;
using FinancialImport.Infrastructure.Sap;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MySqlOptions>(configuration.GetSection("MySql"));
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetSection("MySql").GetValue<string>("ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection ou MySql:ConnectionString nao configurado.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<IHashService, Sha256HashService>();
        services.AddScoped<IApplicationAuthService, ApplicationAuthService>();
        services.AddScoped<ISapSessionStore, SapSessionStore>();
        services.AddScoped<IImportRepository, ImportRepository>();
        services.AddScoped<IImportService, ImportService>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
