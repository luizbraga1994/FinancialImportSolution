namespace FinancialImport.Integration.Hana.Options;

public sealed class HanaOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ProviderInvariantName { get; set; } = "Sap.Data.Hana";
    public string? ProviderAssemblyPath { get; set; }
}
