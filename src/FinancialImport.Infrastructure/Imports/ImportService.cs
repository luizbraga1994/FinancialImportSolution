using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Models;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Abstractions;
using FluentValidation;

namespace FinancialImport.Infrastructure.Imports;

public sealed class ImportService : IImportService
{
    private readonly IImportRepository _repository;
    private readonly IImportLayoutResolver _layoutResolver;
    private readonly IHashService _hashService;
    private readonly IValidator<LancamentoContabilImportado> _validator;
    private readonly IUserContext _userContext;
    private readonly ICompanyContext _companyContext;
    private readonly IClock _clock;

    public ImportService(
        IImportRepository repository,
        IImportLayoutResolver layoutResolver,
        IHashService hashService,
        IValidator<LancamentoContabilImportado> validator,
        IUserContext userContext,
        ICompanyContext companyContext,
        IClock clock)
    {
        _repository = repository;
        _layoutResolver = layoutResolver;
        _hashService = hashService;
        _validator = validator;
        _userContext = userContext;
        _companyContext = companyContext;
        _clock = clock;
    }

    public async Task<ImportPreviewResult> PreviewAsync(ImportFileContext context, CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuário não autenticado.");
        var companyDb = _companyContext.CompanyDb ?? throw new InvalidOperationException("Company não selecionada.");

        var fileHash = _hashService.ComputeHash(context.FileBytes);
        if (await _repository.ExistsFileHashAsync(companyDb, fileHash, cancellationToken))
        {
            return new ImportPreviewResult
            {
                Errors = new[] { "Arquivo já importado para esta company." }
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

    public Task<ImportProcessResult> ProcessAsync(long importFileId, CancellationToken cancellationToken = default)
    {
        // Etapa de integração com SAP será implementada no próximo passo
        return Task.FromResult(new ImportProcessResult
        {
            ImportFileId = importFileId,
            Imported = 0,
            Duplicated = 0,
            Invalid = 0,
            SapErrors = 0
        });
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
