namespace FinancialImport.Application.Imports;

public sealed class LancamentoContabilImportado
{
    public string LayoutOrigem { get; set; } = string.Empty;
    public string? SeqLancamento { get; set; }
    public string Referencia { get; set; } = string.Empty;
    public DateTime DataLancamento { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime DataDocumento { get; set; }
    public string ContaContabil { get; set; } = string.Empty;
    public string ContaContrapartida { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public decimal? ValorCredito { get; set; }
    public decimal? ValorDebito { get; set; }
    public string? TipoLanc { get; set; }
    public string HistoricoLinha { get; set; } = string.Empty;
    public string? Filial { get; set; }
    public string? CentroCusto { get; set; }
    public Dictionary<string, string?> CamposOriginais { get; set; } = new();
}
