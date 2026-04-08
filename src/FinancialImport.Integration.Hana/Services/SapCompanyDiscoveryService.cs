using System.Data.Common;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Integration.Hana.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Integration.Hana.Services;

public sealed class SapCompanyDiscoveryService : ISapCompanyDiscoveryService
{
    private readonly HanaOptions _options;
    private readonly ILogger<SapCompanyDiscoveryService> _logger;

    public SapCompanyDiscoveryService(IOptions<HanaOptions> options, ILogger<SapCompanyDiscoveryService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SapCompanyInfo>> GetAvailableCompaniesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Server))
        {
            throw new InvalidOperationException("HanaDbConnection:Server não configurado.");
        }

        var connectionString = _options.BuildConnectionString();

        const string sql = @"
            SELECT ""dbName"", ""cmpName"", ""cmpStatus""
            FROM ""SBOCOMMON"".""SRGC""";

        var results = new List<SapCompanyInfo>();

        try
        {
            var factory = ResolveFactory();

            await using var connection = factory.CreateConnection();
            if (connection == null)
            {
                throw new InvalidOperationException("Não foi possível criar conexão HANA.");
            }

            connection.ConnectionString = connectionString;
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.CommandTimeout;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var status = reader.IsDBNull(2) ? null : reader.GetString(2);
                results.Add(new SapCompanyInfo
                {
                    CompanyDb = reader.GetString(0),
                    CompanyName = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                    Server = null,
                    IsActive = status == null || !status.Equals("N", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar SBOCOMMON para companies disponíveis.");
            throw;
        }

        return results;
    }

    private DbProviderFactory ResolveFactory()
    {
        try
        {
            return DbProviderFactories.GetFactory(_options.ProviderInvariantName);
        }
        catch
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_options.ProviderAssemblyPath))
                {
                    var loaded = System.Reflection.Assembly.LoadFrom(_options.ProviderAssemblyPath);
                    var factory = GetFactoryFromAssembly(loaded);
                    if (factory != null)
                    {
                        DbProviderFactories.RegisterFactory(_options.ProviderInvariantName, factory);
                        return factory;
                    }
                }

                var assembly = System.Reflection.Assembly.Load(_options.ProviderInvariantName);
                var factoryType = assembly.GetType("Sap.Data.Hana.HanaFactory");
                var instance = factoryType?.GetField("Instance")?.GetValue(null) as DbProviderFactory;
                if (instance != null)
                {
                    DbProviderFactories.RegisterFactory(_options.ProviderInvariantName, instance);
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao registrar provider HANA via reflexão.");
            }

            throw new InvalidOperationException("Provider HANA não disponível. Instale o cliente SAP HANA e registre o provider Sap.Data.Hana.");
        }
    }

    private static DbProviderFactory? GetFactoryFromAssembly(System.Reflection.Assembly assembly)
    {
        var factoryType = assembly.GetType("Sap.Data.Hana.HanaFactory");
        return factoryType?.GetField("Instance")?.GetValue(null) as DbProviderFactory;
    }
}
