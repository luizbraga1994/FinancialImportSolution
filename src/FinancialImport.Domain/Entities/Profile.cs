namespace FinancialImport.Domain.Entities;

public sealed class Profile
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public ICollection<UserProfile> Users { get; set; } = new List<UserProfile>();
    public ICollection<ProfilePermission> Permissions { get; set; } = new List<ProfilePermission>();
}
