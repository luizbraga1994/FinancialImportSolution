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
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");
        var companyDb = _companyContext.CompanyDb ?? throw new InvalidOperationException("Company nao selecionada.");

        var fileHash = _hashService.ComputeHash(context.FileBytes);
        if (await _repository.ExistsFileHashAsync(companyDb, fileHash, cancellationToken))
        {
            return new ImportPreviewResult
            {
                Errors = new[] { "Arquivo ja importado para esta company." }
            };
        }

        var parser = _layoutResolver.Resolve(context);
        context.DetectedLayout = parser.LayoutName;
        var parsed = await parser.ParseAsync(context, cancellationToken);

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

        foreach (var line in parsed)
        {
            var validation = await _validator.ValidateAsync(line, cancellationToken);
            var status = validation.IsValid ? ImportLineStatus.Valid : ImportLineStatus.Invalid;
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors.Select(e => e.ErrorMessage));
            }

            var businessKey = BuildBusinessKey(companyDb, line);
            var businessKeyHash = _hashService.ComputeHash(businessKey);
            var isDuplicate = await _repository.ExistsBusinessKeyAsync(companyDb, businessKeyHash, cancellationToken);
            if (isDuplicate)
            {
                status = ImportLineStatus.Duplicated;
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

        importFile.ValidLines = lines.Count(l => l.Status == ImportLineStatus.Valid);
        importFile.InvalidLines = lines.Count(l => l.Status == ImportLineStatus.Invalid);
        importFile.DuplicatedLines = lines.Count(l => l.Status == ImportLineStatus.Duplicated);
        importFile.LinesWithError = 0;
        importFile.ImportedLines = 0;

        var importFileId = await _repository.AddImportFileAsync(importFile, cancellationToken);
        await _repository.AddImportLinesAsync(lines, cancellationToken);

        return new ImportPreviewResult
        {
            ImportFileId = importFileId,
            LayoutDetected = parser.LayoutName,
            Lines = parsed,
            Errors = errors.Distinct().ToArray()
        };
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

        try
        {
            foreach (var line in validLines)
            {
                try
                {
                    int? bplId = ResolveBplId(line, importFile, branchMappings);

                    var journalEntry = new SapJournalEntry
                    {
                        ReferenceDate = line.PostingDate,
                        DueDate = line.DueDate,
                        TaxDate = line.DocumentDate,
                        Memo = line.Reference,
                        Reference = line.Reference,
                        BPLID = bplId,
                        JournalEntryLines = new List<SapJournalEntryLine>
                        {
                            new()
                            {
                                AccountCode = line.AccountCode,
                                Debit = line.DebitAmount ?? 0,
                                Credit = line.CreditAmount ?? 0,
                                LineMemo = line.LineMemo
                            },
                            new()
                            {
                                AccountCode = line.ContraAccountCode,
                                Debit = line.CreditAmount ?? 0,
                                Credit = line.DebitAmount ?? 0,
                                LineMemo = line.LineMemo
                            }
                        }
                    };

                    var result = await _sapJournalEntryService.CreateJournalEntryAsync(sapSession, journalEntry, cancellationToken);

                    if (result.Success)
                    {
                        line.Status = ImportLineStatus.Imported;
                        line.SapReturnMessage = "OK";
                        TryExtractDocEntry(result.RawResponse, line);
                        imported++;
                    }
                    else
                    {
                        line.Status = ImportLineStatus.SapError;
                        line.SapReturnMessage = result.ErrorMessage;
                        sapErrors++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar linha {LineId} do arquivo {FileId}", line.Id, importFileId);
                    line.Status = ImportLineStatus.SapError;
                    line.SapReturnMessage = ex.Message;
                    sapErrors++;
                }
            }
        }
        finally
        {
            // Always update final status even if cancellation or unexpected error occurs
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

    private static void TryExtractDocEntry(string? rawResponse, ImportLine line)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return;
        try
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (doc.RootElement.TryGetProperty("DocEntry", out var docEntry))
            {
                line.SapDocEntry = docEntry.GetInt32();
            }
        }
        catch (JsonException)
        {
            // Raw response is not valid JSON - DocEntry will remain null
        }
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
