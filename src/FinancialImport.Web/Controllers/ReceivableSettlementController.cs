using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Settlements;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Settlements;
using FinancialImport.Shared.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialImport.Web.Controllers;

public class SettlementPreviewViewModel
{
    public long SettlementFileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int ValidLines { get; set; }
    public int InvalidLines { get; set; }
    public int DuplicatedLines { get; set; }
    public int SettledLines { get; set; }
    public int SapErrorLines { get; set; }
    public SettlementStatus Status { get; set; }
    public string? CorrelationId { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
    public List<ReceivableSettlementLine> Lines { get; set; } = new();
}

[Authorize]
public class ReceivableSettlementController : Controller
{
    private readonly ISettlementService _settlementService;
    private readonly IImportFileReader _fileReader;
    private readonly AppDbContext _dbContext;
    private readonly ICompanyContext _companyContext;
    private readonly ImportProcessingOptions _processingOptions;
    private readonly ILogger<ReceivableSettlementController> _logger;

    public ReceivableSettlementController(
        ISettlementService settlementService,
        IImportFileReader fileReader,
        AppDbContext dbContext,
        ICompanyContext companyContext,
        IOptions<ImportProcessingOptions> processingOptions,
        ILogger<ReceivableSettlementController> logger)
    {
        _settlementService = settlementService;
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
            TempData["Error"] = "Nenhuma empresa selecionada. Selecione uma empresa antes de baixar notas.";
        }
        return View();
    }

