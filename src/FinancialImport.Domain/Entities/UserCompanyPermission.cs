namespace FinancialImport.Domain.Entities;

public sealed class UserCompanyPermission
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public User? User { get; set; }
}
