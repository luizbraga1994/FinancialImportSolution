namespace FinancialImport.Domain.Entities;

/// <summary>
/// Configuracao do sistema armazenada no banco de dados.
/// Substitui as secoes de appsettings.json (exceto a string de conexao).
/// </summary>
public sealed class SystemSetting
{
    public long Id { get; set; }

    /// <summary>Chave no formato Categoria:Subcategoria (ex: "Hana:Server").</summary>
    public string Chave { get; set; } = string.Empty;

    /// <summary>Valor da configuracao (sempre em string; conversao feita pelo servico).</summary>
    public string? Valor { get; set; }

    /// <summary>Categoria de agrupamento (ex: "HANA", "SAP", "Seguranca", "Importacao", "Mensageria").</summary>
    public string Categoria { get; set; } = string.Empty;

    /// <summary>Descricao legivel para exibicao no painel de administracao.</summary>
    public string? Descricao { get; set; }

    /// <summary>Tipo de dado: string | int | bool | password | json | list.</summary>
    public string TipoDado { get; set; } = "string";

    /// <summary>Indica se e um campo obrigatorio para o sistema funcionar.</summary>
    public bool Obrigatorio { get; set; }

    public DateTime? AtualizadoEm { get; set; }
    public string? AtualizadoPor { get; set; }
}
