namespace FinancialImport.Domain.Constants;

public static class PermissionCodes
{
    public const string ImportarLancamentos = "importar_lancamentos";
    public const string VisualizarHistorico = "visualizar_historico";
    public const string ReprocessarImportacao = "reprocessar_importacao";
    public const string TrocarCompany = "trocar_company";
    public const string GerenciarUsuarios = "gerenciar_usuarios";
    public const string GerenciarPerfis = "gerenciar_perfis";
    public const string GerenciarPermissoes = "gerenciar_permissoes";
    public const string VisualizarLogs = "visualizar_logs";

    /// <summary>
    /// Canonical list used to automatically build authorization
    /// policies in Program.cs — adding a new code here automatically
    /// wires the matching policy.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ImportarLancamentos,
        VisualizarHistorico,
        ReprocessarImportacao,
        TrocarCompany,
        GerenciarUsuarios,
        GerenciarPerfis,
        GerenciarPermissoes,
        VisualizarLogs
    };
}
