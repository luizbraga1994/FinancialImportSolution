namespace FinancialImport.Domain.Entities;

public sealed class Rule
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? ScopeCompanyDb { get; set; }
    public bool IsActive { get; set; }
}
