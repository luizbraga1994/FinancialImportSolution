using System.Data.Common;
using System.Reflection;
using FinancialImport.Integration.Hana.Options;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Integration.Hana.Services;

/// <summary>
/// Resolves the SAP HANA <see cref="DbProviderFactory"/> (Sap.Data.Hana),
/// registering it via reflection when it is not already known to
/// <see cref="DbProviderFactories"/>. Mirrors the resolution logic used by
/// <see cref="SapCompanyDiscoveryService"/> so HANA-backed services share the
/// same provider bootstrap.
/// </summary>
internal static class HanaProviderFactoryResolver
{
    public static DbProviderFactory Resolve(HanaOptions options, ILogger logger)
    {
        try
        {
            return DbProviderFactories.GetFactory(options.ProviderInvariantName);
        }
        catch
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(options.ProviderAssemblyPath))
                {
                    var loaded = Assembly.LoadFrom(options.ProviderAssemblyPath);
                    var factory = GetFactoryFromAssembly(loaded);
                    if (factory != null)
                    {
                        DbProviderFactories.RegisterFactory(options.ProviderInvariantName, factory);
                        return factory;
                    }
                }

                var assembly = Assembly.Load(options.ProviderInvariantName);
                var instance = GetFactoryFromAssembly(assembly);
                if (instance != null)
                {
                    DbProviderFactories.RegisterFactory(options.ProviderInvariantName, instance);
                    return instance;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao registrar provider HANA via reflexão.");
            }

            throw new InvalidOperationException("Provider HANA não disponível. Instale o cliente SAP HANA e registre o provider Sap.Data.Hana.");
        }
    }

    private static DbProviderFactory? GetFactoryFromAssembly(Assembly assembly)
    {
        var factoryType = assembly.GetType("Sap.Data.Hana.HanaFactory");
        return factoryType?.GetField("Instance")?.GetValue(null) as DbProviderFactory;
    }
}
