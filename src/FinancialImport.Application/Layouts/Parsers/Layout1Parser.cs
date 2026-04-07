using FinancialImport.Application.Imports;
using Microsoft.Extensions.Options;

namespace FinancialImport.Application.Layouts.Parsers;

public sealed class Layout1Parser : ILayoutImportParser
{
    private readonly LayoutParsingOptions _options;

    public Layout1Parser(IOptions<LayoutParsingOptions> options)
    {
        _options = options.Value;
    }

    public string LayoutName => "Layout1";

    public bool CanParse(ImportFileContext context)
        => context.Headers.Any(h => h.Equals("CODCONTACONTABIL", StringComparison.OrdinalIgnoreCase))
           && context.Headers.Any(h => h.Equals("DTLANCCONTABIL", StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyCollection<LancamentoContabilImportado>> ParseAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new List<LancamentoContabilImportado>();

        foreach (var row in context.Rows)
        {
            var tipo = row.Get("TIPOLANC");
            if (string.IsNullOrWhiteSpace(tipo))
            {
                tipo = _options.DefaultTipoLancLayout1;
            }

            var valor = row.GetDecimal("VLR");
            var credito = tipo?.Equals("C", StringComparison.OrdinalIgnoreCase) == true ? valor : 0m;
            var debito = tipo?.Equals("D", StringComparison.OrdinalIgnoreCase) == true ? valor : 0m;

            result.Add(new LancamentoContabilImportado
            {
                LayoutOrigem = LayoutName,
                Referencia = row.GetRequired("REFERENCIA"),
                DataLancamento = row.GetDate("DTLANCCONTABIL"),
                DataVencimento = row.GetDate("DTLANCCONTABIL"),
                DataDocumento = row.GetDate("DTLANCCONTABIL"),
                ContaContabil = row.GetRequired("CODCONTACONTABIL"),
                ContaContrapartida = row.GetRequired("CODCONTACONTRA"),
                Valor = valor,
                ValorCredito = credito,
                ValorDebito = debito,
                TipoLanc = tipo,
                HistoricoLinha = row.GetRequired("HIST"),
                CamposOriginais = new Dictionary<string, string?>
                {
                    ["CODCONTACONTABIL"] = row.Get("CODCONTACONTABIL"),
                    ["REFERENCIA"] = row.Get("REFERENCIA"),
                    ["DTLANCCONTABIL"] = row.Get("DTLANCCONTABIL"),
                    ["VLR"] = row.Get("VLR"),
                    ["CODCONTACONTRA"] = row.Get("CODCONTACONTRA"),
                    ["HIST"] = row.Get("HIST"),
                    ["TIPOLANC"] = row.Get("TIPOLANC")
                }
            });
        }

        return Task.FromResult<IReadOnlyCollection<LancamentoContabilImportado>>(result);
    }
}
