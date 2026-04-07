namespace FinancialImport.Application.Abstractions;

public interface ILoginAuditContextAccessor
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
