using System.Text.Json.Serialization;

namespace FinancialImport.Application.Models;

/// <summary>
/// Payload for a SAP Business One Incoming Payment (ORCT) that settles an
/// outgoing invoice ("Baixa de Notas de Saída"). Only the fields relevant to
/// the settlement flow are modelled. Null properties are omitted on
/// serialization (the SAP client uses JsonIgnoreCondition.WhenWritingNull),
/// so payment-means specific fields are only sent when applicable.
/// </summary>
public sealed class SapIncomingPayment
{
    [JsonPropertyName("DocType")]
    public string DocType { get; set; } = "rCustomer";

    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = string.Empty;

    [JsonPropertyName("DocDate")]
    public DateTime DocDate { get; set; }

    [JsonPropertyName("TaxDate")]
    public DateTime TaxDate { get; set; }

    [JsonPropertyName("Series")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Series { get; set; }

    [JsonPropertyName("JournalRemarks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JournalRemarks { get; set; }

    [JsonPropertyName("BPLID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BPLID { get; set; }

    // ===== Cash (Dinheiro) =====
    [JsonPropertyName("CashAccount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CashAccount { get; set; }

    [JsonPropertyName("CashSum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CashSum { get; set; }

    // ===== Bank transfer (Transferência) =====
    [JsonPropertyName("TransferAccount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TransferAccount { get; set; }

    [JsonPropertyName("TransferSum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TransferSum { get; set; }

    [JsonPropertyName("TransferDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? TransferDate { get; set; }

    [JsonPropertyName("PaymentInvoices")]
    public List<SapPaymentInvoice> PaymentInvoices { get; set; } = new();

    [JsonPropertyName("PaymentCreditCards")]
    public List<SapPaymentCreditCard> PaymentCreditCards { get; set; } = new();
}

/// <summary>The invoice (or installment) being cleared by the payment.</summary>
public sealed class SapPaymentInvoice
{
    [JsonPropertyName("DocEntry")]
    public int DocEntry { get; set; }

    [JsonPropertyName("InvoiceType")]
    public string InvoiceType { get; set; } = "it_Invoice";

    [JsonPropertyName("SumApplied")]
    public decimal SumApplied { get; set; }

    [JsonPropertyName("TotalDiscount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TotalDiscount { get; set; }

    [JsonPropertyName("DiscountPercent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DiscountPercent { get; set; }
}

/// <summary>Credit/debit card payment means (CartaoC / CartaoD).</summary>
public sealed class SapPaymentCreditCard
{
    [JsonPropertyName("CreditCard")]
    public int CreditCard { get; set; }

    [JsonPropertyName("CreditAcct")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreditAcct { get; set; }

    [JsonPropertyName("CreditCardNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreditCardNumber { get; set; }

    [JsonPropertyName("VoucherNum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VoucherNum { get; set; }

    [JsonPropertyName("PaymentMethodCode")]
    public int PaymentMethodCode { get; set; }

    [JsonPropertyName("NumOfPayments")]
    public int NumOfPayments { get; set; }

    [JsonPropertyName("FirstPaymentDue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? FirstPaymentDue { get; set; }

    [JsonPropertyName("CreditSum")]
    public decimal CreditSum { get; set; }
}
