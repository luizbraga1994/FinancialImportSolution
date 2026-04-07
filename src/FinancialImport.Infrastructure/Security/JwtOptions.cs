namespace FinancialImport.Infrastructure.Security;

public sealed class JwtOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "FinancialImport";
    public string Audience { get; set; } = "FinancialImportClients";
    public int ExpirationMinutes { get; set; } = 480;
    public int RefreshExpirationMinutes { get; set; } = 1440;
}
