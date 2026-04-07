using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Layouts.Parsers;
using FinancialImport.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialImport.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LayoutParsingOptions>(configuration.GetSection("LayoutParsing"));

        services.AddScoped<ILayoutImportParser, Layout1Parser>();
        services.AddScoped<ILayoutImportParser, Layout2Parser>();
        services.AddScoped<IImportLayoutResolver, ImportLayoutResolver>();
        services.AddScoped<IValidator<LancamentoContabilImportado>, LancamentoContabilImportadoValidator>();

        return services;
    }
}
