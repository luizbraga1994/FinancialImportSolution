namespace FinancialImport.Integration.Hana.Options;

/// <summary>
/// Configurações de conexão com SAP HANA.
/// Seção: HanaDbConnection no appsettings.json
/// </summary>
public sealed class HanaOptions
{
    public string Server { get; set; } = string.Empty;
    public string Port { get; set; } = "30015";
    public string Database { get; set; } = string.Empty;
    public string UserID { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 10;
    public int ConnectionTimeout { get; set; } = 60;
    public int CommandTimeout { get; set; } = 300;

    public string ProviderInvariantName { get; set; } = "Sap.Data.Hana";
    public string? ProviderAssemblyPath { get; set; }

    /// <summary>
    /// Monta a connection string para o SAP HANA.
    /// </summary>
    public string BuildConnectionString()
    {
        return BuildConnectionString(null);
    }

    /// <summary>
    /// Monta a connection string para o SAP HANA, opcionalmente apontando o
    /// schema atual (CS) para o banco da empresa informado. Útil para consultar
    /// objetos (OINV, CRD7, ONFM) no contexto da empresa selecionada.
    /// </summary>
    public string BuildConnectionString(string? schemaOverride)
    {
        var serverAddr = !string.IsNullOrWhiteSpace(Port) && !Server.Contains(':')
            ? $"{Server}:{Port}"
            : Server;

        var schema = string.IsNullOrWhiteSpace(schemaOverride) ? Database : schemaOverride;

        return $"Server={serverAddr};UserID={UserID};Password={Password};CS={schema}" +
               $";Pooling=true;MaxPoolSize={MaxPoolSize};MinPoolSize={MinPoolSize}" +
               $";Connection Timeout={ConnectionTimeout};CommandTimeout={CommandTimeout}";
    }
}
