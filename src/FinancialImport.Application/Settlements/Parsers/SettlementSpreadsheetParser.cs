using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Settlements.Parsers;

/// <summary>
/// Parses the standard "Baixa de Notas de Saída" spreadsheet. Detected by the
/// presence of the fiscal key columns (DocPN + Nº Nota) plus a payment value.
/// Column matching is alias-tolerant to absorb header variations.
/// </summary>
public sealed class SettlementSpreadsheetParser : ISettlementParser
{
    public string LayoutName => "BaixaNotasSaida";

    private static readonly string[] DocPnAliases = { "DocPN", "Doc PN", "Documento", "CPF/CNPJ", "CpfCnpj", "DocCliente" };
    private static readonly string[] NotaAliases = { "Nº Nota", "No Nota", "Numero Nota", "NumeroNota", "Nota", "Serial", "NumNota" };
    private static readonly string[] SerieAliases = { "Serie", "Série", "Series", "SeriesStr" };
    private static readonly string[] CnpjAliases = { "CNPJ", "CNPJ Filial", "CnpjFilial", "VATRegNum" };
    private static readonly string[] ModeloAliases = { "Modelo", "Model", "Modelo Nota", "ModeloNota" };
    private static readonly string[] DataDocAliases = { "DataDocumento", "Data Documento", "DocumentDate", "Data Doc", "DtDocumento" };
    private static readonly string[] FormaAliases = { "FormaDePagamento", "Forma de Pagamento", "FormaPagamento", "Forma", "PaymentMeans" };
    private static readonly string[] ContaAliases = { "ContaContabil", "Conta Contabil", "Conta Contábil", "Conta", "AccountCode" };
    private static readonly string[] ValorAliases = { "Valor", "Value", "Amount", "ValorPago" };
    private static readonly string[] DescontoAliases = { "Desconto", "Discount" };
    private static readonly string[] JurosAliases = { "Juros", "Interest", "Mora" };
    private static readonly string[] DataAliases = { "DataPagamento", "Data Pagamento", "Data", "PaymentDate", "DataBaixa" };
    private static readonly string[] ParcelasAliases = { "QtdParcelas", "Qtd Parcelas", "Parcelas", "NumParcelas", "Installments" };
    private static readonly string[] BandeiraAliases = { "Bandeira", "Brand", "CardBrand" };
    private static readonly string[] UltimoDigitosAliases = { "Ultimo4 DigitosCartao", "Ultimo4DigitosCartao", "Ultimos4", "Ultimo4", "CardLastDigits", "Final Cartao" };
    private static readonly string[] RefAliases = { "Ref", "Referencia", "Referência", "Reference" };

    public bool CanParse(SettlementFileContext context)
    {
        var hasDocPn = FindColumn(context.Headers, DocPnAliases) != null;
        var hasNota = FindColumn(context.Headers, NotaAliases) != null;
        var hasValor = FindColumn(context.Headers, ValorAliases) != null;
        return hasDocPn && hasNota && hasValor;
    }

    public Task<IReadOnlyCollection<SettlementLancamento>> ParseAsync(
        SettlementFileContext context,
        CancellationToken cancellationToken = default)
    {
        var docPnCol = FindColumn(context.Headers, DocPnAliases);
        var notaCol = FindColumn(context.Headers, NotaAliases);
        var serieCol = FindColumn(context.Headers, SerieAliases);
        var cnpjCol = FindColumn(context.Headers, CnpjAliases);
        var modeloCol = FindColumn(context.Headers, ModeloAliases);
        var dataDocCol = FindColumn(context.Headers, DataDocAliases);
        var formaCol = FindColumn(context.Headers, FormaAliases);
        var contaCol = FindColumn(context.Headers, ContaAliases);
        var valorCol = FindColumn(context.Headers, ValorAliases);
        var descontoCol = FindColumn(context.Headers, DescontoAliases);
        var jurosCol = FindColumn(context.Headers, JurosAliases);
        var dataCol = FindColumn(context.Headers, DataAliases);
        var parcelasCol = FindColumn(context.Headers, ParcelasAliases);
        var bandeiraCol = FindColumn(context.Headers, BandeiraAliases);
        var ultimoCol = FindColumn(context.Headers, UltimoDigitosAliases);
        var refCol = FindColumn(context.Headers, RefAliases);

        var result = new List<SettlementLancamento>();

        foreach (var row in context.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docPn = docPnCol != null ? (row.Get(docPnCol) ?? string.Empty).Trim() : string.Empty;
            var nota = notaCol != null ? (row.Get(notaCol) ?? string.Empty).Trim() : string.Empty;

            // Skip fully blank rows.
            if (string.IsNullOrWhiteSpace(docPn) && string.IsNullOrWhiteSpace(nota))
                continue;

            var parcelasText = parcelasCol != null ? row.Get(parcelasCol) : null;
            _ = int.TryParse(parcelasText?.Trim(), out var parcelas);

            result.Add(new SettlementLancamento
            {
                DocPN = docPn,
                NumeroNota = nota,
                Serie = serieCol != null ? row.Get(serieCol) : null,
                CnpjFilial = cnpjCol != null ? row.Get(cnpjCol) : null,
                Modelo = modeloCol != null ? row.GetRequired(modeloCol) : string.Empty,
                DataDocumento = dataDocCol != null ? row.GetDate(dataDocCol) : DateTime.MinValue,
                FormaPagamento = formaCol != null ? row.GetRequired(formaCol) : string.Empty,
                ContaContabil = contaCol != null ? row.GetRequired(contaCol) : string.Empty,
                Valor = valorCol != null ? row.GetDecimal(valorCol) : 0m,
                Desconto = descontoCol != null ? row.GetDecimal(descontoCol) : 0m,
                Juros = jurosCol != null ? row.GetDecimal(jurosCol) : 0m,
                DataPagamento = dataCol != null ? row.GetDate(dataCol) : DateTime.MinValue,
                QtdParcelas = parcelas,
                Bandeira = bandeiraCol != null ? row.Get(bandeiraCol) : null,
                Ultimo4Cartao = ultimoCol != null ? row.Get(ultimoCol) : null,
                Referencia = refCol != null ? (row.Get(refCol) ?? string.Empty) : string.Empty,
                CamposOriginais = context.Headers.ToDictionary(h => h, h => row.Get(h))
            });
        }

        return Task.FromResult<IReadOnlyCollection<SettlementLancamento>>(result);
    }

    private static string? FindColumn(IReadOnlyCollection<string> headers, string[] possibleNames)
        => headers.FirstOrDefault(h => possibleNames.Any(p => h.Equals(p, StringComparison.OrdinalIgnoreCase)));
}
