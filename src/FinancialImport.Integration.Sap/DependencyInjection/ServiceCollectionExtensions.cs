using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using FinancialImport.Integration.Sap.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Integration.Sap.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SAP Service Layer integration.
    ///
    /// SapServiceLayerOptions is DB-backed via DbConfigureSapOptions.
    /// The HTTP client reads settings dynamically from ISystemSettingsService
    /// so changes made via the admin UI take effect on the next request
    /// without restarting the application.
    /// </summary>
    public static IServiceCollection AddSapIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient("SapServiceLayer")
            .ConfigureHttpClient((sp, client) =>
            {
                // Read directly from settings service (not IOptions singleton)
                // so that admin UI changes take effect on the next HTTP client instance.
                var settings = sp.GetRequiredService<ISystemSettingsService>();
                var baseUrl = settings.Get("Sap:BaseUrl");
                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    // Normalize: ensure trailing slash for proper URI resolution
                    if (!baseUrl.EndsWith('/')) baseUrl += '/';
                    client.BaseAddress = new Uri(baseUrl);
                }

                var timeout = int.TryParse(settings.Get("Sap:TimeoutSeconds"), out var t) ? t : 180;
                client.Timeout = TimeSpan.FromSeconds(timeout);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<ISystemSettingsService>();
                var ignoreSsl = bool.TryParse(settings.Get("Sap:IgnoreSslErrors"), out var v) && v;
                var handler = new HttpClientHandler();
                if (ignoreSsl)
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            });

        services.AddScoped<ISapCompanySessionService, SapCompanySessionService>();
        services.AddScoped<ISapJournalEntryService, SapJournalEntryService>();
        services.AddSingleton<ISapChartOfAccountsService, SapChartOfAccountsService>();

        return services;
    }
}
