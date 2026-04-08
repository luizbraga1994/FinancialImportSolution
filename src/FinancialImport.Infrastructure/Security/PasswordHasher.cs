using System.Security.Cryptography;

namespace FinancialImport.Infrastructure.Security;

public sealed class PasswordHasher
{
    private const int Iterations = 200_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public (byte[] Hash, byte[] Salt) HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        return (hash, salt);
    }

    public byte[] HashPassword(string password, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));
        ArgumentNullException.ThrowIfNull(salt, nameof(salt));

        if (salt.Length < SaltSize)
            throw new ArgumentException($"Salt deve ter no minimo {SaltSize} bytes.", nameof(salt));

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    public bool Verify(string password, byte[] salt, byte[] hash)
    {
        if (string.IsNullOrWhiteSpace(password) || salt == null || hash == null)
            return false;

        var computed = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }
}
