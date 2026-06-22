using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Settlements;

/// <summary>
/// Input passed to the settlement preview: the uploaded spreadsheet content
/// already split into headers and rows. Reuses <see cref="ImportRow"/> for the
/// per-cell parsing helpers.
/// </summary>
public sealed class SettlementFileContext
{
    public string FileName { get; init; } = string.Empty;
    public byte[] FileBytes { get; init; } = Array.Empty<byte>();
    public string[] Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<ImportRow> Rows { get; init; } = Array.Empty<ImportRow>();
    public bool AllowDuplicate { get; set; } = false;
}
