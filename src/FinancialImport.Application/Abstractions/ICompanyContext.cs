namespace FinancialImport.Application.Abstractions;

public interface ICompanyContext
{
    string? CompanyDb { get; }
    string? CompanyName { get; }
    string? SapUserName { get; }
}
