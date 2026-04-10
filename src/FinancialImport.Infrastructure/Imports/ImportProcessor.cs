using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
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
    private readonly ISapJournalEntryService _sapService;
    private readonly JournalEntryBuilder _entryBuilder;
    private readonly IUserContext _userContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ImportProcessor> _logger;

    public ImportProcessor(
        AppDbContext dbContext,
        IImportRepository repository,
        ISapSessionStore sapSessionStore,
        ISapJournalEntryService sapService,
        JournalEntryBuilder entryBuilder,
        IUserContext userContext,
        IEventPublisher eventPublisher,
        IAuditLogger audit,
        ILogger<ImportProcessor> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sapSessionStore = sapSessionStore;
        _sapService = sapService;
        _entryBuilder = entryBuilder;
        _userContext = userContext;
        _eventPublisher = eventPublisher;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ImportProcessResult> ExecuteAsync(
        long importFileId,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.UtcNow;
        var userId = _userContext.UserId
            ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var importFile = await _repository.GetImportFileWithLinesAsync(importFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Arquivo de importacao {importFileId} nao encontrado.");

        var sapSession = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
        if (sapSession == null)
            throw new InvalidOperationException("Sessao SAP nao ativa. Faca login na company antes de processar.");

        if (!sapSession.CompanyDb.Equals(importFile.CompanyDb, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sessao SAP ativa para company diferente do arquivo.");

        importFile.Status = ImportStatus.Processing;
        importFile.ProcessingStartedAtUtc = start;
        await _repository.UpdateImportFileAsync(importFile, cancellationToken);

        var validLines = importFile.Lines
            .Where(l => l.Status == ImportLineStatus.Valid)
            .ToList();

        _logger.LogInformation(
            "Processing file={FileId} validLines={Count} correlation={CorrelationId}",
            importFileId, validLines.Count, importFile.CorrelationId);

        var branchMappings = await _dbContext.BranchMappings
            .Where(b => b.CompanyDb == importFile.CompanyDb && b.IsActive)
            .ToListAsync(cancellationToken);

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
                CreatedAtUtc = DateTime.UtcNow,
                LastAttemptAtUtc = DateTime.UtcNow,
                CorrelationId = importFile.CorrelationId
            };

            if (existingDispatch != null)
            {
                dispatch.AttemptCount += 1;
                dispatch.LastAttemptAtUtc = DateTime.UtcNow;
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
            var build = _entryBuilder.Build(dispatch.GroupKey, dispatch.GroupKeyHash, groupLines, bplId);
            if (!build.IsBalanced)
            {
                _logger.LogWarning(
                    "Journal entry not balanced file={FileId} group={GroupKey} debit={Debit} credit={Credit}",
                    importFile.Id, dispatch.GroupKey, build.TotalDebit, build.TotalCredit);
            }

            SapResult sapResult;
            try
            {
                sapResult = await _sapService.CreateJournalEntryAsync(sapSession, build.Payload, cancellationToken);
            }
            catch (Exception ex)
            {
                sapResult = SapResult.Fail($"Erro de comunicacao: {ex.Message}");
            }

            if (sapResult.Success)
            {
                var docEntry = ExtractDocEntry(sapResult.RawResponse);
                dispatch.Status = JournalDispatchStatus.Dispatched;
                dispatch.DispatchedAtUtc = DateTime.UtcNow;
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
                    DurationMs = (long)(DateTime.UtcNow - start).TotalMilliseconds
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

        importFile.ImportedLines = imported;
        importFile.LinesWithError = sapErrors;
        importFile.ProcessingCompletedAtUtc = DateTime.UtcNow;

        importFile.Status = sapErrors > 0
            ? (imported > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Failed)
            : (imported > 0 ? ImportStatus.Completed : ImportStatus.Failed);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var duration = (long)(DateTime.UtcNow - start).TotalMilliseconds;

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

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = sapErrors > 0 ? LogSeverities.Warning : LogSeverities.Info,
            Category = LogCategories.Integration,
            Source = nameof(ImportProcessor),
            Operation = "Process",
            Message = $"Import processed: imported={imported} sapErrors={sapErrors}",
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
}
