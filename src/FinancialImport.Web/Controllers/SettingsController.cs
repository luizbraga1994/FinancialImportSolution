using FinancialImport.Application.Settings;
using FinancialImport.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<SettingsController> _logger;

    private static readonly string[] Categories =
        ["HANA", "SAP", "Seguranca", "Importacao", "Mensageria", "Layout"];

    public SettingsController(ISystemSettingsService settings, ILogger<SettingsController> logger)
    {
        _settings = settings;
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
            var formValue = form[setting.Chave].FirstOrDefault();
            // For password fields: empty means "keep existing"
            if (setting.TipoDado == "password" && string.IsNullOrEmpty(formValue))
                continue;
            updates[setting.Chave] = string.IsNullOrWhiteSpace(formValue) ? null : formValue.Trim();
        }

        if (updates.Count > 0)
        {
            await _settings.SetManyAsync(updates, userId, ct);
            _logger.LogInformation("Categoria '{Cat}' salva por '{User}' ({Count} campos).", categoria, userId, updates.Count);
            TempData["Success"] = $"Configuracoes da categoria '{categoria}' salvas com sucesso.";
        }

        return RedirectToAction(nameof(Index), new { categoria });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestHana(CancellationToken ct)
    {
        // Invalidate cache so latest DB values are used
        _settings.InvalidateCache();
        await _settings.PreloadCacheAsync(ct);

        TempData["Info"] = "Cache de configuracoes recarregado. Teste a conexao HANA fazendo login.";
        return RedirectToAction(nameof(Index), new { categoria = "HANA" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ReloadCache()
    {
        _settings.InvalidateCache();
        TempData["Success"] = "Cache de configuracoes invalidado. Proximas requisicoes recarregarao do banco.";
        return RedirectToAction(nameof(Index));
    }
}
