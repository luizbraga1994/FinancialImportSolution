using System.Data;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Integration.Hana.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Integration.Hana.Services;

/// <summary>
/// Resolves open outgoing invoices straight from SAP HANA. The fiscal key
/// (customer document + serial + series + model) is matched against OINV joined
/// with CRD7 (BP tax ids) and ONFM (fiscal note models), returning the DocEntry,
/// CardCode and the VoucherNum (Serial || DocNum) used for card payments.
/// </summary>
public sealed class SapOpenInvoiceService : ISapOpenInvoiceService
{
    // Query mirrors the validated HANA SQL: matches the invoice by the
    // customer's tax id (CRD7), the fiscal serial, the series string and the
    // model name (ONFM.NfmName), normalising dashes/casing on the model.
    private const string Sql = @"
        SELECT T0.""DocEntry"", T0.""CardCode"", T0.""Serial"", T0.""SeriesStr"", T0.""Model"",
               T0.""Serial"" || T0.""DocNum"" AS ""VoucherNum""
        FROM OINV T0
        INNER JOIN CRD7 T1 ON T1.""CardCode"" = T0.""CardCode"" AND COALESCE(T1.""TaxId4"", T1.""TaxId0"", '') <> ''
        INNER JOIN ONFM T2 ON T0.""Model"" = T2.""AbsEntry""
        WHERE COALESCE(T1.""TaxId4"", T1.""TaxId0"", '') = ?
          AND TO_VARCHAR(T0.""Serial"") = ?
          AND IFNULL(T0.""SeriesStr"", '') = ?
          AND Lower(Replace(T2.""NfmName"", '-', '')) = Lower(Replace(?, '-', ''))";

    private readonly HanaOptions _options;
    private readonly ILogger<SapOpenInvoiceService> _logger;

    public SapOpenInvoiceService(IOptions<HanaOptions> options, ILogger<SapOpenInvoiceService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OpenInvoiceMatch?> FindOpenInvoiceAsync(string companyDb, OpenInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Server))
        {
            throw new InvalidOperationException("HanaDbConnection:Server não configurado.");
        }

        var factory = HanaProviderFactoryResolver.Resolve(_options, _logger);
        var connectionString = _options.BuildConnectionString(companyDb);

        var matches = new List<OpenInvoiceMatch>();

        await using var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("Não foi possível criar conexão HANA.");
        connection.ConnectionString = connectionString;
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.CommandTimeout = _options.CommandTimeout;

        AddParameter(command, query.CustomerDoc?.Trim() ?? string.Empty);
        AddParameter(command, query.Serial?.Trim() ?? string.Empty);
        AddParameter(command, query.Series?.Trim() ?? string.Empty);
        AddParameter(command, query.Model?.Trim() ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new OpenInvoiceMatch
            {
                DocEntry = Convert.ToInt32(reader.GetValue(0)),
                CardCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Serial = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2)) ?? string.Empty,
                SeriesStr = reader.IsDBNull(3) ? null : reader.GetString(3),
                Model = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                VoucherNum = reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5)) ?? string.Empty
            });
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Chave fiscal ambígua em {CompanyDb}: DocPN={Doc} Serial={Serial} Serie={Series} Modelo={Model} retornou {Count} faturas.",
                companyDb, query.CustomerDoc, query.Serial, query.Series, query.Model, matches.Count);
            throw new InvalidOperationException(
                $"Chave fiscal ambígua: {matches.Count} faturas encontradas para a nota {query.Serial}.");
        }

        return matches[0];
    }

    private static void AddParameter(IDbCommand command, object value)
    {
        var parameter = command.CreateParameter();
        parameter.DbType = DbType.String;
        parameter.Value = value ?? string.Empty;
        command.Parameters.Add(parameter);
    }
}
