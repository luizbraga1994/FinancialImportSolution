using System.Globalization;
using System.Text;
using FinancialImport.Application.Imports;
using FinancialImport.Shared.Imports;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Builds the deduplication key for an import line using the fields
/// configured in <see cref="ImportProcessingOptions.DeduplicationKey"/>.
/// ALWAYS includes the external control identifier (<c>SeqLancamento</c>)
/// when present and configured so similar lines with different ids are
/// NEVER considered duplicates — this is the core invariant of the
/// recent "ID de controle" improvement.
/// </summary>
public sealed class BusinessKeyBuilder
{
    private readonly ImportProcessingOptions _options;

    public BusinessKeyBuilder(IOptions<ImportProcessingOptions> options)
    {
        _options = options.Value;
    }

    public string BuildBusinessKey(string companyDb, LancamentoContabilImportado line)
    {
        var sb = new StringBuilder();
        var f = _options.DeduplicationKey;

        if (f.IncludeCompanyDb) Append(sb, companyDb);

        // SeqLancamento (the recent control identifier). Critical: when
        // the source provides an id, it MUST be part of the key so two
        // legitimately distinct records with identical content are not
        // flagged as duplicates.
        if (f.IncludeSeqLancamento && !string.IsNullOrWhiteSpace(line.SeqLancamento))
            Append(sb, "seq=" + line.SeqLancamento);

        if (f.IncludeReference) Append(sb, line.Referencia);
        if (f.IncludeAccounts) Append(sb, line.ContaContabil + "->" + line.ContaContrapartida);
        if (f.IncludeDates)
        {
            Append(sb, line.DataLancamento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Append(sb, line.DataVencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Append(sb, line.DataDocumento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (f.IncludeAmount)
        {
            Append(sb, line.Valor.ToString("0.00", CultureInfo.InvariantCulture));
            if (line.ValorCredito.HasValue)
                Append(sb, "c=" + line.ValorCredito.Value.ToString("0.00", CultureInfo.InvariantCulture));
            if (line.ValorDebito.HasValue)
                Append(sb, "d=" + line.ValorDebito.Value.ToString("0.00", CultureInfo.InvariantCulture));
        }
        if (f.IncludeMemo) Append(sb, line.HistoricoLinha);
        if (f.IncludeBranch) Append(sb, line.Filial ?? string.Empty);

        return sb.ToString();
    }

    /// <summary>
    /// Stable group key used to merge lines into a single SAP
    /// JournalEntry. Two lines with the same Reference, dates and the
    /// same SeqLancamento end up in the same group, while lines with
    /// different SeqLancamento are dispatched as independent journals.
    /// </summary>
    public (string key, string hash) BuildGroupKey(
        string companyDb,
        string reference,
        DateTime postingDate,
        DateTime dueDate,
        DateTime documentDate,
        string? seqLancamento)
    {
        var sb = new StringBuilder();
        Append(sb, companyDb);
        Append(sb, reference);
        Append(sb, postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(sb, dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(sb, documentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(seqLancamento))
            Append(sb, "seq=" + seqLancamento);
        var key = sb.ToString();
        var hash = Sha256Hex(key);
        return (key, hash);
    }

    public static string Sha256Hex(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string? value)
    {
        if (sb.Length > 0) sb.Append('|');
        sb.Append(value ?? string.Empty);
    }
}
