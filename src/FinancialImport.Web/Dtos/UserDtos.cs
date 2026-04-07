using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Web.Dtos;

public sealed class UserDto
{
    public long Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsGlobalAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public IReadOnlyCollection<string> Profiles { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> AllowedCompanies { get; set; } = Array.Empty<string>();
}

public sealed class CreateUserRequest
{
    [Required] [MaxLength(80)] public string Login { get; set; } = string.Empty;
    [Required] [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required] [MaxLength(120)] [EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] [MinLength(8)] public string Password { get; set; } = string.Empty;
    public bool IsGlobalAdmin { get; set; }
    public List<long> ProfileIds { get; set; } = new();
    public List<string> AllowedCompanyDbs { get; set; } = new();
}

public sealed class UpdateUserRequest
{
    [Required] [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required] [MaxLength(120)] [EmailAddress] public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsGlobalAdmin { get; set; }
    public List<long> ProfileIds { get; set; } = new();
    public List<string> AllowedCompanyDbs { get; set; } = new();
}

public sealed class ChangePasswordRequest
{
    [Required] [MinLength(8)] public string NewPassword { get; set; } = string.Empty;
}
