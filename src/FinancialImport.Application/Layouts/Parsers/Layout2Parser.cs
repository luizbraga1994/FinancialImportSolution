using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Layouts.Parsers;

public sealed class Layout2Parser : ILayoutImportParser
{
    public string LayoutName => "Layout2";

    public bool CanParse(ImportFileContext context)
        => context.Headers.Any(h => h.Equals("Conta Contabil", StringComparison.OrdinalIgnoreCase))
           && context.Headers.Any(h => h.Equals("Valor Credito", StringComparison.OrdinalIgnoreCase))
           && context.Headers.Any(h => h.Equals("Valor Debito", StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyCollection<LancamentoContabilImportado>> ParseAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new List<LancamentoContabilImportado>();

        foreach (var row in context.Rows)
        {
            var credito = row.GetDecimal("Valor Credito");
            var debito = row.GetDecimal("Valor Debito");
            var valor = credito > 0 ? credito : debito;

            result.Add(new LancamentoContabilImportado
            {
                LayoutOrigem = LayoutName,
                SeqLancamento = row.Get("Seq Lancamento"),
                Referencia = row.GetRequired("Observacao"),
                DataLancamento = row.GetDate("Data Lancamento"),
                DataVencimento = row.GetDate("Data Vencimento"),
                DataDocumento = row.GetDate("Data Documento"),
                ContaContabil = row.GetRequired("Conta Contabil"),
                ContaContrapartida = row.GetRequired("Conta Contrapartida"),
                Valor = valor,
                ValorCredito = credito > 0 ? credito : null,
                ValorDebito = debito > 0 ? debito : null,
                HistoricoLinha = row.GetRequired("Observacao Linha"),
                Filial = row.Get("Filial"),
                CamposOriginais = new Dictionary<string, string?>
                {
                    ["Observacao"] = row.Get("Observacao"),
                    ["Conta Contabil"] = row.Get("Conta Contabil"),
                    ["Conta Contrapartida"] = row.Get("Conta Contrapartida"),
                    ["Valor Credito"] = row.Get("Valor Credito"),
                    ["Valor Debito"] = row.Get("Valor Debito"),
                    ["Data Lancamento"] = row.Get("Data Lancamento"),
                    ["Data Vencimento"] = row.Get("Data Vencimento"),
                    ["Data Documento"] = row.Get("Data Documento"),
                    ["Observacao Linha"] = row.Get("Observacao Linha"),
                    ["Filial"] = row.Get("Filial"),
                    ["Seq Lancamento"] = row.Get("Seq Lancamento")
                }
            });
        }

        return Task.FromResult<IReadOnlyCollection<LancamentoContabilImportado>>(result);
    }
}
