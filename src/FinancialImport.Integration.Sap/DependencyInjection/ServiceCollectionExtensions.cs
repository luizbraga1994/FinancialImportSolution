using FinancialImport.Application.Sap;
using FinancialImport.Integration.Sap.Options;
using FinancialImport.Integration.Sap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinancialImport.Integration.Sap.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SAP Service Layer integration.
    ///
    /// SapServiceLayerOptions is NOT bound from appsettings here — it is fully
    /// driven by ISystemSettingsService through DbConfigureSapOptions, registered
    /// in Infrastructure.DependencyInjection. The HTTP client below resolves the
    /// options lazily (per request) so that updates made via the admin UI take
    /// effect on the next request without an app restart.
    /// </summary>
    public static IServiceCollection AddSapIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("SapServiceLayer")
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SapServiceLayerOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                    client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 180);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SapServiceLayerOptions>>().Value;
                var handler = new HttpClientHandler();
                if (options.IgnoreSslErrors)
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            });

        services.AddScoped<ISapCompanySessionService, SapCompanySessionService>();
        services.AddScoped<ISapJournalEntryService, SapJournalEntryService>();

        return services;
    }
}
