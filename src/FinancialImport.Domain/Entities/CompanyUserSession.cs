namespace FinancialImport.Domain.Entities;

public sealed class CompanyUserSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SapUserName { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? RouteId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime LoginAt { get; set; }

    public User? User { get; set; }
}
