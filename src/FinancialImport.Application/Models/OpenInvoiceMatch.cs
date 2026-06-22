namespace FinancialImport.Application.Models;

/// <summary>
/// Fiscal key used to locate the open SAP invoice that a settlement line
/// refers to. Maps to the columns of the "Baixa de Notas de Saída"
/// spreadsheet (DocPN, Nº Nota, Série, Modelo).
/// </summary>
public sealed class OpenInvoiceQuery
{
    /// <summary>Customer document (CPF/CNPJ) — matched against CRD7.TaxId4/TaxId0.</summary>
    public string CustomerDoc { get; set; } = string.Empty;

    /// <summary>Fiscal serial number ("Nº Nota") — matched against OINV.Serial.</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>Fiscal series ("Série") — matched against OINV.SeriesStr (may be empty).</summary>
    public string? Series { get; set; }

    /// <summary>Fiscal model name ("Modelo", e.g. NFS-e) — matched against ONFM.NfmName.</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Result of resolving an open invoice against SAP HANA. Carries the fields
/// required to build the Incoming Payment (DocEntry, CardCode, VoucherNum).
/// </summary>
public sealed class OpenInvoiceMatch
{
    public int DocEntry { get; set; }
    public string CardCode { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string? SeriesStr { get; set; }
    public int Model { get; set; }

    /// <summary>Branch (OINV.BPLId) used as BPLID on the Incoming Payment.</summary>
    public int? BplId { get; set; }

    /// <summary>OINV.Serial || OINV.DocNum — used as the card VoucherNum.</summary>
    public string VoucherNum { get; set; } = string.Empty;
}