    public IActionResult Instructions()
    {
        return View();
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var bytes = SettlementTemplateBuilder.Build();
        const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(bytes, contentType, "modelo-baixa-notas-saida.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var companyDb = _companyContext.CompanyDb;

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Selecione um arquivo de baixa.";
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
            TempData["Error"] = "Selecione uma empresa antes de baixar notas.";
            return RedirectToAction("Index", "Company");
        }

        try
        {
            SettlementFileContext context;
            await using (var stream = file.OpenReadStream())
            {
                context = await BuildContextAsync(stream, file.FileName, allowDuplicate: false, cancellationToken);
            }

            var result = await _settlementService.PreviewAsync(context, cancellationToken);

            if (result.IsDuplicateFile)
            {
                var key = Guid.NewGuid().ToString("N");
                var tmpPath = Path.Combine(Path.GetTempPath(), $"baixa_{key}.tmp");
                await System.IO.File.WriteAllBytesAsync(tmpPath, context.FileBytes, cancellationToken);

                TempData["DuplicateKey"] = key;
                TempData["DuplicateFileName"] = file.FileName;
                TempData["DuplicateStatus"] = result.ExistingFileStatus;
                return RedirectToAction(nameof(Index));
            }

            if (result.SettlementFileId == 0)
            {
                TempData["Error"] = result.Errors.FirstOrDefault() ?? "Nao foi possivel processar o arquivo.";
                return RedirectToAction(nameof(Index));
            }

            TempData["CorrelationId"] = result.CorrelationId;
            return RedirectToAction(nameof(Preview), new { id = result.SettlementFileId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Operational error during settlement preview of {FileName}.", file.FileName);
            TempData["Error"] = $"Erro ao processar arquivo: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during settlement preview of {FileName}.", file.FileName);
            TempData["Error"] = "Erro inesperado ao processar o arquivo. Verifique os logs de sistema.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDuplicate(string key, string fileName, CancellationToken cancellationToken)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"baixa_{key}.tmp");
        if (!System.IO.File.Exists(tmpPath))
        {
            TempData["Error"] = "Sessao expirada. Faca o upload do arquivo novamente.";
            return RedirectToAction(nameof(Index));
        }

        byte[] fileBytes;
        try
        {
            fileBytes = await System.IO.File.ReadAllBytesAsync(tmpPath, cancellationToken);
            System.IO.File.Delete(tmpPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read pending duplicate settlement temp file {Key}.", key);
            TempData["Error"] = "Erro ao recuperar arquivo temporario. Faca o upload novamente.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            using var ms = new MemoryStream(fileBytes);
            var context = await BuildContextAsync(ms, fileName, allowDuplicate: true, cancellationToken);

            var result = await _settlementService.PreviewAsync(context, cancellationToken);
            if (result.SettlementFileId == 0)
            {
                TempData["Error"] = result.Errors.FirstOrDefault() ?? "Nao foi possivel processar o arquivo.";
                return RedirectToAction(nameof(Index));
            }

            TempData["CorrelationId"] = result.CorrelationId;
            return RedirectToAction(nameof(Preview), new { id = result.SettlementFileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error confirming duplicate settlement upload for {FileName}.", fileName);
            TempData["Error"] = "Erro inesperado ao processar o arquivo. Verifique os logs de sistema.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Preview(long id, CancellationToken cancellationToken)
    {
        var file = await _dbContext.ReceivableSettlementFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (file == null)
        {
            TempData["Error"] = "Arquivo de baixa nao encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var lines = await _dbContext.ReceivableSettlementLines
            .AsNoTracking()
            .Where(l => l.SettlementFileId == id)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var vm = new SettlementPreviewViewModel
        {
            SettlementFileId = file.Id,
            FileName = file.OriginalFileName,
            TotalLines = file.TotalLines,
            ValidLines = lines.Count(l => l.Status == SettlementLineStatus.Valid),
            InvalidLines = lines.Count(l => l.Status is SettlementLineStatus.Invalid or SettlementLineStatus.InvoiceNotFound),
            DuplicatedLines = lines.Count(l => l.Status == SettlementLineStatus.Duplicated),
            SettledLines = lines.Count(l => l.Status == SettlementLineStatus.Settled),
            SapErrorLines = lines.Count(l => l.Status == SettlementLineStatus.SapError),
            Status = file.Status,
            CorrelationId = file.CorrelationId,
            Lines = lines
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(long id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _settlementService.ConfirmAsync(id, cancellationToken);
            var sync = result.SynchronousResult;
            if (sync != null)
            {
                TempData["Success"] = $"Baixa processada — {sync.Settled} nota(s) baixada(s), {sync.SapErrors} com erro.";
            }
            else
            {
                TempData["Success"] = "Baixa enviada para processamento.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming settlement {Id}.", id);
            TempData["Error"] = $"Erro ao confirmar a baixa: {ex.Message}";
        }

        return RedirectToAction(nameof(Preview), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprocess(long id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _settlementService.ReprocessAsync(id, cancellationToken);
            var sync = result.SynchronousResult;
            TempData["Success"] = sync != null
                ? $"Reprocessamento concluido — {sync.Settled} baixada(s), {sync.SapErrors} com erro."
                : "Reprocessamento enviado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reprocessing settlement {Id}.", id);
            TempData["Error"] = $"Erro ao reprocessar: {ex.Message}";
        }

        return RedirectToAction(nameof(Preview), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        var file = await _dbContext.ReceivableSettlementFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file != null && file.Status == SettlementStatus.Processing)
        {
            file.Status = SettlementStatus.Cancelled;
            await _dbContext.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Baixa cancelada.";
        }
        return RedirectToAction(nameof(Preview), new { id });
    }

    private async Task<SettlementFileContext> BuildContextAsync(
        Stream stream, string fileName, bool allowDuplicate, CancellationToken cancellationToken)
    {
        // Reuse the import file reader (CSV/XLSX) to split into headers + rows,
        // then adapt it to the settlement context.
        var imported = await _fileReader.ReadAsync(stream, fileName, cancellationToken);
        return new SettlementFileContext
        {
            FileName = imported.FileName,
            FileBytes = imported.FileBytes,
            Headers = imported.Headers,
            Rows = imported.Rows,
            AllowDuplicate = allowDuplicate
        };
    }
}
