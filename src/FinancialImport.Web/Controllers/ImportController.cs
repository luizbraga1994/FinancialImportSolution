using FinancialImport.Application.Imports;
using FinancialImport.Domain.Constants;
using FinancialImport.Web.Models;
using FinancialImport.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialImport.Web.Controllers;

[Authorize(Policy = PermissionCodes.ImportarLancamentos)]
public sealed class ImportController : Controller
{
    private readonly IImportService _importService;
    private readonly IImportFileReader _fileReader;
    private readonly ILogger<ImportController> _logger;

    public ImportController(IImportService importService, IImportFileReader fileReader, ILogger<ImportController> logger)
    {
        _importService = importService;
        _fileReader = fileReader;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Upload()
    {
        return View(new UploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(UploadViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.File == null)
        {
            return View(model);
        }

        try
        {
            var context = await _fileReader.ReadAsync(model.File, cancellationToken);
            if (!string.IsNullOrWhiteSpace(model.Layout))
            {
                context.DetectedLayout = model.Layout;
            }
            context.BranchDefault = model.BranchDefault;
            context.UseBranchFromFile = model.UseBranchFromFile;
            var result = await _importService.PreviewAsync(context, cancellationToken);

            var viewModel = new PreviewViewModel
            {
                ImportFileId = result.ImportFileId,
                LayoutDetected = result.LayoutDetected,
                TotalLines = result.Lines.Count,
                InvalidLines = result.Errors.Count,
                Errors = result.Errors
            };

            return View("Preview", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar upload.");
            ModelState.AddModelError(string.Empty, "Erro ao processar o arquivo.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(long importFileId, CancellationToken cancellationToken)
    {
        var result = await _importService.ProcessAsync(importFileId, cancellationToken);
        return View("Result", result);
    }
}
