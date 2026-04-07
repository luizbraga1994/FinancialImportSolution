namespace FinancialImport.Domain.Entities;

public sealed class ProfilePermission
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public long PermissionId { get; set; }

    public Profile? Profile { get; set; }
    public Permission? Permission { get; set; }
}
