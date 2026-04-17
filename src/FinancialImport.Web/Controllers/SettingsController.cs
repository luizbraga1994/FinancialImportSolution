using FinancialImport.Application.Settings;
using FinancialImport.Domain.Entities;
using FinancialImport.Shared.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;
    private readonly ILogger<SettingsController> _logger;

    private static readonly string[] Categories =
        ["SAP", "Seguranca", "Importacao", "Mensageria", "Layout"];

    public SettingsController(ISystemSettingsService settings, IAuditLogger audit, ILogger<SettingsController> logger)
    {
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? categoria, CancellationToken ct)
    {
        var all = await _settings.GetAllAsync(ct);
        var activeCategory = categoria ?? Categories[0];
        var grouped = all
            .GroupBy(s => s.Categoria)
            .OrderBy(g => Array.IndexOf(Categories, g.Key) is var i && i >= 0 ? i : 99)
            .ThenBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Chave).ToList());

        ViewBag.Categories = Categories;
        ViewBag.ActiveCategory = activeCategory;
        ViewBag.Grouped = grouped;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategory(string categoria, IFormCollection form, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimConstants.Login)?.Value ?? "sistema";

        var settings = await _settings.GetCategoryAsync(categoria, ct);
        var updates = new Dictionary<string, string?>();

        foreach (var setting in settings)
        {
            if (setting.TipoDado == "bool")
            {
                var isChecked = form[setting.Chave].Any(v => v == "true");
                updates[setting.Chave] = isChecked ? "true" : "false";
                continue;
            }

            var formValue = form[setting.Chave].FirstOrDefault();

            if (setting.TipoDado == "password" && string.IsNullOrEmpty(formValue))
                continue;

            updates[setting.Chave] = string.IsNullOrWhiteSpace(formValue) ? null : formValue.Trim();
        }

        if (updates.Count > 0)
        {
            await _settings.SetManyAsync(updates, userId, ct);
            _logger.LogInformation("Categoria '{Cat}' salva por '{User}' ({Count} campos).", categoria, userId, updates.Count);

            // Log which keys were changed (mask password values)
            var changedKeys = updates.Select(u =>
                u.Key.Contains("Password", StringComparison.OrdinalIgnoreCase) || u.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                    ? $"{u.Key} = ***"
                    : $"{u.Key} = {u.Value ?? "(null)"}");

            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Info,
                Category = LogCategories.Audit,
                Source = nameof(SettingsController),
                Operation = "AlterarConfiguracoes",
                Message = $"Configuracoes da categoria '{categoria}' alteradas por '{userId}' ({updates.Count} campo(s)).",
                Details = $"Usuario: {userId}\nCategoria: {categoria}\nCampos alterados ({updates.Count}):\n" + string.Join("\n", changedKeys)
            }, ct);

            TempData["Success"] = $"Configuracoes da categoria '{categoria}' salvas com sucesso.";
        }

        return RedirectToAction(nameof(Index), new { categoria });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReloadCache(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimConstants.Login)?.Value ?? "sistema";
        _settings.InvalidateCache();

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Info,
            Category = LogCategories.Audit,
            Source = nameof(SettingsController),
            Operation = "RecarregarCache",
            Message = $"Cache de configuracoes invalidado manualmente por '{userId}'."
        }, ct);

        TempData["Success"] = "Cache de configuracoes invalidado. Proximas requisicoes recarregarao do banco.";
        return RedirectToAction(nameof(Index));
    }
}
