using System.Globalization;

namespace FinancialImport.Application.Imports;

public sealed class ImportFileContext
{
    public string FileName { get; init; } = string.Empty;
    public byte[] FileBytes { get; init; } = Array.Empty<byte>();
    public string[] Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<ImportRow> Rows { get; init; } = Array.Empty<ImportRow>();
    public string? DetectedLayout { get; set; }
    public string? BranchDefault { get; set; }
    public bool UseBranchFromFile { get; set; } = true;
}

public sealed class ImportRow
{
    private readonly Dictionary<string, string?> _values;

    public ImportRow(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public string? Get(string columnName)
        => _values.TryGetValue(columnName, out var value) ? value : null;

    public string GetRequired(string columnName)
        => Get(columnName) ?? string.Empty;

    public decimal GetDecimal(string columnName, CultureInfo? culture = null)
    {
        var value = Get(columnName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        culture ??= CultureInfo.InvariantCulture;
        return decimal.Parse(value, culture);
    }

    public DateTime GetDate(string columnName, CultureInfo? culture = null)
    {
        var value = Get(columnName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.MinValue;
        }

        culture ??= CultureInfo.InvariantCulture;
        return DateTime.Parse(value, culture);
    }
}
