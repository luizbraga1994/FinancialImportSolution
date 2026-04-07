using FinancialImport.Application.Sap;
using FinancialImport.Integration.Hana.Options;
using FinancialImport.Integration.Hana.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Integration.Hana.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHanaIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HanaOptions>(configuration.GetSection("Hana"));
        services.AddScoped<ISapCompanyDiscoveryService, SapCompanyDiscoveryService>();
        return services;
    }
}
