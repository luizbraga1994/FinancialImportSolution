namespace FinancialImport.Domain.Entities;

public sealed class BranchMapping
{
    public long Id { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string FileBranchCode { get; set; } = string.Empty;
    public int BplId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
