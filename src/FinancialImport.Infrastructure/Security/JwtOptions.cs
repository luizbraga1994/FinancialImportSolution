using System.ComponentModel.DataAnnotations;

namespace FinancialImport.Infrastructure.Security;

public sealed class JwtOptions
{
    public const int MinKeyLengthBytes = 32;

    [Required, MinLength(MinKeyLengthBytes)]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "FinancialImport";

    [Required]
    public string Audience { get; set; } = "FinancialImportClients";

    [Range(1, 1440)]
    public int ExpirationMinutes { get; set; } = 480;

    [Range(1, 10080)]
    public int RefreshExpirationMinutes { get; set; } = 1440;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Jwt:SecretKey nao pode ser vazio.");

        if (SecretKey.Length < MinKeyLengthBytes)
            throw new InvalidOperationException($"Jwt:SecretKey deve ter no minimo {MinKeyLengthBytes} caracteres para seguranca HMAC-SHA256.");

        if (SecretKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Jwt:SecretKey ainda esta com o valor padrao. Altere para uma chave segura.");
    }
}
