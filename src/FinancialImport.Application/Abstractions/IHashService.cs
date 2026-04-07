namespace FinancialImport.Application.Abstractions;

public interface IHashService
{
    string ComputeHash(byte[] data);
    string ComputeHash(string input);
}
