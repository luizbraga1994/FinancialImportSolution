using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Messaging;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Imports;
using FinancialImport.Shared.Logging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Orchestrates the upload → preview → confirm pipeline. Heavy lifting
/// is delegated to collaborators so this class is focused on business
/// flow: deduplication, persistence, outbox publication, and domain
/// events. The actual SAP Service Layer calls are done by
/// <see cref="ImportProcessor"/> either inline or via the async worker.
/// </summary>
public sealed class ImportService : IImportService
{
    private readonly IImportRepository _repository;
    private readonly IImportLayoutResolver _layoutResolver;
    private readonly IHashService _hashService;
    private readonly IValidator<LancamentoContabilImportado> _validator;
    private readonly IUserContext _userContext;
    private readonly ICompanyContext _companyContext;
    private readonly AppDbContext _dbContext;
    private readonly BusinessKeyBuilder _keyBuilder;
    private readonly IImportProcessor _processor;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICommandBus _commandBus;
    private readonly IAuditLogger _audit;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly ImportProcessingOptions _processingOptions;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        IImportRepository repository,
        IImportLayoutResolver layoutResolver,
        IHashService hashService,
        IValidator<LancamentoContabilImportado> validator,
        IUserContext userContext,
        ICompanyContext companyContext,
        AppDbContext dbContext,
        BusinessKeyBuilder keyBuilder,
        IImportProcessor processor,
        IEventPublisher eventPublisher,
        ICommandBus commandBus,
        IAuditLogger audit,
        ICorrelationContextAccessor correlation,
        IOptions<ImportProcessingOptions> processingOptions,
        ILogger<ImportService> logger)
    {
        _repository = repository;
        _layoutResolver = layoutResolver;
        _hashService = hashService;
        _validator = validator;
        _userContext = userContext;
        _companyContext = companyContext;
        _dbContext = dbContext;
        _keyBuilder = keyBuilder;
        _processor = processor;
        _eventPublisher = eventPublisher;
        _commandBus = commandBus;
        _audit = audit;
        _correlation = correlation;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    public async Task<ImportPreviewResult> PreviewAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId
            ?? throw new InvalidOperationException("Usuario nao autenticado.");
        var companyDb = _companyContext.CompanyDb
            ?? throw new InvalidOperationException("Company nao selecionada.");

        var correlationId = _correlation.Current?.CorrelationId ?? Guid.NewGuid().ToString("N");
        var fileHash = _hashService.ComputeHash(context.FileBytes);

        _logger.LogInformation(
            "ImportService.PreviewAsync start user={UserId} company={Company} file={FileName} correlation={CorrelationId}",
            userId, companyDb, context.FileName, correlationId);

        var existingFile = await _dbContext.ImportFiles
            .FirstOrDefaultAsync(f => f.CompanyDb == companyDb && f.FileHash == fileHash, cancellationToken);

        if (existingFile != null
            && existingFile.Status != ImportStatus.Failed
            && existingFile.Status != ImportStatus.Rejected)
        {
            return new ImportPreviewResult
            {
                CorrelationId = correlationId,
                Errors = new[]
                {
                    $"Arquivo ja importado para esta company. Status atual: {existingFile.Status}"
                }
            };
        }

        ILayoutImportParser parser;
        IReadOnlyCollection<LancamentoContabilImportado> parsed;
        try
        {
            parser = _layoutResolver.Resolve(context);
            context.DetectedLayout = parser.LayoutName;
            parsed = await parser.ParseAsync(context, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Warning,
                Category = LogCategories.Functional,
                Source = nameof(ImportService),
                Operation = "Preview.LayoutResolve",
                Message = $"Layout nao reconhecido: {ex.Message}",
                Details = $"Headers={string.Join(", ", context.Headers.Take(15))}",
                UserId = userId,
                CompanyDb = companyDb,
                CorrelationId = correlationId
            }, cancellationToken);

            return new ImportPreviewResult
            {
                CorrelationId = correlationId,
                Errors = new[] { $"Layout nao reconhecido: {ex.Message}" }
            };
        }

        // Build lines and compute business keys ONCE, then do a single
        // set-based dedup query against the database (no more N+1).
        var errors = new List<string>();
        var lines = new List<ImportLine>(parsed.Count);
        var businessKeyHashes = new HashSet<string>(StringComparer.Ordinal);
        var lineInfos = new List<(LancamentoContabilImportado Source, string BusinessKey, string BusinessKeyHash, bool Valid, string? ValidationMessage)>();

        foreach (var sourceLine in parsed)
        {
            var validation = await _validator.ValidateAsync(sourceLine, cancellationToken);
            var valid = validation.IsValid;
            string? validationMessage = null;
            if (!valid)
            {
                validationMessage = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                foreach (var err in validation.Errors)
                    errors.Add($"{sourceLine.Referencia}: {err.ErrorMessage}");
            }

            var businessKey = _keyBuilder.BuildBusinessKey(companyDb, sourceLine);
            var businessKeyHash = _hashService.ComputeHash(businessKey);
            businessKeyHashes.Add(businessKeyHash);
            lineInfos.Add((sourceLine, businessKey, businessKeyHash, valid, validationMessage));
        }

        var existingKeys = await _repository.GetExistingBusinessKeysAsync(
            companyDb, businessKeyHashes, cancellationToken);

        var validCount = 0;
        var invalidCount = 0;
        var duplicatedCount = 0;

        foreach (var info in lineInfos)
        {
            var isDuplicate = existingKeys.Contains(info.BusinessKeyHash);
            var status = !info.Valid
                ? ImportLineStatus.Invalid
                : isDuplicate
                    ? ImportLineStatus.Duplicated
                    : ImportLineStatus.Valid;

            if (status == ImportLineStatus.Invalid) invalidCount++;
            else if (status == ImportLineStatus.Duplicated) duplicatedCount++;
            else validCount++;

            var seqLancamento = info.Source.SeqLancamento;
            var (groupKey, groupKeyHash) = _keyBuilder.BuildGroupKey(
                companyDb,
                info.Source.Referencia,
                info.Source.DataLancamento,
                info.Source.DataVencimento != DateTime.MinValue ? info.Source.DataVencimento : info.Source.DataLancamento,
                info.Source.DataDocumento != DateTime.MinValue ? info.Source.DataDocumento : info.Source.DataLancamento,
                seqLancamento);

            lines.Add(new ImportLine
            {
                LineHash = _hashService.ComputeHash(JsonSerializer.Serialize(info.Source)),
                BusinessKeyHash = info.BusinessKeyHash,
                SeqLancamento = seqLancamento,
                Reference = info.Source.Referencia,
                AccountCode = info.Source.ContaContabil,
                ContraAccountCode = info.Source.ContaContrapartida,
                PostingDate = info.Source.DataLancamento,
                DueDate = info.Source.DataVencimento != DateTime.MinValue
                    ? info.Source.DataVencimento
                    : info.Source.DataLancamento,
                DocumentDate = info.Source.DataDocumento != DateTime.MinValue
                    ? info.Source.DataDocumento
                    : info.Source.DataLancamento,
                Amount = info.Source.Valor,
                CreditAmount = info.Source.ValorCredito,
                DebitAmount = info.Source.ValorDebito,
                LineMemo = info.Source.HistoricoLinha,
                BranchCode = info.Source.Filial,
                CompanyDb = companyDb,
                Status = status,
                ValidationMessage = info.ValidationMessage,
                SourceJson = JsonSerializer.Serialize(info.Source),
                GroupKeyHash = groupKeyHash
            });
        }

        // Persist everything in a single transaction. Previously the
        // code tried to do this but referenced a non-existent
        // `transaction` variable, which meant the project did not
        // compile — this path is now correct.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        long importFileId;
        try
        {
            if (existingFile != null)
            {
                await _repository.RemoveLinesForFileAsync(existingFile.Id, cancellationToken);

                existingFile.UserId = userId;
                existingFile.OriginalFileName = context.FileName;
                existingFile.LayoutDetected = parser.LayoutName;
                existingFile.BranchDefault = context.BranchDefault;
                existingFile.UseBranchFromFile = context.UseBranchFromFile;
                existingFile.Status = ImportStatus.Validated;
                existingFile.TotalLines = parsed.Count;
                existingFile.ValidLines = validCount;
                existingFile.InvalidLines = invalidCount;
                existingFile.DuplicatedLines = duplicatedCount;
                existingFile.LinesWithError = 0;
                existingFile.ImportedLines = 0;
                existingFile.ImportedAt = DateTime.UtcNow;
                existingFile.CorrelationId = correlationId;

                _dbContext.ImportFiles.Update(existingFile);
                importFileId = existingFile.Id;
            }
            else
            {
                var file = new ImportFile
                {
                    UserId = userId,
                    CompanyDb = companyDb,
                    OriginalFileName = context.FileName,
                    FileHash = fileHash,
                    LayoutDetected = parser.LayoutName,
                    BranchDefault = context.BranchDefault,
                    UseBranchFromFile = context.UseBranchFromFile,
                    Status = ImportStatus.Validated,
                    TotalLines = parsed.Count,
                    ValidLines = validCount,
                    InvalidLines = invalidCount,
                    DuplicatedLines = duplicatedCount,
                    LinesWithError = 0,
                    ImportedLines = 0,
                    ImportedAt = DateTime.UtcNow,
                    CorrelationId = correlationId
                };
                await _dbContext.ImportFiles.AddAsync(file, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                importFileId = file.Id;
            }

            foreach (var line in lines) line.ImportFileId = importFileId;
            await _dbContext.ImportLines.AddRangeAsync(lines, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish integration event via transactional outbox.
            await _eventPublisher.PublishAsync(new ImportValidatedEvent
            {
                ImportFileId = importFileId,
                CompanyDb = companyDb,
                UserId = userId,
                LayoutDetected = parser.LayoutName,
                TotalLines = parsed.Count,
                ValidLines = validCount,
                InvalidLines = invalidCount,
                DuplicatedLines = duplicatedCount,
                FileHash = fileHash,
                OriginalFileName = context.FileName
            }, cancellationToken);
            // The event publisher enqueues into AppDbContext — flush it
            // inside the same business transaction.
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = LogSeverities.Info,
            Category = LogCategories.Functional,
            Source = nameof(ImportService),
            Operation = "Preview",
            Message = $"Preview OK: total={parsed.Count} valid={validCount} invalid={invalidCount} duplicated={duplicatedCount}",
            UserId = userId,
            CompanyDb = companyDb,
            ImportFileId = importFileId,
            CorrelationId = correlationId,
            StatusAfter = ImportStatus.Validated.ToString()
        }, cancellationToken);

        return new ImportPreviewResult
        {
            ImportFileId = importFileId,
            LayoutDetected = parser.LayoutName,
            Lines = parsed,
            Errors = errors.Distinct().ToArray(),
            ValidLines = validCount,
            InvalidLines = invalidCount,
            DuplicatedLines = duplicatedCount,
            CorrelationId = correlationId
        };
    }

    public async Task<ImportConfirmResult> ConfirmAsync(
        long importFileId,
        CancellationToken cancellationToken = default)
    {
        return await ConfirmInternalAsync(importFileId, isReprocess: false, cancellationToken);
    }

    public async Task<ImportConfirmResult> ReprocessAsync(
        long importFileId,
        CancellationToken cancellationToken = default)
    {
        return await ConfirmInternalAsync(importFileId, isReprocess: true, cancellationToken);
    }

    private async Task<ImportConfirmResult> ConfirmInternalAsync(
        long importFileId,
        bool isReprocess,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var importFile = await _repository.GetImportFileAsync(importFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Arquivo de importacao {importFileId} nao encontrado.");

        var correlationId = importFile.CorrelationId
            ?? _correlation.Current?.CorrelationId
            ?? Guid.NewGuid().ToString("N");

        // Async mode: enqueue command and return immediately.
        if (_processingOptions.UseAsyncConfirmation)
        {
            await _commandBus.SendAsync(new ProcessImportCommand
            {
                ImportFileId = importFileId,
                UserId = userId,
                CompanyDb = importFile.CompanyDb,
                IsReprocess = isReprocess
            }, cancellationToken);
            // Make sure the command is persisted.
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Info,
                Category = LogCategories.Functional,
                Source = nameof(ImportService),
                Operation = isReprocess ? "Reprocess" : "Confirm",
                Message = "Import confirmation enqueued for async processing.",
                ImportFileId = importFileId,
                UserId = userId,
                CompanyDb = importFile.CompanyDb,
                CorrelationId = correlationId
            }, cancellationToken);

            return new ImportConfirmResult
            {
                ImportFileId = importFileId,
                Accepted = true,
                IsAsync = true,
                CorrelationId = correlationId
            };
        }

        // Sync mode: run the processor inline so the user gets
        // immediate feedback (legacy flow).
        var result = await _processor.ExecuteAsync(importFileId, cancellationToken);
        return new ImportConfirmResult
        {
            ImportFileId = importFileId,
            Accepted = true,
            IsAsync = false,
            CorrelationId = correlationId,
            SynchronousResult = result
        };
    }
}
