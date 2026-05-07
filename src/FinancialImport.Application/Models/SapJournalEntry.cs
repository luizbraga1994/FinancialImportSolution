using System.Text.Json.Serialization;

namespace FinancialImport.Application.Models;

public sealed class SapJournalEntry
{
    [JsonPropertyName("ReferenceDate")]
    public DateTime ReferenceDate { get; set; }

    [JsonPropertyName("DueDate")]
    public DateTime DueDate { get; set; }

    [JsonPropertyName("TaxDate")]
    public DateTime TaxDate { get; set; }

    [JsonPropertyName("Memo")]
    public string Memo { get; set; } = string.Empty;

    [JsonPropertyName("Reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("JournalEntryLines")]
    public List<SapJournalEntryLine> JournalEntryLines { get; set; } = new();
}

public sealed class SapJournalEntryLine
{
    /// <summary>G/L account code — set only when the code is numeric (e.g. "1.1.1.02.002").</summary>
    [JsonPropertyName("AccountCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountCode { get; set; }

    /// <summary>Business Partner code — set only when the code contains letters (e.g. "F00012").</summary>
    [JsonPropertyName("ShortName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortName { get; set; }

    [JsonPropertyName("Debit")]
    public decimal Debit { get; set; }

    [JsonPropertyName("Credit")]
    public decimal Credit { get; set; }

    [JsonPropertyName("LineMemo")]
    public string LineMemo { get; set; } = string.Empty;

    [JsonPropertyName("BPLID")]
    public int? BPLID { get; set; }

    [JsonPropertyName("CostingCode")]
    public string? CostingCode { get; set; }
}
