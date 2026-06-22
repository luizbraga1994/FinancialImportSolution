namespace FinancialImport.Application.Models;

/// <summary>
/// SAP credit-card catalog resolved from HANA: the card brands (OCRC) and the
/// payment-type codes (OCRP). Lets a settlement line resolve its card brand to
/// the SAP CreditCard code, G/L account and payment-method code without any
/// manually maintained mapping table.
/// </summary>
public sealed class SapCardCatalog
{
    /// <summary>Brand name (OCRC.CardName, normalized upper-case) -> brand info.</summary>
    public IReadOnlyDictionary<string, CardBrandInfo> Brands { get; init; }
        = new Dictionary<string, CardBrandInfo>();

    /// <summary>OCRP.CrTypeCode where InstalMent = 'N' (à vista).</summary>
    public int? SinglePaymentMethodCode { get; init; }

    /// <summary>OCRP.CrTypeCode where InstalMent = 'Y' (parcelado).</summary>
    public int? InstallmentMethodCode { get; init; }

    /// <summary>
    /// Resolves the card payment data for a brand and installment count, or null
    /// when the brand is unknown. The payment method follows the installment
    /// count: more than one installment uses the "parcelado" code, otherwise the
    /// "à vista" code (falling back to 2/1 when OCRP is unavailable).
    /// </summary>
    public CardPaymentInfo? Resolve(string? brand, int installments)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return null;

        if (!Brands.TryGetValue(brand.Trim().ToUpperInvariant(), out var info))
            return null;

        var methodCode = installments > 1
            ? (InstallmentMethodCode ?? 2)
            : (SinglePaymentMethodCode ?? 1);

        return new CardPaymentInfo
        {
            CreditCard = info.CreditCard,
            CreditAcct = info.AcctCode,
            PaymentMethodCode = methodCode
        };
    }
}

public sealed class CardBrandInfo
{
    public int CreditCard { get; init; }
    public string AcctCode { get; init; } = string.Empty;
}

public sealed class CardPaymentInfo
{
    public int CreditCard { get; init; }
    public string? CreditAcct { get; init; }
    public int PaymentMethodCode { get; init; }
}
