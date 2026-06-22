using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

/// <summary>
/// A single accounts-receivable settlement row. It carries the fiscal key used
/// to locate the open SAP invoice (DocPN + Serial + Series + Model) plus the
/// payment data that becomes a SAP Incoming Payment.
/// </summary>
public sealed class ReceivableSettlementLine
{
    public long Id { get; set; }
    public long SettlementFileId { get; set; }

    /// <summary>Raw hash of the full source JSON. Used only for change tracking.</summary>
    public string LineHash { get; set; } = string.Empty;

    /// <summary>
    /// Deduplication key hash built from the fiscal note key
    /// (CustomerDoc + InvoiceSerial + InvoiceSeries + InvoiceModel).
    /// </summary>
    public string BusinessKeyHash { get; set; } = string.Empty;

    // ===== Fiscal key (used to resolve the open invoice in HANA) =====

    /// <summary>Customer document (CPF/CNPJ) from the "DocPN" column.</summary>
    public string CustomerDoc { get; set; } = string.Empty;

    /// <summary>Fiscal serial number of the note ("Nº Nota" -> OINV.Serial).</summary>
    public string InvoiceSerial { get; set; } = string.Empty;

    /// <summary>Fiscal series ("Serie" -> OINV.SeriesStr). May be empty.</summary>
    public string? InvoiceSeries { get; set; }

    /// <summary>Branch tax id (CNPJ filial) from the "CNPJ" column.</summary>
    public string? BranchTaxId { get; set; }

    /// <summary>Fiscal model name ("Modelo" -> ONFM.NfmName, e.g. NFS-e).</summary>
    public string InvoiceModel { get; set; } = string.Empty;

    // ===== Payment data (from the spreadsheet) =====

    /// <summary>Payment means: Dinheiro, Transferência, CartaoC, CartaoD.</summary>
    public string PaymentMeans { get; set; } = string.Empty;

    /// <summary>G/L account that receives the value ("ContaContabil").</summary>
    public string ReceivingAccount { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal Interest { get; set; }
    public DateTime PaymentDate { get; set; }

    public int Installments { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
    public string Reference { get; set; } = string.Empty;

    // ===== Resolved from SAP/HANA during preview =====

    /// <summary>Customer card code resolved from the matched invoice.</summary>
    public string? CardCode { get; set; }

    /// <summary>DocEntry of the matched open invoice (target of PaymentInvoices).</summary>
    public int? InvoiceDocEntry { get; set; }

    /// <summary>Voucher number (OINV.Serial || OINV.DocNum) used for card payments.</summary>
    public string? VoucherNum { get; set; }

    /// <summary>Resolved branch (BPLID) for the incoming payment.</summary>
    public int? BplId { get; set; }

    public string CompanyDb { get; set; } = string.Empty;
    public SettlementLineStatus Status { get; set; }
    public string? ValidationMessage { get; set; }
    public string? SapReturnMessage { get; set; }
    public int? SapDocEntry { get; set; }
    public string? SourceJson { get; set; }

    /// <summary>
    /// Hash of the grouping key, linking this line to a single
    /// <see cref="IncomingPaymentDispatch"/>. For settlements this is normally
    /// one payment per invoice line.
    /// </summary>
    public string? GroupKeyHash { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public ReceivableSettlementFile? SettlementFile { get; set; }
}
