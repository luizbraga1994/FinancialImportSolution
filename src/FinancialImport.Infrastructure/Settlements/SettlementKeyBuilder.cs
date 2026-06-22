using System.Globalization;
using System.Text;
using FinancialImport.Application.Settlements;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Builds the canonical deduplication / grouping key for a settlement line from
/// its fiscal note key (company + DocPN + serial + series + model). Each note is
/// settled by exactly one Incoming Payment, so the business key and the group
/// key are the same canonical string.
/// </summary>
public static class SettlementKeyBuilder
{
    public static string BuildBusinessKey(string companyDb, SettlementLancamento line)
    {
        var sb = new StringBuilder();
        Append(sb, companyDb);
        Append(sb, Normalize(line.DocPN));
        Append(sb, Normalize(line.NumeroNota));
        Append(sb, Normalize(line.Serie));
        Append(sb, Normalize(line.Modelo));
        return sb.ToString();
    }

    public static string BuildGroupKeyLabel(string docPn, string nota, string? serie, string modelo)
        => $"{docPn}|{nota}|{serie}|{modelo}";

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToUpper(CultureInfo.InvariantCulture);

    private static void Append(StringBuilder sb, string? value)
    {
        if (sb.Length > 0) sb.Append('|');
        sb.Append(value ?? string.Empty);
    }
}
