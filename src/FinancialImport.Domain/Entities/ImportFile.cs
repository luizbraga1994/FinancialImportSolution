using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

public sealed class ImportFile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string LayoutDetected { get; set; } = string.Empty;
    public string? BranchDefault { get; set; }
    public bool UseBranchFromFile { get; set; }
    public ImportStatus Status { get; set; }
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int ImportedLines { get; set; }
    public int DuplicatedLines { get; set; }
    public int LinesWithError { get; set; }
    public DateTime ImportedAt { get; set; }

    public User? User { get; set; }
    public ICollection<ImportLine> Lines { get; set; } = new List<ImportLine>();
}
