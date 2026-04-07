namespace FinancialImport.Domain.Entities;

public sealed class User
{
    public long Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[]? PasswordSalt { get; set; }
    public bool IsActive { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public ICollection<UserProfile> Profiles { get; set; } = new List<UserProfile>();
    public ICollection<UserCompanyPermission> AllowedCompanies { get; set; } = new List<UserCompanyPermission>();
    public ICollection<CompanyUserSession> CompanySessions { get; set; } = new List<CompanyUserSession>();
}
