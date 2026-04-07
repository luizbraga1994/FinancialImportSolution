using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Web.Models;

public sealed class LoginViewModel
{
    [Required]
    public string Login { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? Error { get; set; }
}
