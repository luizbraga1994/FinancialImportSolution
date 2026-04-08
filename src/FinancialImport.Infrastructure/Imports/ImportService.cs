using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Abstractions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Imports;

public sealed class ImportService : IImportService
{
    private readonly IImportRepository _repository;
    private readonly IImportLayoutResolver _layoutResolver;
    private readonly IHashService _hashService;
    private readonly IValidator<LancamentoContabilImportado> _validator;
    private readonly IUserContext _userContext;
    private readonly ICompanyContext _companyContext;
    private readonly ISapSessionStore _sapSessionStore;
    private readonly ISapJournalEntryService _sapJournalEntryService;
    private readonly AppDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        IImportRepository repository,
        IImportLayoutResolver layoutResolver,
        IHashService hashService,
        IValidator<LancamentoContabilImportado> validator,
        IUserContext userContext,
        ICompanyContext companyContext,
        ISapSessionStore sapSessionStore,
        ISapJournalEntryService sapJournalEntryService,
        AppDbContext dbContext,
        IClock clock,
        ILogger<ImportService> logger)
    {
        _repository = repository;
        _layoutResolver = layoutResolver;
        _hashService = hashService;
        _validator = validator;
        _userContext = userContext;
        _companyContext = companyContext;
        _sapSessionStore = sapSessionStore;
        _sapJournalEntryService = sapJournalEntryService;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ImportPreviewResult> PreviewAsync(ImportFileContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== INICIO PREVIEWASYNC ===");

        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");
        var companyDb = _companyContext.CompanyDb ?? throw new InvalidOperationException("Company nao selecionada.");

        _logger.LogInformation("PreviewAsync - UserId: {UserId}, CompanyDb: '{CompanyDb}'", userId, companyDb);
        _logger.LogInformation("PreviewAsync - FileName: {FileName}, Headers: {Headers}",
            context.FileName, string.Join(", ", context.Headers.Take(10)));

        var fileHash = _hashService.ComputeHash(context.FileBytes);
        _logger.LogInformation("PreviewAsync - FileHash: {FileHash}", fileHash);

        // Verifica se o arquivo ja foi importado, mas permite reprocessamento se for Failed ou Rejected
        var existingFile = await _dbContext.ImportFiles
            .FirstOrDefaultAsync(f => f.CompanyDb == companyDb && f.FileHash == fileHash, cancellationToken);

        if (existingFile != null && existingFile.Status != ImportStatus.Failed && existingFile.Status != ImportStatus.Rejected)
        {
            _logger.LogWarning("PreviewAsync - Arquivo ja importado para esta company com status {Status}. FileHash: {FileHash}",
                existingFile.Status, fileHash);
            return new ImportPreviewResult
            {
                Errors = new[] { $"Arquivo ja importado para esta company. Status atual: {existingFile.Status}. Use a opcao de reprocessamento se necessario." }
            };
        }

        if (existingFile != null)
        {
            _logger.LogInformation("PreviewAsync - Arquivo com status {Status} encontrado. Permitindo reprocessamento.", existingFile.Status);
        }

        try
        {
            var parser = _layoutResolver.Resolve(context);
            context.DetectedLayout = parser.LayoutName;
            _logger.LogInformation("PreviewAsync - Layout detectado: {LayoutName}", parser.LayoutName);

            var parsed = await parser.ParseAsync(context, cancellationToken);
            _logger.LogInformation("PreviewAsync - Parse concluido. Total de linhas parseadas: {Count}", parsed.Count);

            var importFile = new ImportFile
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
                ImportedAt = _clock.Now
            };

            var lines = new List<ImportLine>();
            var errors = new List<string>();
            var validCount = 0;
            var invalidCount = 0;
            var duplicatedCount = 0;

            foreach (var line in parsed)
            {
                var validation = await _validator.ValidateAsync(line, cancellationToken);
                var status = validation.IsValid ? ImportLineStatus.Valid : ImportLineStatus.Invalid;

                if (!validation.IsValid)
                {
                    invalidCount++;
                    foreach (var error in validation.Errors)
                    {
                        errors.Add($"Linha: {line.Referencia} - {error.ErrorMessage}");
                        _logger.LogDebug("Erro de validacao na linha: {Error}", error.ErrorMessage);
                    }
                }
                else
                {
                    validCount++;
                }

                var businessKey = BuildBusinessKey(companyDb, line);
                var businessKeyHash = _hashService.ComputeHash(businessKey);
                var isDuplicate = await _repository.ExistsBusinessKeyAsync(companyDb, businessKeyHash, cancellationToken);

                if (isDuplicate)
                {
                    status = ImportLineStatus.Duplicated;
                    duplicatedCount++;
                    validCount--; // Remove do count de validas se for duplicada
                    _logger.LogDebug("Linha duplicada detectada. BusinessKeyHash: {BusinessKeyHash}", businessKeyHash);
                }

                var importLine = new ImportLine
                {
                    ImportFile = importFile,
                    LineHash = _hashService.ComputeHash(JsonSerializer.Serialize(line)),
                    BusinessKeyHash = businessKeyHash,
                    SeqLancamento = line.SeqLancamento,
                    Reference = line.Referencia,
                    AccountCode = line.ContaContabil,
                    ContraAccountCode = line.ContaContrapartida,
                    PostingDate = line.DataLancamento,
                    DueDate = line.DataVencimento,
                    DocumentDate = line.DataDocumento,
                    Amount = line.Valor,
                    CreditAmount = line.ValorCredito,
                    DebitAmount = line.ValorDebito,
                    LineMemo = line.HistoricoLinha,
                    BranchCode = line.Filial,
                    CompanyDb = companyDb,
                    Status = status,
                    ValidationMessage = validation.IsValid ? null : string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)),
                    SourceJson = JsonSerializer.Serialize(line)
                };

                lines.Add(importLine);
            }

            importFile.ValidLines = validCount;
            importFile.InvalidLines = invalidCount;
            importFile.DuplicatedLines = duplicatedCount;
            importFile.LinesWithError = 0;
            importFile.ImportedLines = 0;

            _logger.LogInformation("PreviewAsync - Resumo: Total={Total}, Validas={Valid}, Invalidas={Invalid}, Duplicadas={Duplicated}",
                parsed.Count, validCount, invalidCount, duplicatedCount);

            long importFileId;

            if (existingFile != null && (existingFile.Status == ImportStatus.Failed || existingFile.Status == ImportStatus.Rejected))
            {
                // Reprocessamento: remove linhas antigas e atualiza o arquivo
                _logger.LogInformation("PreviewAsync - Reprocessando arquivo existente {FileId}", existingFile.Id);

                var oldLines = await _dbContext.ImportLines
                    .Where(l => l.ImportFileId == existingFile.Id)
                    .ToListAsync(cancellationToken);
                _dbContext.ImportLines.RemoveRange(oldLines);

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
                existingFile.ImportedAt = _clock.Now;

                _dbContext.ImportFiles.Update(existingFile);
                await _dbContext.SaveChangesAsync(cancellationToken);

                importFileId = existingFile.Id;
            }
            else
            {
                importFileId = await _repository.AddImportFileAsync(importFile, cancellationToken);
            }

            await _repository.AddImportLinesAsync(lines, cancellationToken);

            return new ImportPreviewResult
            {
                ImportFileId = importFileId,
                LayoutDetected = parser.LayoutName,
                Lines = parsed,
                Errors = errors.Distinct().ToArray(),
                ValidLines = validCount,
                InvalidLines = invalidCount,
                DuplicatedLines = duplicatedCount
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "PreviewAsync - Erro de layout: {Message}. Headers do arquivo: {Headers}",
                ex.Message, string.Join(", ", context.Headers));
            return new ImportPreviewResult
            {
                Errors = new[] { $"Layout nao reconhecido: {ex.Message}. Headers encontrados: {string.Join(", ", context.Headers.Take(15))}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreviewAsync - Erro inesperado ao processar arquivo {FileName}", context.FileName);
            return new ImportPreviewResult
            {
                Errors = new[] { $"Erro ao processar arquivo: {ex.Message}" }
            };
        }
    }

    public async Task<ImportProcessResult> ProcessAsync(long importFileId, CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var importFile = await _repository.GetImportFileAsync(importFileId, cancellationToken);
        if (importFile == null)
            throw new KeyNotFoundException($"Arquivo de importacao {importFileId} nao encontrado.");

        var sapSession = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
        if (sapSession == null)
            throw new InvalidOperationException("Sessao SAP nao ativa. Faca login na company antes de processar.");

        if (!sapSession.CompanyDb.Equals(importFile.CompanyDb, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sessao SAP ativa para company diferente do arquivo.");

        importFile.Status = ImportStatus.Processing;
        await _repository.UpdateImportFileAsync(importFile, cancellationToken);

        var validLines = importFile.Lines
            .Where(l => l.Status == ImportLineStatus.Valid || l.Status == ImportLineStatus.SapError)
            .ToList();

        var branchMappings = await _dbContext.BranchMappings
            .Where(b => b.CompanyDb == importFile.CompanyDb && b.IsActive)
            .ToListAsync(cancellationToken);

        int imported = 0, duplicated = 0, invalid = 0, sapErrors = 0;

        var groups = validLines
            .GroupBy(l => l.Reference ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Processando arquivo {FileId}: {LineCount} linhas agrupadas em {GroupCount} lancamentos SAP.",
            importFileId, validLines.Count, groups.Count);

        try
        {
            foreach (var group in groups)
            {
                var groupLines = group.ToList();
                var firstLine = groupLines[0];

                try
                {
                    int? bplId = ResolveBplId(firstLine, importFile, branchMappings);

                    var journalEntry = new SapJournalEntry
                    {
                        ReferenceDate = firstLine.PostingDate,
                        DueDate = firstLine.DueDate,
                        TaxDate = firstLine.DocumentDate,
                        Memo = Truncate(firstLine.Reference, 50),
                        Reference = Truncate(firstLine.Reference, 27),
                        BPLID = bplId,
                        JournalEntryLines = new List<SapJournalEntryLine>()
                    };

                    foreach (var line in groupLines)
                    {
                        journalEntry.JournalEntryLines.Add(new SapJournalEntryLine
                        {
                            AccountCode = line.AccountCode,
                            Debit = line.DebitAmount ?? 0,
                            Credit = line.CreditAmount ?? 0,
                            LineMemo = Truncate(line.LineMemo, 50)
                        });
                        journalEntry.JournalEntryLines.Add(new SapJournalEntryLine
                        {
                            AccountCode = line.ContraAccountCode,
                            Debit = line.CreditAmount ?? 0,
                            Credit = line.DebitAmount ?? 0,
                            LineMemo = Truncate(line.LineMemo, 50)
                        });
                    }

                    var result = await _sapJournalEntryService.CreateJournalEntryAsync(sapSession, journalEntry, cancellationToken);

                    if (result.Success)
                    {
                        int? docEntry = ExtractDocEntry(result.RawResponse);
                        foreach (var line in groupLines)
                        {
                            line.Status = ImportLineStatus.Imported;
                            line.SapReturnMessage = "OK";
                            line.SapDocEntry = docEntry;
                            imported++;
                        }
                    }
                    else
                    {
                        foreach (var line in groupLines)
                        {
                            line.Status = ImportLineStatus.SapError;
                            line.SapReturnMessage = result.ErrorMessage;
                            sapErrors++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Erro ao processar grupo '{Reference}' do arquivo {FileId}",
                        group.Key, importFileId);
                    foreach (var line in groupLines)
                    {
                        line.Status = ImportLineStatus.SapError;
                        line.SapReturnMessage = ex.Message;
                        sapErrors++;
                    }
                }
            }
        }
        finally
        {
            duplicated = importFile.Lines.Count(l => l.Status == ImportLineStatus.Duplicated);
            invalid = importFile.Lines.Count(l => l.Status == ImportLineStatus.Invalid);

            importFile.ImportedLines = imported;
            importFile.LinesWithError = sapErrors;
            importFile.DuplicatedLines = duplicated;
            importFile.InvalidLines = invalid;

            if (sapErrors > 0 || invalid > 0)
                importFile.Status = imported > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Failed;
            else
                importFile.Status = ImportStatus.Completed;

            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return new ImportProcessResult
        {
            ImportFileId = importFileId,
            Imported = imported,
            Duplicated = duplicated,
            Invalid = invalid,
            SapErrors = sapErrors
        };
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
            {
                return docEntry.GetInt32();
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private static string BuildBusinessKey(string companyDb, LancamentoContabilImportado line)
    {
        return string.Join("|",
            companyDb,
            line.ContaContabil,
            line.ContaContrapartida,
            line.DataLancamento.ToString("yyyy-MM-dd"),
            line.Valor.ToString("0.00"),
            line.HistoricoLinha,
            line.Filial ?? string.Empty);
    }
}