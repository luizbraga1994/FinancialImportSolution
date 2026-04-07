using System.ComponentModel.DataAnnotations;
using FinancialImport.Application.Models;

namespace FinancialImport.Web.Models;

public sealed class CompanySelectionViewModel
{
    public IReadOnlyCollection<SapCompanyInfo> Companies { get; set; } = Array.Empty<SapCompanyInfo>();

    [Required]
    public string CompanyDb { get; set; } = string.Empty;

    [Required]
    public string SapUserName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string SapPassword { get; set; } = string.Empty;

    public string? Error { get; set; }
}
