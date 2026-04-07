namespace FinancialImport.Api.Dtos;

public sealed class ImportPreviewResponse
{
    public long ImportFileId { get; set; }
    public string LayoutDetected { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int DuplicatedLines { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
}

public sealed class ImportProcessResponse
{
    public long ImportFileId { get; set; }
    public int Imported { get; set; }
    public int Duplicated { get; set; }
    public int Invalid { get; set; }
    public int SapErrors { get; set; }
}

public sealed class ImportHistoryDto
{
    public long Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string CompanyDb { get; set; } = string.Empty;
    public string LayoutDetected { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int ImportedLines { get; set; }
    public int InvalidLines { get; set; }
    public int DuplicatedLines { get; set; }
    public int LinesWithError { get; set; }
    public DateTime ImportedAt { get; set; }
    public long UserId { get; set; }
}

public sealed class ImportLineDto
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string ContraAccountCode { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public decimal Amount { get; set; }
    public decimal? CreditAmount { get; set; }
    public decimal? DebitAmount { get; set; }
    public string LineMemo { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ValidationMessage { get; set; }
    public string? SapReturnMessage { get; set; }
    public int? SapDocEntry { get; set; }
}

public sealed class PagedResult<T>
{
    public IReadOnlyCollection<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
