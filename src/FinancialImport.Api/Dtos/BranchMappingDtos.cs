namespace FinancialImport.Api.Dtos;

public sealed class BranchMappingDto
{
    public long Id { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string FileBranchCode { get; set; } = string.Empty;
    public int BplId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CreateBranchMappingRequest
{
    public string CompanyDb { get; set; } = string.Empty;
    public string FileBranchCode { get; set; } = string.Empty;
    public int BplId { get; set; }
    public string BranchName { get; set; } = string.Empty;
}
