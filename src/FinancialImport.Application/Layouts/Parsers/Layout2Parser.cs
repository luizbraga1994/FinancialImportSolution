using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Layouts.Parsers;

public sealed class Layout2Parser : ILayoutImportParser
{
    public string LayoutName => "Layout2";

    public bool CanParse(ImportFileContext context)
    {
        var hasContaContabil = context.Headers.Any(h => h.Equals("Conta Contabil", StringComparison.OrdinalIgnoreCase));
        var hasValorCredito = context.Headers.Any(h => h.Equals("Valor Credito", StringComparison.OrdinalIgnoreCase));
        var hasValorDebito = context.Headers.Any(h => h.Equals("Valor Debito", StringComparison.OrdinalIgnoreCase));

        // Também aceita variantes comuns
        var hasAlternateConta = context.Headers.Any(h =>
            h.Equals("Conta Contábil", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("ContaContabil", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("AccountCode", StringComparison.OrdinalIgnoreCase));

        var hasAlternateCredito = context.Headers.Any(h =>
            h.Equals("Credito", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Credit", StringComparison.OrdinalIgnoreCase));

        var hasAlternateDebito = context.Headers.Any(h =>
            h.Equals("Debito", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Debit", StringComparison.OrdinalIgnoreCase));

        return (hasContaContabil || hasAlternateConta) &&
               (hasValorCredito || hasAlternateCredito) &&
               (hasValorDebito || hasAlternateDebito);
    }

    public Task<IReadOnlyCollection<LancamentoContabilImportado>> ParseAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new List<LancamentoContabilImportado>();

        // Mapeamento flexivel de colunas
        var contaContabilCol = FindColumn(context.Headers, new[] { "Conta Contabil", "Conta Contábil", "ContaContabil", "AccountCode" });
        var contaContrapartidaCol = FindColumn(context.Headers, new[] { "Conta Contrapartida", "ContaContrapartida", "CounterpartAccount", "ContraAccount" });
        var valorCreditoCol = FindColumn(context.Headers, new[] { "Valor Credito", "Credito", "Credit", "CreditAmount" });
        var valorDebitoCol = FindColumn(context.Headers, new[] { "Valor Debito", "Debito", "Debit", "DebitAmount" });
        var dataLancamentoCol = FindColumn(context.Headers, new[] { "Data Lancamento", "DataLancamento", "PostingDate", "Data Lançamento" });
        var dataVencimentoCol = FindColumn(context.Headers, new[] { "Data Vencimento", "DataVencimento", "DueDate", "Data Vencimento" });
        var dataDocumentoCol = FindColumn(context.Headers, new[] { "Data Documento", "DataDocumento", "DocumentDate", "TaxDate" });
        var observacaoCol = FindColumn(context.Headers, new[] { "Observacao", "Observacao Linha", "Memo", "Historico", "Observação" });
        var referenciaCol = FindColumn(context.Headers, new[] { "Referencia", "Referência", "Reference", "Observacao" });
        var filialCol = FindColumn(context.Headers, new[] { "Filial", "Branch", "BranchCode", "BPLID", "BPLId" });
        var seqLancamentoCol = FindColumn(context.Headers, new[] { "Seq Lancamento", "SeqLancamento", "Sequence" });

        foreach (var row in context.Rows)
        {
            var credito = row.GetDecimal(valorCreditoCol ?? "Valor Credito");
            var debito = row.GetDecimal(valorDebitoCol ?? "Valor Debito");
            var valor = credito > 0 ? credito : debito;

            var referencia = !string.IsNullOrWhiteSpace(row.Get(referenciaCol ?? "Observacao"))
                ? row.Get(referenciaCol ?? "Observacao") ?? string.Empty
                : row.Get(observacaoCol ?? "Observacao Linha") ?? string.Empty;

            result.Add(new LancamentoContabilImportado
            {
                LayoutOrigem = LayoutName,
                SeqLancamento = row.Get(seqLancamentoCol ?? "Seq Lancamento"),
                Referencia = referencia,
                DataLancamento = row.GetDate(dataLancamentoCol ?? "Data Lancamento"),
                DataVencimento = row.GetDate(dataVencimentoCol ?? "Data Vencimento") != DateTime.MinValue
                    ? row.GetDate(dataVencimentoCol ?? "Data Vencimento")
                    : row.GetDate(dataLancamentoCol ?? "Data Lancamento"),
                DataDocumento = row.GetDate(dataDocumentoCol ?? "Data Documento") != DateTime.MinValue
                    ? row.GetDate(dataDocumentoCol ?? "Data Documento")
                    : row.GetDate(dataLancamentoCol ?? "Data Lancamento"),
                ContaContabil = row.GetRequired(contaContabilCol ?? "Conta Contabil"),
                ContaContrapartida = row.GetRequired(contaContrapartidaCol ?? "Conta Contrapartida"),
                Valor = valor,
                ValorCredito = credito > 0 ? credito : null,
                ValorDebito = debito > 0 ? debito : null,
                HistoricoLinha = row.GetRequired(observacaoCol ?? "Observacao Linha"),
                Filial = row.Get(filialCol ?? "Filial"),
                CamposOriginais = context.Headers.ToDictionary(h => h, h => row.Get(h))
            });
        }

        return Task.FromResult<IReadOnlyCollection<LancamentoContabilImportado>>(result);
    }

    private static string? FindColumn(IReadOnlyCollection<string> headers, string[] possibleNames)
    {
        return headers.FirstOrDefault(h => possibleNames.Any(p => h.Equals(p, StringComparison.OrdinalIgnoreCase)));
    }
}