using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
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
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Selecione um arquivo para importar.";
            return RedirectToAction(nameof(Index));
        }

        var allowedExtensions = new[] { ".csv", ".txt", ".xlsx" };
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
        {
            TempData["Error"] = "Extensao de arquivo nao permitida. Use CSV, TXT ou XLSX.";
            return RedirectToAction(nameof(Index));
        }

        const long maxFileSize = 10 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            TempData["Error"] = "Arquivo excede o tamanho maximo de 10 MB.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(_companyContext.CompanyDb))
        {
            TempData["Error"] = "Selecione uma empresa antes de importar.";
            return RedirectToAction("Index", "Company");
        }

        try
        {
            _logger.LogInformation(
                "Iniciando preview do arquivo '{FileName}' ({Size} bytes) para company '{Company}'.",
                file.FileName, file.Length, _companyContext.CompanyDb);

            var context = await ReadFileAsync(file, cancellationToken);
            var result = await _importService.PreviewAsync(context, cancellationToken);

            if (result.ImportFileId == 0)
            {
                // PreviewAsync returned without persisting (e.g. file already imported)
                TempData["Error"] = result.Errors.FirstOrDefault() ?? "Nao foi possivel processar o arquivo.";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation(
                "Preview concluido: arquivo {FileId}, layout {Layout}, {Valid} validas, {Invalid} invalidas, {Dup} duplicadas.",
                result.ImportFileId, result.LayoutDetected, result.ValidLines, result.InvalidLines, result.DuplicatedLines);

            return RedirectToAction(nameof(Preview), new { id = result.ImportFileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro durante preview do arquivo '{FileName}'.", file.FileName);
            TempData["Error"] = $"Erro ao processar arquivo: {ex.Message}";
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
            _logger.LogInformation("Confirmando importacao do arquivo {FileId}.", id);
            var result = await _importService.ProcessAsync(id, cancellationToken);

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

    private static async Task<ImportFileContext> ReadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }

        if (lines.Count == 0)
        {
            return new ImportFileContext { FileName = file.FileName, FileBytes = fileBytes };
        }

        var delimiter = lines[0].Contains(';') ? ';' : ',';
        var headers = lines[0].Split(delimiter).Select(h => h.Trim()).ToArray();
        var rows = new List<ImportRow>();

        foreach (var line in lines.Skip(1))
        {
            var values = line.Split(delimiter);
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < values.Length ? values[i].Trim() : null;
                dict[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            rows.Add(new ImportRow(dict));
        }

        return new ImportFileContext
        {
            FileName = file.FileName,
            FileBytes = fileBytes,
            Headers = headers,
            Rows = rows
        };
    }
}
