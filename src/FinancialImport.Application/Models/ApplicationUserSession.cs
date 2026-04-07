namespace FinancialImport.Application.Models;

public sealed class ApplicationUserSession
{
    public long UserId { get; init; }
    public string Login { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Profiles { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> AllowedCompanies { get; init; } = Array.Empty<string>();
    public bool IsGlobalAdmin { get; init; }
}
