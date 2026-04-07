namespace FinancialImport.Application.Abstractions;

public interface IUserContext
{
    long? UserId { get; }
    string? Login { get; }
    string? Name { get; }
}
