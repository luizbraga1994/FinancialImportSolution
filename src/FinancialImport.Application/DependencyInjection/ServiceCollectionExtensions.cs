using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Layouts.Parsers;
using FinancialImport.Application.Settlements;
using FinancialImport.Application.Settlements.Parsers;
using FinancialImport.Application.Validators;
using FinancialImport.Shared.Correlation;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // No appsettings binding here. All strongly-typed options are DB-backed
        // via DbConfigureXxxOptions registered in Infrastructure. The only
        // exception is HanaOptions, bound in Integration.Hana from the
        // HanaDbConnection section of appsettings.

        // --- Correlation ---
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

        // --- Layout parsers ---
        services.AddScoped<ILayoutImportParser, Layout1Parser>();
        services.AddScoped<ILayoutImportParser, Layout2Parser>();
        services.AddScoped<IImportLayoutResolver, ImportLayoutResolver>();

        // --- Settlement (Baixa de Notas de Saída) parser ---
        services.AddScoped<ISettlementParser, SettlementSpreadsheetParser>();

        // --- Validation ---
        services.AddScoped<IValidator<LancamentoContabilImportado>, LancamentoContabilImportadoValidator>();
        services.AddScoped<IValidator<SettlementLancamento>, SettlementLancamentoValidator>();

        return services;
    }
}
