using FinancialImport.Domain.Entities;

namespace FinancialImport.Application.Settings;

/// <summary>
/// Servico de configuracoes do sistema lidas do banco de dados.
/// Usa cache em memoria (TTL 5 min) para evitar queries a cada requisicao.
/// </summary>
public interface ISystemSettingsService
{
    /// <summary>Leitura sincrona a partir do cache (requer PreloadCacheAsync antes do primeiro uso).</summary>
    string? Get(string key);

    /// <summary>Retorna todas as entradas do cache cujas chaves iniciam com o prefixo informado.</summary>
    IReadOnlyDictionary<string, string?> GetByPrefix(string prefix);

    /// <summary>Leitura assincrona — garante que o cache esta populado antes de retornar.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Retorna todos os settings de uma categoria.</summary>
    Task<IReadOnlyList<SystemSetting>> GetCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>Retorna todos os settings do sistema.</summary>
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Persiste um valor e invalida o cache.</summary>
    Task SetAsync(string key, string? value, string updatedBy, CancellationToken ct = default);

    /// <summary>Persiste varios valores em uma unica operacao e invalida o cache.</summary>
    Task SetManyAsync(IDictionary<string, string?> values, string updatedBy, CancellationToken ct = default);

    /// <summary>Carrega todos os settings do banco na memoria. Deve ser chamado na inicializacao da aplicacao.</summary>
    Task PreloadCacheAsync(CancellationToken ct = default);

    /// <summary>Forca recarga do cache na proxima leitura.</summary>
    void InvalidateCache();
}
