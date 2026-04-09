using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Layouts.Parsers;
using FinancialImport.Application.Validators;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Imports;
using FinancialImport.Shared.Messaging;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // LayoutDefinitionsOptions defines the column mappings for each file layout.
        // It is a structured definition (not a connection/credential), so it stays
        // bound from appsettings (Imports:Layouts section) for version-controlled
        // change management. All other options come from the DB settings table.
        services.Configure<LayoutDefinitionsOptions>(configuration.GetSection(LayoutDefinitionsOptions.SectionName));

        // --- Correlation ---
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

        // --- Layout parsers ---
        services.AddScoped<ILayoutImportParser, Layout1Parser>();
        services.AddScoped<ILayoutImportParser, Layout2Parser>();
        services.AddScoped<IImportLayoutResolver, ImportLayoutResolver>();

        // --- Validation ---
        services.AddScoped<IValidator<LancamentoContabilImportado>, LancamentoContabilImportadoValidator>();

        return services;
    }
}
