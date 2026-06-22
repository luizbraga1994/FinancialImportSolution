namespace FinancialImport.Domain.Entities;

/// <summary>
/// Maps a card brand from the settlement spreadsheet (e.g. "ELOCREDITO",
/// "ELODEBITO") to the SAP Business One credit-card master data required to
/// build the <c>PaymentCreditCards</c> section of an Incoming Payment.
/// Mirrors the configurable <see cref="BranchMapping"/> pattern.
/// </summary>
public sealed class CardBrandMapping
{
    public long Id { get; set; }
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>Brand name as it appears in the file ("Bandeira" column).</summary>
    public string BrandName { get; set; } = string.Empty;

    /// <summary>SAP credit card code (OCRC.CreditCard), e.g. 3 for ELOCREDITO.</summary>
    public int SapCreditCardCode { get; set; }

    /// <summary>SAP credit card payment method code (1 = single, 2 = installments).</summary>
    public int PaymentMethodCode { get; set; }

    /// <summary>
    /// Optional G/L account override (CreditAcct). When null, the account from
    /// the file's "ContaContabil" column is used.
    /// </summary>
    public string? CreditAccount { get; set; }

    public bool IsActive { get; set; }
}
