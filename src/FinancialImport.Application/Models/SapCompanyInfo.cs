namespace FinancialImport.Application.Models;

public sealed class SapCompanyInfo
{
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Server { get; set; }
    public bool IsActive { get; set; }
}
