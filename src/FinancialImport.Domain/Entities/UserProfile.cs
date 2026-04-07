namespace FinancialImport.Domain.Entities;

public sealed class UserProfile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long ProfileId { get; set; }

    public User? User { get; set; }
    public Profile? Profile { get; set; }
}
