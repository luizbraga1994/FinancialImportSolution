using System.Security.Cryptography;
using System.Text;
using FinancialImport.Application.Abstractions;

namespace FinancialImport.Infrastructure.Hashing;

public sealed class Sha256HashService : IHashService
{
    public string ComputeHash(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        return ComputeHash(bytes);
    }
}
