using FinancialImport.Application.Settings;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Settings;

/// <summary>
/// Implementacao de ISystemSettingsService que le do banco MySQL via EF Core.
/// O cache em memoria e atualizado a cada 5 minutos ou quando um valor e salvo.
/// </summary>
public sealed class DbSystemSettingsService : ISystemSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DbSystemSettingsService> _logger;

    private volatile Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheLoadedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public DbSystemSettingsService(IServiceScopeFactory scopeFactory, ILogger<DbSystemSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string? Get(string key)
    {
        _cache.TryGetValue(key, out var value);
        return value;
    }

    public IReadOnlyDictionary<string, string?> GetByPrefix(string prefix)
    {
        var snapshot = _cache;
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in snapshot)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result[key] = value;
        }
        return result;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct);
        return Get(key);
    }

    public async Task<IReadOnlyList<SystemSetting>> GetCategoryAsync(string category, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Categoria == category)
            .OrderBy(s => s.Chave)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SystemSettings
            .AsNoTracking()
            .OrderBy(s => s.Categoria)
            .ThenBy(s => s.Chave)
            .ToListAsync(ct);
    }

    public async Task SetAsync(string key, string? value, string updatedBy, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Chave == key, ct);
        if (existing != null)
        {
            existing.Valor = value;
            existing.AtualizadoEm = DateTime.Now;
            existing.AtualizadoPor = updatedBy;
        }
        else
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Chave = key,
                Valor = value,
                Categoria = key.Contains(':') ? key.Split(':')[0] : "Geral",
                AtualizadoEm = DateTime.Now,
                AtualizadoPor = updatedBy
            });
        }

        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(ct);
        _logger.LogInformation("Configuracao '{Key}' atualizada por '{User}'.", key, updatedBy);
    }

    public async Task SetManyAsync(IDictionary<string, string?> values, string updatedBy, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keys = values.Keys.ToList();
        var existing = await db.SystemSettings
            .Where(s => keys.Contains(s.Chave))
            .ToListAsync(ct);

        var existingByKey = existing.ToDictionary(s => s.Chave, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;

        foreach (var (key, value) in values)
        {
            if (existingByKey.TryGetValue(key, out var row))
            {
                row.Valor = value;
                row.AtualizadoEm = now;
                row.AtualizadoPor = updatedBy;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Chave = key,
                    Valor = value,
                    Categoria = key.Contains(':') ? key.Split(':')[0] : "Geral",
                    AtualizadoEm = now,
                    AtualizadoPor = updatedBy
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await ReloadCacheAsync(ct);
        _logger.LogInformation("{Count} configuracoes atualizadas por '{User}'.", values.Count, updatedBy);
    }

    public async Task PreloadCacheAsync(CancellationToken ct = default)
    {
        await LoadCacheAsync(ct);
    }

    public void InvalidateCache()
    {
        _cacheLoadedAt = DateTime.MinValue;
    }

    /// <summary>
    /// Forces an immediate cache reload so synchronous <see cref="Get"/>
    /// callers see updated values right away (not just after TTL expiry).
    /// </summary>
    private async Task ReloadCacheAsync(CancellationToken ct)
    {
        _cacheLoadedAt = DateTime.MinValue;
        await LoadCacheAsync(ct);
    }

    private async Task EnsureCacheAsync(CancellationToken ct)
    {
        if (_cache.Count > 0 && DateTime.Now - _cacheLoadedAt < CacheTtl)
            return;
        await LoadCacheAsync(ct);
    }

    private async Task LoadCacheAsync(CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            // double-check after acquiring lock
            if (_cache.Count > 0 && DateTime.Now - _cacheLoadedAt < CacheTtl)
                return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var all = await db.SystemSettings.AsNoTracking().ToListAsync(ct);

            _cache = all.ToDictionary(s => s.Chave, s => s.Valor, StringComparer.OrdinalIgnoreCase);
            _cacheLoadedAt = DateTime.Now;
            _logger.LogDebug("Cache de configuracoes carregado com {Count} entradas.", all.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar cache de configuracoes do banco.");
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
