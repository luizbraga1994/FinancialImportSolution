using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Sap;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Imports;
using FinancialImport.Shared.Imports;
using FinancialImport.Shared.Logging;
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

    /// <summary>
    /// Account codes in the file that do NOT exist in the SAP chart of accounts.
    /// Empty if the chart of accounts could not be fetched (no SAP session yet).
    /// </summary>
    public IReadOnlyCollection<string> InvalidAccountCodes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// True when we fetched the chart of accounts and validated every code.
    /// Used by the UI to differentiate "accounts OK" from "not checked yet".
    /// </summary>
    public bool AccountsValidated { get; set; }
}

public class ImportPreviewGroup
{
    public string Reference { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public int LineCount { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal TotalDebit { get; set; }
    public List<ImportLine> Lines { get; set; } = new();

    /// <summary>Hash used by the exclude-group endpoint to target this group.</summary>
    public string GroupKeyHash { get; set; } = string.Empty;

    /// <summary>True when every line in this group has been excluded by the operator.</summary>
    public bool IsExcluded { get; set; }

    /// <summary>True when the group has at least one line successfully imported.</summary>
    public bool IsImported { get; set; }
}

[Authorize]
public class ImportController : Controller
{
    private readonly IImportService _importService;
    private readonly IImportFileReader _fileReader;
    private readonly AppDbContext _dbContext;
    private readonly ICompanyContext _companyContext;
    private readonly ImportProcessingOptions _processingOptions;
    private readonly ISapSessionStore _sapSessionStore;
    private readonly ISapChartOfAccountsService _chartOfAccounts;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        IImportService importService,
        IImportFileReader fileReader,
        AppDbContext dbContext,
        ICompanyContext companyContext,
        IOptions<ImportProcessingOptions> processingOptions,
        ISapSessionStore sapSessionStore,
        ISapChartOfAccountsService chartOfAccounts,
        IAuditLogger audit,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _fileReader = fileReader;
        _dbContext = dbContext;
        _companyContext = companyContext;
        _processingOptions = processingOptions.Value;
        _sapSessionStore = sapSessionStore;
        _chartOfAccounts = chartOfAccounts;
        _audit = audit;
        _logger = logger;
    }

    private string CurrentUser => User.FindFirst("login")?.Value ?? "desconhecido";
    private string CurrentCompany => _companyContext.CompanyDb ?? "-";

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
            .GroupBy(l => l.GroupKeyHash ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(g => g.First().PostingDate)
            .ThenBy(g => g.First().Reference ?? string.Empty)
            .Select(g =>
            {
                var first = g.First();
                return new ImportPreviewGroup
                {
                    Reference = first.Reference ?? string.Empty,
                    PostingDate = first.PostingDate,
                    LineCount = g.Count(),
                    TotalCredit = g.Sum(l => l.CreditAmount ?? 0m),
                    TotalDebit = g.Sum(l => l.DebitAmount ?? 0m),
                    GroupKeyHash = g.Key,
                    IsExcluded = g.All(l => l.Status == ImportLineStatus.Excluded),
                    IsImported = g.Any(l => l.Status == ImportLineStatus.Imported),
                    Lines = g.OrderBy(l => l.Id).ToList()
                };
            })
            .ToList();

        // Best-effort account validation against the SAP chart of accounts.
        // Only runs when a SAP session already exists for this user + company,
        // so the preview page loads fast for users that haven't logged in to
        // SAP yet. Invalid codes shown here give the user a chance to fix the
        // file before confirming (avoiding a run that fails group-by-group).
        var invalidAccounts = new List<string>();
        var accountsValidated = false;
        try
        {
            var userIdClaim = User.FindFirst("user_id")?.Value;
            if (long.TryParse(userIdClaim, out var userId))
            {
                var session = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
                if (session != null && session.CompanyDb.Equals(importFile.CompanyDb, StringComparison.OrdinalIgnoreCase))
                {
                    var accounts = await _chartOfAccounts.GetAccountCodesAsync(session, cancellationToken);
                    if (accounts.Count > 0)
                    {
                        var codesInFile = importFile.Lines
                            .SelectMany(l => new[] { l.AccountCode, l.ContraAccountCode })
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        invalidAccounts = codesInFile
                            .Where(c => !accounts.ContainsKey(c))
                            .OrderBy(c => c)
                            .ToList();
                        accountsValidated = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao validar contas contra o plano de contas do SAP no preview (nao-critico).");
        }

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
            InvalidAccountCodes = invalidAccounts,
            AccountsValidated = accountsValidated,
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
        // The dispatch runs synchronously on the HTTP thread and can take
        // several minutes for large files. If we pass the request cancellation
        // token, any browser hiccup / tab close / NIC blip kills the processor
        // mid-batch with a half-dispatched journal. Use a dedicated 15-minute
        // token: it insulates the long-running work and still prevents runaway
        // requests. The user can still cancel via the Cancel button, which
        // sets ImportStatus.Cancelled and the processor checks that between
        // groups.
        using var runToken = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        try
        {
            var result = await _importService.ConfirmAsync(id, runToken.Token);

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
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            _logger.LogError("Confirm de importacao {FileId} excedeu 15 minutos e foi abortado.", id);
            TempData["Error"] = "Processamento excedeu 15 minutos e foi abortado. Use Reprocessar para continuar de onde parou.";
            return RedirectToAction(nameof(Preview), new { id });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        var importFile = await _dbContext.ImportFiles
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (importFile == null)
            return NotFound();

        if (importFile.Status != ImportStatus.Processing)
            return BadRequest("Importacao nao esta em processamento.");

        importFile.Status = ImportStatus.Cancelled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Importacao {Id} cancelada pelo usuario.", id);

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Warning,
            Category = LogCategories.Audit,
            Source = nameof(ImportController),
            Operation = "CancelarProcessamento",
            Message = $"Processamento da importacao {id} cancelado por '{CurrentUser}'.",
            ImportFileId = id,
            CompanyDb = CurrentCompany
        }, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// Real-time progress endpoint polled by the loading overlay during
    /// the SAP dispatch. Returns how many groups have been sent, how many
    /// failed, the file status, and (best-effort) the last group the
    /// processor touched so the user sees movement and does not assume
    /// the screen is frozen.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Progress(long id, CancellationToken cancellationToken)
    {
        var file = await _dbContext.ImportFiles
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new { f.Id, f.Status, f.TotalLines, f.ValidLines, f.ImportedLines, f.LinesWithError, f.OriginalFileName, f.ProcessingStartedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        if (file == null)
            return NotFound();

        var dispatches = await _dbContext.JournalEntryDispatches
            .AsNoTracking()
            .Where(d => d.ImportFileId == id)
            .Select(d => new { d.Status, d.GroupKey, d.LastAttemptAtUtc, d.LastError })
            .ToListAsync(cancellationToken);

        var dispatched = dispatches.Count(d => d.Status == JournalDispatchStatus.Dispatched);
        var failed = dispatches.Count(d => d.Status == JournalDispatchStatus.Failed);
        var inFlight = dispatches.Count(d => d.Status == JournalDispatchStatus.InFlight);
        var totalGroups = dispatches.Count;

        var latest = dispatches
            .OrderByDescending(d => d.LastAttemptAtUtc)
            .FirstOrDefault();

        var elapsed = file.ProcessingStartedAtUtc.HasValue
            ? (long)(DateTime.Now - file.ProcessingStartedAtUtc.Value).TotalSeconds
            : 0;

        return Json(new
        {
            status = file.Status.ToString(),
            isFinished = file.Status != ImportStatus.Processing,
            fileName = file.OriginalFileName,
            totalLines = file.TotalLines,
            validLines = file.ValidLines,
            importedLines = file.ImportedLines,
            linesWithError = file.LinesWithError,
            totalGroups,
            dispatched,
            failed,
            inFlight,
            percent = totalGroups > 0 ? (int)Math.Round((dispatched + failed) * 100.0 / totalGroups) : 0,
            currentGroup = latest?.GroupKey,
            lastError = latest?.Status == JournalDispatchStatus.Failed ? latest.LastError : null,
            elapsedSeconds = elapsed
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprocess(long id, CancellationToken cancellationToken)
    {
        // See Confirm for why we isolate the processor from the request token.
        using var runToken = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        try
        {
            var result = await _importService.ReprocessAsync(id, runToken.Token);
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
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            _logger.LogError("Reprocess de importacao {FileId} excedeu 15 minutos e foi abortado.", id);
            TempData["Error"] = "Reprocessamento excedeu 15 minutos e foi abortado. Clique em Reprocessar novamente para continuar.";
            return RedirectToAction(nameof(Preview), new { id });
        }
    }

    /// <summary>
    /// Marks all lines of a given GroupKeyHash as Excluded so they are skipped
    /// when the import is confirmed. Useful when the operator reviews the
    /// preview and wants to cherry-pick which journal entries to send to SAP.
    /// Only lines in Valid or SapError status can be excluded — Imported and
    /// Invalid/Duplicated lines are left untouched.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcludeGroup(long id, string groupKeyHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupKeyHash))
        {
            TempData["Error"] = "Identificador do grupo nao informado.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        var importFile = await _dbContext.ImportFiles
            .Include(f => f.Lines)
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (importFile == null)
        {
            TempData["Error"] = "Importacao nao encontrada.";
            return RedirectToAction(nameof(Index));
        }

        if (importFile.Status == ImportStatus.Processing)
        {
            TempData["Error"] = "Nao e possivel excluir grupos enquanto a importacao esta em processamento.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        var affected = importFile.Lines
            .Where(l => l.GroupKeyHash == groupKeyHash
                     && (l.Status == ImportLineStatus.Valid || l.Status == ImportLineStatus.SapError))
            .ToList();

        if (affected.Count == 0)
        {
            TempData["Error"] = "Nenhuma linha elegivel para exclusao neste grupo.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        var reference = affected[0].Reference ?? "(sem referencia)";
        foreach (var line in affected)
        {
            line.Status = ImportLineStatus.Excluded;
            line.ValidationMessage = "Excluido pelo operador antes do envio ao SAP.";
        }

        // Recount so the header counters stay accurate
        importFile.ValidLines = importFile.Lines.Count(l => l.Status == ImportLineStatus.Valid);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Grupo '{GroupKey}' ({Count} linha(s)) excluido do arquivo {FileId} por '{User}'.",
            reference, affected.Count, id, CurrentUser);

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Warning,
            Category = LogCategories.Audit,
            Source = nameof(ImportController),
            Operation = "ExcluirGrupo",
            Message = $"Grupo '{reference}' ({affected.Count} linha(s)) excluido do arquivo {id} por '{CurrentUser}'.",
            Details = $"Referencia: {reference}\nLinhas excluidas: {affected.Count}\nContas: {string.Join(", ", affected.Select(l => l.AccountCode).Distinct())}\nEmpresa: {CurrentCompany}",
            ImportFileId = id,
            CompanyDb = CurrentCompany,
            BusinessKey = groupKeyHash
        }, cancellationToken);

        TempData["Success"] = $"Grupo '{reference}' excluido ({affected.Count} linha(s)). Essas linhas nao serao enviadas ao SAP.";
        return RedirectToAction(nameof(Preview), new { id });
    }

    /// <summary>
    /// Excludes a single import line (one Excel row) from the journal.
    /// The remaining lines in the same group still go to SAP. Excluding
    /// a debit without excluding the matching credit (or vice-versa)
    /// leaves the group unbalanced — the ImportProcessor will log a
    /// warning but still attempt the dispatch.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcludeLine(long id, long lineId, CancellationToken cancellationToken)
    {
        var importFile = await _dbContext.ImportFiles
            .Include(f => f.Lines)
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (importFile == null)
        {
            TempData["Error"] = "Importacao nao encontrada.";
            return RedirectToAction(nameof(Index));
        }

        if (importFile.Status == ImportStatus.Processing)
        {
            TempData["Error"] = "Nao e possivel excluir linhas enquanto a importacao esta em processamento.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        var line = importFile.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null)
        {
            TempData["Error"] = "Linha nao encontrada.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        if (line.Status != ImportLineStatus.Valid && line.Status != ImportLineStatus.SapError)
        {
            TempData["Error"] = $"Linha com status '{line.Status}' nao pode ser excluida.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        line.Status = ImportLineStatus.Excluded;
        line.ValidationMessage = "Excluida pelo operador antes do envio ao SAP.";

        importFile.ValidLines = importFile.Lines.Count(l => l.Status == ImportLineStatus.Valid);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Linha {LineId} (grupo {GroupKey}) excluida do arquivo {FileId} por '{User}'.",
            lineId, line.Reference, id, CurrentUser);

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Warning,
            Category = LogCategories.Audit,
            Source = nameof(ImportController),
            Operation = "ExcluirLinha",
            Message = $"Linha {lineId} excluida do grupo '{line.Reference}' (arquivo {id}) por '{CurrentUser}'. Conta: {line.AccountCode}, D:{line.DebitAmount ?? 0:N2} / C:{line.CreditAmount ?? 0:N2}.",
            ImportFileId = id,
            ImportLineId = lineId,
            CompanyDb = CurrentCompany
        }, cancellationToken);

        TempData["Success"] = $"Linha excluida com sucesso. Atencao: se o grupo '{line.Reference}' ficar desbalanceado, o SAP pode rejeitar o lancamento inteiro.";
        return RedirectToAction(nameof(Preview), new { id });
    }
}
