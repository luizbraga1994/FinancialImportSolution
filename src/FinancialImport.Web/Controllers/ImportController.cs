using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Imports;
using FinancialImport.Shared.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialImport.Web.Controllers;

public class ImportPreviewViewModel
{
    public long ImportFileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string LayoutDetected { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int DuplicatedLines { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
    public List<ImportPreviewGroup> Groups { get; set; } = new();
    public ImportStatus Status { get; set; }
    public string? CorrelationId { get; set; }
}

public class ImportPreviewGroup
{
    public string Reference { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal TotalDebit { get; set; }
    public List<ImportLine> Lines { get; set; } = new();
}

[Authorize]
public class ImportController : Controller
{
    private readonly IImportService _importService;
    private readonly IImportFileReader _fileReader;
    private readonly AppDbContext _dbContext;
    private readonly ICompanyContext _companyContext;
    private readonly ImportProcessingOptions _processingOptions;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        IImportService importService,
        IImportFileReader fileReader,
        AppDbContext dbContext,
        ICompanyContext companyContext,
        IOptions<ImportProcessingOptions> processingOptions,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _fileReader = fileReader;
        _dbContext = dbContext;
        _companyContext = companyContext;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (string.IsNullOrWhiteSpace(_companyContext.CompanyDb))
        {
            TempData["Error"] = "Nenhuma empresa selecionada. Selecione uma empresa antes de importar.";
        }
        return View();
    }

    [HttpGet]
    public IActionResult DownloadTemplate(string? layout = "Layout2")
    {
        var bytes = ImportTemplateBuilder.BuildLayout2Template();
        const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(bytes, contentType, "modelo-importacao-layout2.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var companyDb = _companyContext.CompanyDb;

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Selecione um arquivo para importar.";
            return RedirectToAction(nameof(Index));
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) ||
            !_processingOptions.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            TempData["Error"] = $"Extensao '{extension}' nao permitida. Use: {string.Join(", ", _processingOptions.AllowedExtensions)}";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > _processingOptions.MaxFileSizeBytes)
        {
            var maxMb = _processingOptions.MaxFileSizeBytes / (1024.0 * 1024.0);
            TempData["Error"] = $"Arquivo excede o tamanho maximo de {maxMb:F1} MB.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            TempData["Error"] = "Selecione uma empresa antes de importar.";
            return RedirectToAction("Index", "Company");
        }

        try
        {
            ImportFileContext context;
            await using (var stream = file.OpenReadStream())
            {
                context = await _fileReader.ReadAsync(stream, file.FileName, cancellationToken);
            }

            var result = await _importService.PreviewAsync(context, cancellationToken);

            if (result.ImportFileId == 0)
            {
                TempData["Error"] = result.Errors.FirstOrDefault() ?? "Nao foi possivel processar o arquivo.";
                return RedirectToAction(nameof(Index));
            }

            TempData["CorrelationId"] = result.CorrelationId;
            return RedirectToAction(nameof(Preview), new { id = result.ImportFileId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Operational error during preview of {FileName} for {Company}.", file.FileName, companyDb);
            TempData["Error"] = $"Erro ao processar arquivo: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during preview of {FileName} for {Company}.", file.FileName, companyDb);
            // Don't leak internal details to end users
            TempData["Error"] = "Erro inesperado ao processar o arquivo. Verifique os logs de sistema.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Preview(long id, CancellationToken cancellationToken)
    {
        var importFile = await _dbContext.ImportFiles
            .AsNoTracking()
            .Include(f => f.Lines)
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (importFile == null)
        {
            TempData["Error"] = "Arquivo de importacao nao encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var groups = importFile.Lines
            .GroupBy(l => l.Reference ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new ImportPreviewGroup
            {
                Reference = g.Key,
                LineCount = g.Count(),
                TotalCredit = g.Sum(l => l.CreditAmount ?? 0m),
                TotalDebit = g.Sum(l => l.DebitAmount ?? 0m),
                Lines = g.OrderBy(l => l.Id).ToList()
            })
            .ToList();

        var model = new ImportPreviewViewModel
        {
            ImportFileId = importFile.Id,
            FileName = importFile.OriginalFileName,
            LayoutDetected = importFile.LayoutDetected,
            TotalLines = importFile.TotalLines,
            ValidLines = importFile.ValidLines,
            InvalidLines = importFile.InvalidLines,
            DuplicatedLines = importFile.DuplicatedLines,
            Groups = groups,
            Status = importFile.Status,
            CorrelationId = importFile.CorrelationId,
            Errors = importFile.Lines
                .Where(l => !string.IsNullOrWhiteSpace(l.ValidationMessage))
                .Select(l => l.ValidationMessage!)
                .Distinct()
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(long id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _importService.ConfirmAsync(id, cancellationToken);

            if (result.IsAsync)
            {
                TempData["Success"] = "Importacao enviada para processamento assincrono.";
                TempData["CorrelationId"] = result.CorrelationId;
                return RedirectToAction(nameof(Preview), new { id });
            }

            var sync = result.SynchronousResult;
            if (sync != null && sync.SapErrors == 0 && sync.Imported > 0)
                TempData["Success"] = $"Importacao concluida: {sync.Imported} linhas importadas.";
            else if (sync != null && sync.Imported > 0)
                TempData["Success"] = $"Importacao parcial: {sync.Imported} OK, {sync.SapErrors} com erro SAP.";
            else
                TempData["Error"] = $"Nenhuma linha importada. Verifique o preview.";

            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to confirm import {FileId}.", id);
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Preview), new { id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprocess(long id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _importService.ReprocessAsync(id, cancellationToken);
            TempData["Success"] = result.IsAsync
                ? "Reprocessamento enviado para fila."
                : "Reprocessamento concluido.";
            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Preview), new { id });
        }
    }
}
