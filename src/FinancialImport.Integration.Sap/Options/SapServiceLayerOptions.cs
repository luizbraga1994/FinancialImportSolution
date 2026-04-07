namespace FinancialImport.Integration.Sap.Options;

public sealed class SapServiceLayerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 100;
    public int MaxRetries { get; set; } = 3;
    public int SessionDurationMinutes { get; set; } = 30;
}
