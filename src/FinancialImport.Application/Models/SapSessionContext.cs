namespace FinancialImport.Application.Models;

public sealed class SapSessionContext
{
    public string CompanyDb { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string? RouteId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string SapUserName { get; init; } = string.Empty;
}
