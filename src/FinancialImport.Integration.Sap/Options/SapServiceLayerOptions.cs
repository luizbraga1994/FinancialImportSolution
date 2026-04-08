namespace FinancialImport.Integration.Sap.Options;

public sealed class SapServiceLayerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Language { get; set; } = 29;
    public bool IgnoreSslErrors { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxRetryAttempts { get; set; } = 3;
    public int SessionTimeoutMinutes { get; set; } = 25;

    // Backwards-compatible aliases
    public int MaxRetries { get => MaxRetryAttempts; set => MaxRetryAttempts = value; }
    public int SessionDurationMinutes { get => SessionTimeoutMinutes; set => SessionTimeoutMinutes = value; }
}
