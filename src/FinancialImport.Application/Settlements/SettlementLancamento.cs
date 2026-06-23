namespace FinancialImport.Application.Settlements;

/// <summary>
/// Parsed representation of a single row of the "Baixa de Notas de Saída"
/// spreadsheet, before it is validated/persisted as a
/// <c>ReceivableSettlementLine</c>.
/// </summary>
public sealed class SettlementLancamento
{
    // Fiscal key (locates the open invoice)
    public string DocPN { get; set; } = string.Empty;
    public string NumeroNota { get; set; } = string.Empty;
    public string? Serie { get; set; }
    public string? CnpjFilial { get; set; }
    public string Modelo { get; set; } = string.Empty;

    /// <summary>Data do documento ("DataDocumento") — casada com OINV.TaxDate.</summary>
    public DateTime DataDocumento { get; set; }

    // Payment data
    public string FormaPagamento { get; set; } = string.Empty;
    public string ContaContabil { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public decimal Desconto { get; set; }
    public decimal Juros { get; set; }
    public DateTime DataPagamento { get; set; }
    public int QtdParcelas { get; set; }
    public string? Bandeira { get; set; }
    public string? Ultimo4Cartao { get; set; }
    public string Referencia { get; set; } = string.Empty;

    public Dictionary<string, string?> CamposOriginais { get; set; } = new();
}
