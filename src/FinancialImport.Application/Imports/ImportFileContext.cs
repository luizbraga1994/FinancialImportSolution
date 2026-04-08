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
            return 0m;

        culture ??= CultureInfo.InvariantCulture;

        // Handle Brazilian format (1.234,56) by normalizing
        var normalized = value.Trim();
        if (culture.Equals(CultureInfo.InvariantCulture) && normalized.Contains(','))
        {
            // If has both . and , assume Brazilian format
            if (normalized.Contains('.') && normalized.LastIndexOf(',') > normalized.LastIndexOf('.'))
            {
                normalized = normalized.Replace(".", "").Replace(",", ".");
            }
            else if (!normalized.Contains('.'))
            {
                normalized = normalized.Replace(",", ".");
            }
        }

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    public DateTime GetDate(string columnName, CultureInfo? culture = null)
    {
        var value = Get(columnName);
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.MinValue;

        culture ??= CultureInfo.InvariantCulture;

        // Try common date formats
        var formats = new[]
        {
            "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy",
            "MM/dd/yyyy", "yyyyMMdd", "dd/MM/yyyy HH:mm:ss"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, culture,
            DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(value.Trim(), culture, DateTimeStyles.None, out var fallback)
            ? fallback
            : DateTime.MinValue;
    }
}
