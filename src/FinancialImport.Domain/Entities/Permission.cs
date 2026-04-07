namespace FinancialImport.Domain.Entities;

public sealed class Permission
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool IsActive { get; set; }

    public ICollection<ProfilePermission> Profiles { get; set; } = new List<ProfilePermission>();
}
