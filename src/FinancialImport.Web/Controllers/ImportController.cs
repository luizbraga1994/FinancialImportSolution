using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize]
public class ImportController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Selecione um arquivo para importar.";
            return View(nameof(Index));
        }

        var allowedExtensions = new[] { ".csv", ".txt", ".xlsx" };
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
        {
            TempData["Error"] = "Extensao de arquivo nao permitida. Use CSV, TXT ou XLSX.";
            return View(nameof(Index));
        }

        const long maxFileSize = 10 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            TempData["Error"] = "Arquivo excede o tamanho maximo de 10 MB.";
            return View(nameof(Index));
        }

        // TODO: Implement file import processing via IImportService
        await Task.CompletedTask;

        TempData["Success"] = "Arquivo enviado com sucesso.";
        return RedirectToAction(nameof(Index));
    }
}
