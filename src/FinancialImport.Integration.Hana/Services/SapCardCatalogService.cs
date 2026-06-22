using System.Data;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Integration.Hana.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Integration.Hana.Services;

/// <summary>
/// Loads the SAP credit-card catalog from HANA: OCRC (credit cards) for the
/// brand -> CreditCard/AcctCode mapping, and OCRP (credit payment types) for the
/// à vista / parcelado payment-method codes.
/// </summary>
public sealed class SapCardCatalogService : ISapCardCatalogService
{
    private const string OcrcSql = @"SELECT ""CreditCard"", ""CardName"", ""AcctCode"" FROM OCRC";
    private const string OcrpSql = @"SELECT ""CrTypeCode"", ""InstalMent"" FROM OCRP";

    private readonly HanaOptions _options;
    private readonly ILogger<SapCardCatalogService> _logger;

    public SapCardCatalogService(IOptions<HanaOptions> options, ILogger<SapCardCatalogService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SapCardCatalog> GetCardCatalogAsync(string companyDb, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Server))
            throw new InvalidOperationException("HanaDbConnection:Server não configurado.");

        var factory = HanaProviderFactoryResolver.Resolve(_options, _logger);
        var connectionString = _options.BuildConnectionString(companyDb);

        var brands = new Dictionary<string, CardBrandInfo>(StringComparer.OrdinalIgnoreCase);
        int? singleMethod = null;
        int? installmentMethod = null;

        await using var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("Não foi possível criar conexão HANA.");
        connection.ConnectionString = connectionString;
        await connection.OpenAsync(cancellationToken);

        // OCRC — credit card brands
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = OcrcSql;
            cmd.CommandTimeout = _options.CommandTimeout;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(1)) continue;
                var name = reader.GetString(1).Trim();
                if (name.Length == 0) continue;
                brands[name.ToUpperInvariant()] = new CardBrandInfo
                {
                    CreditCard = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                    AcctCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                };
            }
        }

        // OCRP — credit payment types (à vista / parcelado)
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = OcrpSql;
            cmd.CommandTimeout = _options.CommandTimeout;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0)) continue;
                var code = Convert.ToInt32(reader.GetValue(0));
                var installment = !reader.IsDBNull(1)
                    && string.Equals(reader.GetString(1).Trim(), "Y", StringComparison.OrdinalIgnoreCase);
                if (installment) installmentMethod ??= code;
                else singleMethod ??= code;
            }
        }

        return new SapCardCatalog
        {
            Brands = brands,
            SinglePaymentMethodCode = singleMethod,
            InstallmentMethodCode = installmentMethod
        };
    }
}
