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
        // --- Strongly typed options (everything that used to be hardcoded) ---
        services.Configure<LayoutParsingOptions>(configuration.GetSection("LayoutParsing"));
        services.Configure<LayoutDefinitionsOptions>(configuration.GetSection(LayoutDefinitionsOptions.SectionName));
        services.Configure<ImportProcessingOptions>(configuration.GetSection(ImportProcessingOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

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
