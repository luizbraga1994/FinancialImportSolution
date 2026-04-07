namespace FinancialImport.Shared.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
}
