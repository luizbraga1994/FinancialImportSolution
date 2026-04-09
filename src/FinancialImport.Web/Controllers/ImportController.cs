using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly AppDbContext _dbContext;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        IImportService importService,
        AppDbContext dbContext,
        ICompanyContext companyContext,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _dbContext = dbContext;
        _companyContext = companyContext;
        _logger = logger;
    }

    public IActionResult Index()
    {
        var companyDb = _companyContext.CompanyDb;
        _logger.LogInformation("Acessando página de importação. CompanyDb: '{CompanyDb}', Usuario: '{User}'",
            companyDb, User.Identity?.Name ?? "desconhecido");

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            TempData["Error"] = "Nenhuma empresa selecionada. Por favor, selecione uma empresa no menu Empresas antes de importar.";
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
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var companyDb = _companyContext.CompanyDb;

        _logger.LogInformation("=== INICIO UPLOAD ===");
        _logger.LogInformation("CompanyDb atual: '{CompanyDb}'", companyDb);
        _logger.LogInformation("Usuario: '{User}'", User.Identity?.Name ?? "desconhecido");
        _logger.LogInformation("Claims do usuario: {Claims}", string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload falhou: Nenhum arquivo selecionado.");
            TempData["Error"] = "Selecione um arquivo para importar.";
            return RedirectToAction(nameof(Index));
        }

        var allowedExtensions = new[] { ".csv", ".txt", ".xlsx" };
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
        {
            _logger.LogWarning("Upload falhou: Extensao '{Extension}' nao permitida para arquivo '{FileName}'.", extension, file.FileName);
            TempData["Error"] = "Extensao de arquivo nao permitida. Use CSV, TXT ou XLSX.";
            return RedirectToAction(nameof(Index));
        }

        const long maxFileSize = 10 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            _logger.LogWarning("Upload falhou: Arquivo '{FileName}' excede o tamanho maximo de 10 MB (tamanho: {Size} bytes).", file.FileName, file.Length);
            TempData["Error"] = "Arquivo excede o tamanho maximo de 10 MB.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(companyDb))
        {
            _logger.LogWarning("Upload falhou: Nenhuma empresa selecionada. Usuario deve selecionar uma empresa primeiro.");
            TempData["Error"] = "Selecione uma empresa antes de importar. Acesse o menu Empresas e escolha uma base.";
            return RedirectToAction("Index", "Company");
        }

        try
        {
            _logger.LogInformation("Iniciando preview do arquivo '{FileName}' ({Size} bytes) para company '{Company}'.",
                file.FileName, file.Length, companyDb);

            var context = await ImportFileReader.ReadAsync(file, cancellationToken);
            _logger.LogInformation("Arquivo lido com sucesso. Headers detectados: {Headers}",
                string.Join(", ", context.Headers.Take(10)));

            var result = await _importService.PreviewAsync(context, cancellationToken);

            if (result.ImportFileId == 0)
            {
                var errorMsg = result.Errors.FirstOrDefault() ?? "Nao foi possivel processar o arquivo.";
                _logger.LogWarning("Preview falhou para arquivo '{FileName}': {Error}. CompanyDb: '{Company}'",
                    file.FileName, errorMsg, companyDb);
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation(
                "Preview concluido com sucesso: arquivo {FileId}, layout {Layout}, Total: {Total}, Validas: {Valid}, Invalidas: {Invalid}, Duplicadas: {Dup}.",
                result.ImportFileId, result.LayoutDetected, result.TotalLines, result.ValidLines, result.InvalidLines, result.DuplicatedLines);

            return RedirectToAction(nameof(Preview), new { id = result.ImportFileId });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Erro de operacao invalida durante preview do arquivo '{FileName}' para company '{Company}'.",
                file.FileName, companyDb);
            TempData["Error"] = $"Erro ao processar arquivo: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado durante preview do arquivo '{FileName}' para company '{Company}'.",
                file.FileName, companyDb);
            TempData["Error"] = $"Erro ao processar arquivo: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Preview(long id, CancellationToken cancellationToken)
    {
        var companyDb = _companyContext.CompanyDb;
        _logger.LogInformation("Acessando preview do arquivo {FileId}. CompanyDb atual: '{CompanyDb}'", id, companyDb);

        var importFile = await _dbContext.ImportFiles
            .AsNoTracking()
            .Include(f => f.Lines)
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (importFile == null)
        {
            _logger.LogWarning("Arquivo de importacao {FileId} nao encontrado.", id);
            TempData["Error"] = "Arquivo de importacao nao encontrado.";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Preview carregado: arquivo {FileId}, Company: '{CompanyDb}', Status: {Status}, Linhas: {TotalLines}",
            importFile.Id, importFile.CompanyDb, importFile.Status, importFile.TotalLines);

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
        _logger.LogInformation("Confirmando importacao do arquivo {FileId}.", id);

        try
        {
            var result = await _importService.ProcessAsync(id, cancellationToken);

            _logger.LogInformation(
                "Resultado da confirmacao do arquivo {FileId}: Importados: {Imported}, Duplicados: {Duplicated}, Invalidos: {Invalid}, Erros SAP: {SapErrors}",
                id, result.Imported, result.Duplicated, result.Invalid, result.SapErrors);

            if (result.SapErrors == 0 && result.Imported > 0)
            {
                TempData["Success"] = $"Importacao concluida com sucesso: {result.Imported} linhas importadas.";
            }
            else if (result.Imported > 0)
            {
                TempData["Success"] = $"Importacao parcial: {result.Imported} importadas, {result.SapErrors} com erro SAP, {result.Invalid} invalidas.";
            }
            else
            {
                TempData["Error"] = $"Nenhuma linha importada. {result.SapErrors} erros SAP, {result.Invalid} invalidas, {result.Duplicated} duplicadas.";
            }

            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Falha ao confirmar importacao {FileId}.", id);
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao confirmar importacao {FileId}.", id);
            TempData["Error"] = $"Erro ao processar importacao: {ex.Message}";
            return RedirectToAction(nameof(Preview), new { id });
        }
    }
}