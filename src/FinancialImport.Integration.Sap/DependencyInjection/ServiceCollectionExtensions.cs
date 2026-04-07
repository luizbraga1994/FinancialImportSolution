using FinancialImport.Application.Sap;
using FinancialImport.Integration.Sap.Options;
using FinancialImport.Integration.Sap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Integration.Sap.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSapIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("SapServiceLayer");
        services.Configure<SapServiceLayerOptions>(section);

        var options = new SapServiceLayerOptions();
        section.Bind(options);

        services.AddHttpClient("SapServiceLayer", client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });

        services.AddScoped<ISapCompanySessionService, SapCompanySessionService>();
        services.AddScoped<ISapJournalEntryService, SapJournalEntryService>();

        return services;
    }
}
