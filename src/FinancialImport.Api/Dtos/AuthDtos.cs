using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Api.Dtos;

public sealed class LoginRequest
{
    [Required] public string Login { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsGlobalAdmin { get; set; }
    public IReadOnlyCollection<string> Profiles { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> AllowedCompanies { get; set; } = Array.Empty<string>();
}

public sealed class RefreshTokenRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}
