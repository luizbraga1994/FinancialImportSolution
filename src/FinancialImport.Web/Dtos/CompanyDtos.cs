using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Web.Dtos;

public sealed class CompanyDto
{
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Server { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CompanyLoginRequest
{
    [Required] public string CompanyDb { get; set; } = string.Empty;
    [Required] public string SapUserName { get; set; } = string.Empty;
    [Required] public string SapPassword { get; set; } = string.Empty;
}

public sealed class CompanyLoginResponse
{
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SapUserName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public sealed class CompanySessionDto
{
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SapUserName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
