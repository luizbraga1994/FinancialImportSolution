using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Idempotent SAP dispatcher. Reads the validated ImportFile, groups
/// the lines, and creates a <see cref="JournalEntryDispatch"/> row for
/// each group BEFORE calling SAP. The unique index on
/// (CompanyDb, GroupKeyHash) means that a second attempt for the same
/// group is rejected by the database — the SAP ledger cannot receive a
/// duplicate entry even in the face of crashes, timeouts or retries.
/// </summary>
public sealed class ImportProcessor : IImportProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly IImportRepository _repository;
    private readonly ISapSessionStore _sapSessionStore;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly ISapJournalEntryService _sapService;
    private readonly ISapChartOfAccountsService _chartOfAccounts;
    private readonly JournalEntryBuilder _entryBuilder;
    private readonly IUserContext _userContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ImportProcessor> _logger;

    public ImportProcessor(
        AppDbContext dbContext,
        IImportRepository repository,
        ISapSessionStore sapSessionStore,
        ISapCompanySessionService sapSessionService,
        ISapJournalEntryService sapService,
        ISapChartOfAccountsService chartOfAccounts,
        JournalEntryBuilder entryBuilder,
        IUserContext userContext,
        IEventPublisher eventPublisher,
        ISystemSettingsService settings,
        IAuditLogger audit,
        ILogger<ImportProcessor> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sapSessionStore = sapSessionStore;
        _sapSessionService = sapSessionService;
        _sapService = sapService;
        _chartOfAccounts = chartOfAccounts;
        _entryBuilder = entryBuilder;
        _userContext = userContext;
        _eventPublisher = eventPublisher;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ImportProcessResult> ExecuteAsync(
        long importFileId,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.Now;
        var userId = _userContext.UserId
            ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var importFile = await _repository.GetImportFileWithLinesAsync(importFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Arquivo de importacao {importFileId} nao encontrado.");

        // Try to get an existing active session; if missing or expired,
        // auto-login using the SAP credentials from DB settings — same
        // pattern as PortalSapB1.ServiceLayerAdapter.GetSessionAsync().
        var sapSession = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);

        if (sapSession == null || !sapSession.CompanyDb.Equals(importFile.CompanyDb, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Sessao SAP ausente ou para company diferente. Tentando login automatico em '{CompanyDb}'...",
                importFile.CompanyDb);

            var loginResult = await _sapSessionService.SignInCompanyAsync(
                importFile.CompanyDb,
                _settings.Get("Sap:UserName") ?? "",
                _settings.Get("Sap:Password") ?? "",
                cancellationToken);

            if (!loginResult.Success)
                throw new InvalidOperationException(
                    $"Nao foi possivel conectar ao SAP para '{importFile.CompanyDb}': {loginResult.ErrorMessage}");

            sapSession = loginResult.Session!;
        }

        importFile.Status = ImportStatus.Processing;
        importFile.ProcessingStartedAtUtc = start;
        // Clear the previous run's completion timestamp so the /Progress
        // endpoint does not mistake a stale 'finished' state for the current
        // run. Also clears error/imported counters so the UI shows fresh data.
        importFile.ProcessingCompletedAtUtc = null;
        await _repository.UpdateImportFileAsync(importFile, cancellationToken);

        // Include lines that previously errored on SAP so Reprocess picks them up.
        // Groups that were dispatched successfully are skipped later via the
        // JournalEntryDispatch idempotency check, so there is no risk of double-
        // posting. Duplicated/Invalid lines never reach SAP.
        var validLines = importFile.Lines
            .Where(l => l.Status == ImportLineStatus.Valid
                     || l.Status == ImportLineStatus.SapError)
            .ToList();

        _logger.LogInformation(
            "Processing file={FileId} lines={Count} correlation={CorrelationId}",
            importFileId, validLines.Count, importFile.CorrelationId);

        var branchMappings = await _dbContext.BranchMappings
            .Where(b => b.CompanyDb == importFile.CompanyDb && b.IsActive)
            .ToListAsync(cancellationToken);

        // Fetch chart of accounts to auto-resolve partial codes (e.g. "1612001100002" → "1612001100002-0")
        IReadOnlyDictionary<string, string> accountCodes;
        try
        {
            accountCodes = await _chartOfAccounts.GetAccountCodesAsync(sapSession, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ChartOfAccounts — account codes will not be auto-resolved.");
            accountCodes = new Dictionary<string, string>();
        }

        // Group by GroupKeyHash precomputed at preview time. This
        // means a reprocess hits the same groups, so the unique index
        // on LancamentoSapDispatch stops us from re-sending.
        var groups = validLines
            .Where(l => !string.IsNullOrEmpty(l.GroupKeyHash))
            .GroupBy(l => l.GroupKeyHash!, StringComparer.Ordinal)
            .ToList();

        int imported = 0;
        int sapErrors = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if the user requested cancellation via the UI
            var currentStatus = await _dbContext.ImportFiles
                .AsNoTracking()
                .Where(f => f.Id == importFileId)
                .Select(f => f.Status)
                .FirstAsync(cancellationToken);

            if (currentStatus == ImportStatus.Cancelled)
            {
                _logger.LogInformation("Import {FileId} cancelled by user after {Imported} groups.", importFileId, imported);
                break;
            }

            var groupLines = group.OrderBy(l => l.Id).ToList();

            // Check the dispatch table — if this group already has a
            // Dispatched row, we re-attach the SapDocEntry to the lines
            // (in case they were marked SapError before) and skip SAP.
            var existingDispatch = await _dbContext.JournalEntryDispatches
                .FirstOrDefaultAsync(d =>
                    d.CompanyDb == importFile.CompanyDb && d.GroupKeyHash == group.Key, cancellationToken);

            if (existingDispatch is { Status: JournalDispatchStatus.Dispatched })
            {
                foreach (var line in groupLines)
                {
                    line.Status = ImportLineStatus.Imported;
                    line.SapDocEntry = existingDispatch.SapDocEntry;
                    line.SapReturnMessage = "Already dispatched (idempotent).";
                    imported++;
                }
                continue;
            }

            var dispatch = existingDispatch ?? new JournalEntryDispatch
            {
                ImportFileId = importFile.Id,
                CompanyDb = importFile.CompanyDb,
                GroupKeyHash = group.Key,
                GroupKey = BuildGroupKeyLabel(groupLines[0]),
                Status = JournalDispatchStatus.InFlight,
                AttemptCount = 1,
                CreatedAtUtc = DateTime.Now,
                LastAttemptAtUtc = DateTime.Now,
                CorrelationId = importFile.CorrelationId
            };

            if (existingDispatch != null)
            {
                dispatch.AttemptCount += 1;
                dispatch.LastAttemptAtUtc = DateTime.Now;
                dispatch.Status = JournalDispatchStatus.InFlight;
            }
            else
            {
                await _dbContext.JournalEntryDispatches.AddAsync(dispatch, cancellationToken);
            }

            try
            {
                // Persist the InFlight row FIRST. The unique index is
                // the safety net against concurrent dispatchers.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Dispatch row already exists for group {GroupKey} — another worker is handling it.",
                    group.Key);
                continue;
            }

            int? bplId = ResolveBplId(groupLines[0], importFile, branchMappings);
            var build = _entryBuilder.Build(dispatch.GroupKey, dispatch.GroupKeyHash, groupLines, bplId,
                accountCodes.Count > 0 ? accountCodes : null);
            if (!build.IsBalanced)
            {
                _logger.LogWarning(
                    "Journal entry not balanced file={FileId} group={GroupKey} debit={Debit} credit={Credit}",
                    importFile.Id, dispatch.GroupKey, build.TotalDebit, build.TotalCredit);
            }

            // Auto-resolve partial G/L account codes to full SAP codes (with check digit).
            // Business Partner lines (ShortName) are not in the chart of accounts and are left as-is.
            if (accountCodes.Count > 0)
            {
                foreach (var line in build.Payload.JournalEntryLines)
                {
                    if (!string.IsNullOrWhiteSpace(line.AccountCode))
                        line.AccountCode = _chartOfAccounts.ResolveAccountCode(line.AccountCode, accountCodes);
                }
            }

            SapResult sapResult;
            Exception? sapException = null;
            try
            {
                sapResult = await _sapService.CreateJournalEntryAsync(sapSession, build.Payload, cancellationToken);

                // Session expired mid-batch — re-login once and retry
                if (sapResult.IsSessionExpired)
                {
                    _logger.LogInformation("SAP session expired mid-batch. Re-authenticating for '{CompanyDb}'...", importFile.CompanyDb);
                    var relogin = await _sapSessionService.SignInCompanyAsync(
                        importFile.CompanyDb,
                        _settings.Get("Sap:UserName") ?? "",
                        _settings.Get("Sap:Password") ?? "",
                        cancellationToken);

                    if (relogin.Success)
                    {
                        sapSession = relogin.Session!;
                        sapResult = await _sapService.CreateJournalEntryAsync(sapSession, build.Payload, cancellationToken);
                    }
                    else
                    {
                        _logger.LogError("Re-authentication to SAP failed for '{CompanyDb}': {Error}",
                            importFile.CompanyDb, relogin.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                sapException = ex;
                _logger.LogError(ex,
                    "Exception while dispatching group {GroupKey} for file {FileId} (company {CompanyDb}).",
                    dispatch.GroupKey, importFile.Id, importFile.CompanyDb);
                sapResult = SapResult.Fail($"Erro de comunicacao: {ex.Message}");
            }

            if (sapResult.Success)
            {
                var docEntry = ExtractDocEntry(sapResult.RawResponse);
                dispatch.Status = JournalDispatchStatus.Dispatched;
                dispatch.DispatchedAtUtc = DateTime.Now;
                dispatch.SapDocEntry = docEntry;
                dispatch.SapResponseSummary = Truncate(sapResult.RawResponse, 2000);
                dispatch.LastError = null;

                foreach (var line in groupLines)
                {
                    line.Status = ImportLineStatus.Imported;
                    line.SapReturnMessage = "OK";
                    line.SapDocEntry = docEntry;
                    imported++;
                }

                await _eventPublisher.PublishAsync(new SapDispatchSucceededEvent
                {
                    ImportFileId = importFile.Id,
                    CompanyDb = importFile.CompanyDb,
                    GroupKey = dispatch.GroupKey,
                    DocEntry = docEntry,
                    DurationMs = (long)(DateTime.Now - start).TotalMilliseconds
                }, cancellationToken);
            }
            else
            {
                dispatch.Status = JournalDispatchStatus.Failed;
                dispatch.LastError = Truncate(sapResult.ErrorMessage ?? "Unknown SAP error", 2000);
                dispatch.SapResponseSummary = Truncate(sapResult.RawResponse, 2000);

                foreach (var line in groupLines)
                {
                    line.Status = ImportLineStatus.SapError;
                    line.SapReturnMessage = dispatch.LastError;
                    sapErrors++;
                }

                // Per-group audit log with the FULL error details so users can see
                // exactly which group failed and why, without digging through app logs.
                var detailsBuilder = new System.Text.StringBuilder();
                detailsBuilder.AppendLine($"Grupo: {dispatch.GroupKey}");
                detailsBuilder.AppendLine($"Tentativa: {dispatch.AttemptCount}");
                detailsBuilder.AppendLine($"Linhas afetadas: {groupLines.Count}");
                detailsBuilder.AppendLine($"Debito total: {build.TotalDebit:N2}");
                detailsBuilder.AppendLine($"Credito total: {build.TotalCredit:N2}");
                detailsBuilder.AppendLine($"Contas utilizadas: {string.Join(", ", build.Payload.JournalEntryLines.Select(l => l.AccountCode ?? l.ShortName).Distinct())}");
                detailsBuilder.AppendLine();
                detailsBuilder.AppendLine("--- Resposta completa do SAP ---");
                detailsBuilder.AppendLine(sapResult.RawResponse ?? "(sem corpo de resposta)");
                if (sapException != null)
                {
                    detailsBuilder.AppendLine();
                    detailsBuilder.AppendLine("--- Exception capturada ---");
                    detailsBuilder.AppendLine(sapException.ToString());
                }

                await _audit.WriteAsync(new AuditLogEntry
                {
                    Level = LogSeverities.Error,
                    Category = LogCategories.Integration,
                    Source = nameof(ImportProcessor),
                    Operation = "DispatchJournalEntry",
                    Message = $"SAP rejeitou lancamento do grupo '{dispatch.GroupKey}': {dispatch.LastError}",
                    Details = detailsBuilder.ToString(),
                    StackTrace = sapException?.StackTrace,
                    ImportFileId = importFile.Id,
                    CompanyDb = importFile.CompanyDb,
                    CorrelationId = importFile.CorrelationId,
                    BusinessKey = dispatch.GroupKeyHash,
                    StatusAfter = dispatch.Status.ToString()
                }, cancellationToken);

                await _eventPublisher.PublishAsync(new SapDispatchFailedEvent
                {
                    ImportFileId = importFile.Id,
                    CompanyDb = importFile.CompanyDb,
                    GroupKey = dispatch.GroupKey,
                    ErrorMessage = dispatch.LastError,
                    AttemptCount = dispatch.AttemptCount
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Reload to detect if status was set to Cancelled during processing
        await _dbContext.Entry(importFile).ReloadAsync(cancellationToken);

        importFile.ImportedLines = imported;
        importFile.LinesWithError = sapErrors;
        importFile.ProcessingCompletedAtUtc = DateTime.Now;

        if (importFile.Status == ImportStatus.Cancelled)
        {
            // Keep Cancelled status; imported lines stay as Imported
        }
        else
        {
            importFile.Status = sapErrors > 0
                ? (imported > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Failed)
                : (imported > 0 ? ImportStatus.Completed : ImportStatus.Failed);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var duration = (long)(DateTime.Now - start).TotalMilliseconds;

        await _eventPublisher.PublishAsync(new ImportProcessedEvent
        {
            ImportFileId = importFileId,
            CompanyDb = importFile.CompanyDb,
            Imported = imported,
            SapErrors = sapErrors,
            Duplicated = importFile.DuplicatedLines,
            Invalid = importFile.InvalidLines,
            Status = importFile.Status.ToString(),
            DurationMs = duration
        }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Build a readable summary message so operators get the full picture
        // without expanding details. Include: file name, totals, duration, status.
        var totalGroups = groups.Count;
        var dispatchedGroups = imported > 0
            ? await _dbContext.JournalEntryDispatches
                .Where(d => d.ImportFileId == importFile.Id && d.Status == JournalDispatchStatus.Dispatched)
                .CountAsync(cancellationToken)
            : 0;

        string summaryMessage = importFile.Status switch
        {
            ImportStatus.Completed => $"Importacao '{importFile.OriginalFileName}' concluida com sucesso — {imported} linha(s) em {dispatchedGroups} lancamento(s) SAP. Duracao: {FormatDuration(duration)}.",
            ImportStatus.PartiallyCompleted => $"Importacao '{importFile.OriginalFileName}' concluida parcialmente — {imported} linha(s) enviadas, {sapErrors} com erro em {totalGroups - dispatchedGroups} grupo(s). Duracao: {FormatDuration(duration)}.",
            ImportStatus.Failed => $"Importacao '{importFile.OriginalFileName}' falhou — 0 linha(s) enviadas, {sapErrors} erro(s) em {totalGroups} grupo(s). Verifique os logs de cada grupo para o motivo. Duracao: {FormatDuration(duration)}.",
            ImportStatus.Cancelled => $"Importacao '{importFile.OriginalFileName}' cancelada pelo usuario — {imported} linha(s) ja enviadas antes do cancelamento.",
            _ => $"Importacao '{importFile.OriginalFileName}' processada — status: {importFile.Status}. Duracao: {FormatDuration(duration)}."
        };

        var summaryDetails = $"Arquivo: {importFile.OriginalFileName}\n" +
                             $"Layout: {importFile.LayoutDetected}\n" +
                             $"Empresa: {importFile.CompanyDb}\n" +
                             $"Total de linhas: {importFile.TotalLines}\n" +
                             $"  - Validas/Reprocessadas: {validLines.Count}\n" +
                             $"  - Invalidas: {importFile.InvalidLines}\n" +
                             $"  - Duplicadas: {importFile.DuplicatedLines}\n" +
                             $"Grupos processados: {totalGroups}\n" +
                             $"  - Enviados ao SAP: {dispatchedGroups}\n" +
                             $"  - Com erro: {totalGroups - dispatchedGroups}\n" +
                             $"Linhas enviadas: {imported}\n" +
                             $"Linhas com erro: {sapErrors}\n" +
                             $"Duracao: {FormatDuration(duration)}\n" +
                             $"Correlation ID: {importFile.CorrelationId}";

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = importFile.Status == ImportStatus.Failed ? LogSeverities.Error
                 : sapErrors > 0 ? LogSeverities.Warning
                 : LogSeverities.Info,
            Category = LogCategories.Integration,
            Source = nameof(ImportProcessor),
            Operation = "ProcessImport",
            Message = summaryMessage,
            Details = summaryDetails,
            ImportFileId = importFileId,
            CompanyDb = importFile.CompanyDb,
            CorrelationId = importFile.CorrelationId,
            DurationMs = duration,
            StatusAfter = importFile.Status.ToString()
        }, cancellationToken);

        return new ImportProcessResult
        {
            ImportFileId = importFileId,
            Imported = imported,
            Duplicated = importFile.DuplicatedLines,
            Invalid = importFile.InvalidLines,
            SapErrors = sapErrors,
            DurationMs = duration,
            Status = importFile.Status.ToString()
        };
    }

    private static string BuildGroupKeyLabel(ImportLine firstLine)
    {
        // Human-readable label stored in LancamentoSapDispatch.GroupKey for
        // troubleshooting. SeqLancamento is intentionally excluded — the
        // group merges all lines that share the same Referencia + dates.
        return $"{firstLine.Reference}|{firstLine.PostingDate:yyyy-MM-dd}|{firstLine.DueDate:yyyy-MM-dd}|{firstLine.DocumentDate:yyyy-MM-dd}";
    }

    private static int? ResolveBplId(ImportLine line, ImportFile importFile, List<BranchMapping> mappings)
    {
        var branchCode = importFile.UseBranchFromFile && !string.IsNullOrWhiteSpace(line.BranchCode)
            ? line.BranchCode
            : importFile.BranchDefault;

        if (string.IsNullOrWhiteSpace(branchCode)) return null;

        var mapping = mappings.FirstOrDefault(m =>
            m.FileBranchCode.Equals(branchCode, StringComparison.OrdinalIgnoreCase));

        return mapping?.BplId;
    }

    private static int? ExtractDocEntry(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("DocEntry", out var docEntry))
                return docEntry.GetInt32();
        }
        catch (JsonException) { }
        return null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value.Substring(0, max - 3) + "...";
    }

    private static string FormatDuration(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        var seconds = ms / 1000.0;
        if (seconds < 60) return $"{seconds:N1}s";
        var minutes = seconds / 60;
        return $"{minutes:N1}min";
    }
}
