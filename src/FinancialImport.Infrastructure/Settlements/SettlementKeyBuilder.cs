using System.Globalization;
using System.Text;
using FinancialImport.Application.Settlements;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Builds the deduplication and grouping keys for a settlement line.
///
/// A single note (fiscal key) may be settled by MORE THAN ONE payment — e.g.
/// part in cash and part by transfer — so the keys are per-PAYMENT, not per-note:
///
///  - <b>Business key</b> (cross-file dedup): fiscal key + payment means +
///    receiving account + amount + reference. Two distinct payments for the same
///    note differ here, so neither is flagged as a duplicate of the other.
///  - <b>Group key</b> (per-line dispatch/idempotency): the business key plus the
///    line ordinal, guaranteeing each spreadsheet row becomes exactly one
///    Incoming Payment even when two rows are otherwise identical.
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
        Append(sb, line.DataDocumento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(sb, Normalize(line.FormaPagamento));
        Append(sb, Normalize(line.ContaContabil));
        Append(sb, line.Valor.ToString("0.00", CultureInfo.InvariantCulture));
        Append(sb, Normalize(line.Referencia));
        return sb.ToString();
    }

    /// <summary>
    /// Per-line group key. Appends the row ordinal so two rows that share the
    /// same business key (same note + means + amount + reference) still produce
    /// two distinct payments instead of colliding on the unique index.
    /// </summary>
    public static string BuildGroupKey(string businessKey, int ordinal)
        => $"{businessKey}|#{ordinal}";

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
