using System.Globalization;
using System.Text;

namespace FinancialImport.Application.Settlements;

public enum PaymentMeans
{
    Unknown = 0,
    Cash = 1,
    Transfer = 2,
    Card = 3
}

/// <summary>
/// Classifies the spreadsheet "FormaDePagamento" into a payment means. Shared by
/// the validator (which relaxes the ContaContabil rule for cards) and the
/// Incoming Payment builder so both agree on what counts as a card payment.
/// </summary>
public static class PaymentMeansClassifier
{
    public static PaymentMeans Classify(string? raw)
    {
        var norm = RemoveAccents(raw ?? string.Empty).Trim().ToLowerInvariant();
        if (norm.Length == 0) return PaymentMeans.Unknown;
        if (norm.Contains("dinheiro") || norm.Contains("cash") || norm.Contains("especie")) return PaymentMeans.Cash;
        if (norm.Contains("transfer")) return PaymentMeans.Transfer;
        if (norm.Contains("cartao") || norm.Contains("card")) return PaymentMeans.Card;
        return PaymentMeans.Unknown;
    }

    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
