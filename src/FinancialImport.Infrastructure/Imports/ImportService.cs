using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
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
    private readonly ISapSessionStore _sapSessionStore;
    private readonly ISapChartOfAccountsService _chartOfAccounts;
    private readonly ISapBusinessPartnerService _businessPartners;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly ISystemSettingsService _settings;
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
        ISapSessionStore sapSessionStore,
        ISapChartOfAccountsService chartOfAccounts,
        ISapBusinessPartnerService businessPartners,
        ISapCompanySessionService sapSessionService,
        ISystemSettingsService settings,
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
        _sapSessionStore = sapSessionStore;
        _chartOfAccounts = chartOfAccounts;
        _businessPartners = businessPartners;
        _sapSessionService = sapSessionService;
        _settings = settings;
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
            && existingFile.Status != ImportStatus.Rejected
            && !context.AllowDuplicate)
        {
            return new ImportPreviewResult
            {
                CorrelationId = correlationId,
                IsDuplicateFile = true,
                ExistingFileStatus = existingFile.Status.ToString()
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

        var existingKeys = context.AllowDuplicate
            ? new HashSet<string>()
            : await _repository.GetExistingBusinessKeysAsync(
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
                info.Source.DataDocumento != DateTime.MinValue ? info.Source.DataDocumento : info.Source.DataLancamento);

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
                CostingCode = info.Source.CentroCusto,
                CompanyDb = companyDb,
                Status = status,
                ValidationMessage = info.ValidationMessage,
                SourceJson = JsonSerializer.Serialize(info.Source),
                GroupKeyHash = groupKeyHash
            });
        }

        // Validate and auto-complete account codes against the SAP chart of accounts.
        // This runs best-effort: if there is no active SAP session or the fetch
        // fails, lines keep their original codes and validation happens later.
        var accountsValidated = false;
        var invalidAccountCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var session = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
            if (session == null || !session.CompanyDb.Equals(companyDb, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("No active SAP session found for user {UserId} / company '{CompanyDb}'. Auto-creating...", userId, companyDb);
                var loginResult = await _sapSessionService.SignInCompanyAsync(
                    companyDb,
                    _settings.Get("Sap:UserName") ?? "",
                    _settings.Get("Sap:Password") ?? "",
                    cancellationToken);
                if (loginResult.Success)
                {
                    session = loginResult.Session;
                    _logger.LogInformation("SAP session auto-created for company '{CompanyDb}'.", companyDb);
                }
                else
                {
                    _logger.LogWarning("Failed to auto-create SAP session for '{CompanyDb}': {Error}", companyDb, loginResult.ErrorMessage);
                }
            }

            if (session != null)
            {
                var accounts = await _chartOfAccounts.GetAccountCodesAsync(session, cancellationToken);
                IReadOnlySet<string> cardCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    cardCodes = await _businessPartners.GetCardCodesAsync(session, cancellationToken);
                }
                catch (Exception bpEx)
                {
                    _logger.LogWarning(bpEx, "Failed to fetch Business Partners from SAP (non-critical). BP codes will not be validated.");
                }

                if (accounts.Count > 0)
                {
                    accountsValidated = true;
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line.AccountCode))
                        {
                            var resolved = _chartOfAccounts.ResolveAccountCode(line.AccountCode, accounts);
                            if (resolved == line.AccountCode && !accounts.ContainsKey(line.AccountCode))
                            {
                                if (SapAccountCodeHelper.IsBusinessPartner(line.AccountCode))
                                {
                                    // Letter-code not in COA → must be a Business Partner card code.
                                    // Validate against the BP list when available.
                                    if (cardCodes.Count > 0 && !cardCodes.Contains(line.AccountCode))
                                    {
                                        invalidAccountCodes.Add(line.AccountCode);
                                        var msg = $"Business Partner '{line.AccountCode}' nao encontrado no SAP.";
                                        line.ValidationMessage = string.IsNullOrWhiteSpace(line.ValidationMessage) ? msg : line.ValidationMessage + "; " + msg;
                                        if (line.Status == ImportLineStatus.Valid)
                                        {
                                            line.Status = ImportLineStatus.Invalid;
                                            validCount--;
                                            invalidCount++;
                                        }
                                    }
                                }
                                else
                                {
                                    invalidAccountCodes.Add(line.AccountCode);
                                    var msg = $"Conta '{line.AccountCode}' nao encontrada no plano de contas do SAP.";
                                    line.ValidationMessage = string.IsNullOrWhiteSpace(line.ValidationMessage) ? msg : line.ValidationMessage + "; " + msg;
                                    if (line.Status == ImportLineStatus.Valid)
                                    {
                                        line.Status = ImportLineStatus.Invalid;
                                        validCount--;
                                        invalidCount++;
                                    }
                                }
                            }
                            else
                            {
                                line.AccountCode = resolved;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(line.ContraAccountCode))
                        {
                            var resolved = _chartOfAccounts.ResolveAccountCode(line.ContraAccountCode, accounts);
                            if (resolved == line.ContraAccountCode && !accounts.ContainsKey(line.ContraAccountCode))
                            {
                                if (SapAccountCodeHelper.IsBusinessPartner(line.ContraAccountCode))
                                {
                                    if (cardCodes.Count > 0 && !cardCodes.Contains(line.ContraAccountCode))
                                    {
                                        invalidAccountCodes.Add(line.ContraAccountCode);
                                        var msg = $"Business Partner '{line.ContraAccountCode}' nao encontrado no SAP.";
                                        line.ValidationMessage = string.IsNullOrWhiteSpace(line.ValidationMessage) ? msg : line.ValidationMessage + "; " + msg;
                                        if (line.Status == ImportLineStatus.Valid)
                                        {
                                            line.Status = ImportLineStatus.Invalid;
                                            validCount--;
                                            invalidCount++;
                                        }
                                    }
                                }
                                else
                                {
                                    invalidAccountCodes.Add(line.ContraAccountCode);
                                    var msg = $"Contrapartida '{line.ContraAccountCode}' nao encontrada no plano de contas do SAP.";
                                    line.ValidationMessage = string.IsNullOrWhiteSpace(line.ValidationMessage) ? msg : line.ValidationMessage + "; " + msg;
                                    if (line.Status == ImportLineStatus.Valid)
                                    {
                                        line.Status = ImportLineStatus.Invalid;
                                        validCount--;
                                        invalidCount++;
                                    }
                                }
                            }
                            else
                            {
                                line.ContraAccountCode = resolved;
                            }
                        }
                    }

                    if (invalidAccountCodes.Count > 0)
                    {
                        _logger.LogWarning(
                            "Account validation found {Count} invalid code(s) in file '{FileName}': {Codes}",
                            invalidAccountCodes.Count, context.FileName,
                            string.Join(", ", invalidAccountCodes.Take(20)));
                    }
                    else
                    {
                        _logger.LogInformation(
                            "All account codes validated OK against SAP chart of accounts for '{FileName}'.",
                            context.FileName);
                    }
                }
                else
                {
                    _logger.LogWarning("ChartOfAccounts returned empty for company '{CompanyDb}'. Skipping account validation.", companyDb);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate accounts against SAP chart of accounts during upload (non-critical).");
        }

        // Persist everything in a single transaction.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        long importFileId;
        try
        {
            if (existingFile != null)
            {
                await _repository.RemoveLinesForFileAsync(existingFile.Id, cancellationToken);

                // Remove previous dispatch records so the processor re-sends to SAP
                // instead of treating the groups as already dispatched (idempotency bypass).
                if (context.AllowDuplicate)
                {
                    await _dbContext.JournalEntryDispatches
                        .Where(d => d.ImportFileId == existingFile.Id)
                        .ExecuteDeleteAsync(cancellationToken);

                    // Lines from OTHER import files that share the same business key
                    // hashes must also be removed before reinsertion. The global unique
                    // constraint on (CompanyDb, HashChaveNegocio) spans ALL files, so
                    // leftover rows from previous imports would block the insert below.
                    var hashList = businessKeyHashes.ToList();
                    await _dbContext.ImportLines
                        .Where(l => l.CompanyDb == companyDb && hashList.Contains(l.BusinessKeyHash))
                        .ExecuteDeleteAsync(cancellationToken);
                }

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
                existingFile.ImportedAt = DateTime.Now;
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
                    ImportedAt = DateTime.Now,
                    CorrelationId = correlationId
                };
                await _dbContext.ImportFiles.AddAsync(file, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                importFileId = file.Id;
            }

            // Duplicated lines are only shown in the preview; they already exist
            // in the DB (from a previous import file) and the unique constraint on
            // (CompanyDb, HashChaveNegocio) would reject inserting them again.
            var linesToInsert = lines
                .Where(l => l.Status != ImportLineStatus.Duplicated)
                .ToList();
            foreach (var line in linesToInsert) line.ImportFileId = importFileId;
            await _dbContext.ImportLines.AddRangeAsync(linesToInsert, cancellationToken);
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

        var previewMessage = invalidCount > 0 || duplicatedCount > 0
            ? $"Preview do arquivo '{context.FileName}' concluido com avisos — {validCount} linha(s) validas, {invalidCount} invalidas, {duplicatedCount} duplicadas (total {parsed.Count})."
            : $"Preview do arquivo '{context.FileName}' concluido — {validCount} linha(s) prontas para envio ao SAP.";

        var previewDetails = $"Arquivo: {context.FileName}\n" +
                             $"Layout detectado: {parser.LayoutName}\n" +
                             $"Empresa: {companyDb}\n" +
                             $"Usuario: {userId}\n" +
                             $"Total de linhas: {parsed.Count}\n" +
                             $"  - Validas: {validCount}\n" +
                             $"  - Invalidas: {invalidCount}\n" +
                             $"  - Duplicadas: {duplicatedCount}\n" +
                             $"Validacao de contas: {(accountsValidated ? "Sim" : "Nao (sem sessao SAP)")}\n" +
                             (invalidAccountCodes.Count > 0
                                 ? $"Contas invalidas: {string.Join(", ", invalidAccountCodes.Take(20))}\n"
                                 : "") +
                             $"Correlation ID: {correlationId}";

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = invalidCount > 0 ? LogSeverities.Warning : LogSeverities.Info,
            Category = LogCategories.Functional,
            Source = nameof(ImportService),
            Operation = "Preview",
            Message = previewMessage,
            Details = previewDetails,
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
