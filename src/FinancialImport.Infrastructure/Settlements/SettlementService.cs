using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settlements;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Logging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Orchestrates the upload → preview → confirm pipeline for the "Baixa de Notas
/// de Saída". During preview each line's open invoice is resolved in SAP (HANA)
/// so the operator sees, before confirming, which notes were located. Confirm
/// dispatches the Incoming Payments inline via <see cref="ISettlementProcessor"/>.
/// </summary>
public sealed class SettlementService : ISettlementService
{
    private readonly ISettlementRepository _repository;
    private readonly ISettlementParser _parser;
    private readonly IHashService _hashService;
    private readonly IValidator<SettlementLancamento> _validator;
    private readonly IUserContext _userContext;
    private readonly ICompanyContext _companyContext;
    private readonly AppDbContext _dbContext;
    private readonly ISettlementProcessor _processor;
    private readonly ISapOpenInvoiceService _openInvoices;
    private readonly IAuditLogger _audit;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly ILogger<SettlementService> _logger;

    public SettlementService(
        ISettlementRepository repository,
        ISettlementParser parser,
        IHashService hashService,
        IValidator<SettlementLancamento> validator,
        IUserContext userContext,
        ICompanyContext companyContext,
        AppDbContext dbContext,
        ISettlementProcessor processor,
        ISapOpenInvoiceService openInvoices,
        IAuditLogger audit,
        ICorrelationContextAccessor correlation,
        ILogger<SettlementService> logger)
    {
        _repository = repository;
        _parser = parser;
        _hashService = hashService;
        _validator = validator;
        _userContext = userContext;
        _companyContext = companyContext;
        _dbContext = dbContext;
        _processor = processor;
        _openInvoices = openInvoices;
        _audit = audit;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task<SettlementPreviewResult> PreviewAsync(SettlementFileContext context, CancellationToken cancellationToken = default)
    {
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");
        var companyDb = _companyContext.CompanyDb ?? throw new InvalidOperationException("Company nao selecionada.");

        var correlationId = _correlation.Current?.CorrelationId ?? Guid.NewGuid().ToString("N");
        var fileHash = _hashService.ComputeHash(context.FileBytes);

        var existingFile = await _dbContext.ReceivableSettlementFiles
            .FirstOrDefaultAsync(f => f.CompanyDb == companyDb && f.FileHash == fileHash, cancellationToken);

        if (existingFile != null
            && existingFile.Status != SettlementStatus.Failed
            && existingFile.Status != SettlementStatus.Rejected
            && !context.AllowDuplicate)
        {
            return new SettlementPreviewResult
            {
                CorrelationId = correlationId,
                IsDuplicateFile = true,
                ExistingFileStatus = existingFile.Status.ToString()
            };
        }

        if (!_parser.CanParse(context))
        {
            return new SettlementPreviewResult
            {
                CorrelationId = correlationId,
                Errors = new[] { "Layout da planilha de baixa não reconhecido. Verifique as colunas (DocPN, Nº Nota, Valor)." }
            };
        }

        var parsed = await _parser.ParseAsync(context, cancellationToken);

        var errors = new List<string>();
        var infos = new List<(SettlementLancamento Source, string BusinessKeyHash, bool Valid, string? Message)>();
        var businessKeyHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in parsed)
        {
            var validation = await _validator.ValidateAsync(source, cancellationToken);
            string? message = null;
            if (!validation.IsValid)
            {
                message = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage));
                foreach (var err in validation.Errors)
                    errors.Add($"{source.NumeroNota}: {err.ErrorMessage}");
            }

            var businessKey = SettlementKeyBuilder.BuildBusinessKey(companyDb, source);
            var businessKeyHash = _hashService.ComputeHash(businessKey);
            businessKeyHashes.Add(businessKeyHash);
            infos.Add((source, businessKeyHash, validation.IsValid, message));
        }

        var existingKeys = context.AllowDuplicate
            ? new HashSet<string>()
            : await _repository.GetExistingBusinessKeysAsync(companyDb, businessKeyHashes, cancellationToken);

        var lines = new List<ReceivableSettlementLine>(parsed.Count);
        int validCount = 0, invalidCount = 0, duplicatedCount = 0;

        for (var ordinal = 0; ordinal < infos.Count; ordinal++)
        {
            var info = infos[ordinal];
            var source = info.Source;
            var json = JsonSerializer.Serialize(source);

            // Per-line group key (business key + ordinal) so two payments for the
            // SAME note never collide and each becomes its own Incoming Payment.
            var groupKeyHash = _hashService.ComputeHash(
                SettlementKeyBuilder.BuildGroupKey(info.BusinessKeyHash, ordinal));

            var line = new ReceivableSettlementLine
            {
                LineHash = _hashService.ComputeHash(json),
                BusinessKeyHash = info.BusinessKeyHash,
                GroupKeyHash = groupKeyHash,
                CustomerDoc = source.DocPN,
                InvoiceSerial = source.NumeroNota,
                InvoiceSeries = source.Serie,
                BranchTaxId = source.CnpjFilial,
                InvoiceModel = source.Modelo,
                DocumentDate = source.DataDocumento != DateTime.MinValue ? source.DataDocumento : DateTime.Today,
                PaymentMeans = source.FormaPagamento,
                ReceivingAccount = source.ContaContabil,
                Amount = source.Valor,
                Discount = source.Desconto,
                Interest = source.Juros,
                PaymentDate = source.DataPagamento != DateTime.MinValue ? source.DataPagamento : DateTime.Today,
                Installments = source.QtdParcelas,
                CardBrand = source.Bandeira,
                CardLastDigits = source.Ultimo4Cartao,
                Reference = source.Referencia,
                CompanyDb = companyDb,
                ValidationMessage = info.Message,
                SourceJson = json
            };

            var isDuplicate = existingKeys.Contains(info.BusinessKeyHash);

            if (!info.Valid)
            {
                line.Status = SettlementLineStatus.Invalid;
                invalidCount++;
            }
            else if (isDuplicate)
            {
                line.Status = SettlementLineStatus.Duplicated;
                duplicatedCount++;
            }
            else
            {
                // Resolve the open invoice in SAP (HANA). Best-effort: a HANA
                // failure marks the line as not-found rather than aborting the
                // whole preview.
                var resolved = await TryResolveInvoiceAsync(companyDb, source, line, cancellationToken);
                if (resolved)
                {
                    line.Status = SettlementLineStatus.Valid;
                    validCount++;
                }
                else
                {
                    line.Status = SettlementLineStatus.InvoiceNotFound;
                    invalidCount++;
                }
            }

            lines.Add(line);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        long settlementFileId;
        try
        {
            if (existingFile != null)
            {
                await _repository.RemoveLinesForFileAsync(existingFile.Id, cancellationToken);

                // NOTE: dispatch records are intentionally preserved on re-upload.
                // They anchor the SAP DocEntry so the processor can verify in SAP
                // whether the payment still exists (skip) or was cancelled
                // (re-launch), instead of blindly creating a duplicate payment.

                existingFile.UserId = userId;
                existingFile.OriginalFileName = context.FileName;
                existingFile.LayoutDetected = _parser.LayoutName;
                existingFile.Status = SettlementStatus.Validated;
                existingFile.TotalLines = parsed.Count;
                existingFile.ValidLines = validCount;
                existingFile.InvalidLines = invalidCount;
                existingFile.DuplicatedLines = duplicatedCount;
                existingFile.LinesWithError = 0;
                existingFile.SettledLines = 0;
                existingFile.ImportedAt = DateTime.Now;
                existingFile.CorrelationId = correlationId;
                _dbContext.ReceivableSettlementFiles.Update(existingFile);
                settlementFileId = existingFile.Id;
            }
            else
            {
                var file = new ReceivableSettlementFile
                {
                    UserId = userId,
                    CompanyDb = companyDb,
                    OriginalFileName = context.FileName,
                    FileHash = fileHash,
                    LayoutDetected = _parser.LayoutName,
                    Status = SettlementStatus.Validated,
                    TotalLines = parsed.Count,
                    ValidLines = validCount,
                    InvalidLines = invalidCount,
                    DuplicatedLines = duplicatedCount,
                    LinesWithError = 0,
                    SettledLines = 0,
                    ImportedAt = DateTime.Now,
                    CorrelationId = correlationId
                };
                await _dbContext.ReceivableSettlementFiles.AddAsync(file, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                settlementFileId = file.Id;
            }

            foreach (var line in lines) line.SettlementFileId = settlementFileId;
            await _dbContext.ReceivableSettlementLines.AddRangeAsync(lines, cancellationToken);
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
            Level = invalidCount > 0 ? LogSeverities.Warning : LogSeverities.Info,
            Category = LogCategories.Functional,
            Source = nameof(SettlementService),
            Operation = "Preview",
            Message = $"Preview de baixa '{context.FileName}' — {validCount} válida(s), {invalidCount} inválida(s)/não localizada(s), {duplicatedCount} duplicada(s) (total {parsed.Count}).",
            UserId = userId,
            CompanyDb = companyDb,
            CorrelationId = correlationId,
            StatusAfter = SettlementStatus.Validated.ToString()
        }, cancellationToken);

        return new SettlementPreviewResult
        {
            SettlementFileId = settlementFileId,
            LayoutDetected = _parser.LayoutName,
            Lines = parsed,
            Errors = errors.Distinct().ToArray(),
            ValidLines = validCount,
            InvalidLines = invalidCount,
            DuplicatedLines = duplicatedCount,
            CorrelationId = correlationId
        };
    }

    private async Task<bool> TryResolveInvoiceAsync(
        string companyDb,
        SettlementLancamento source,
        ReceivableSettlementLine line,
        CancellationToken cancellationToken)
    {
        try
        {
            var match = await _openInvoices.FindOpenInvoiceAsync(companyDb, new OpenInvoiceQuery
            {
                CustomerDoc = source.DocPN,
                Serial = source.NumeroNota,
                Series = source.Serie,
                Model = source.Modelo,
                DocumentDate = source.DataDocumento != DateTime.MinValue ? source.DataDocumento : null
            }, cancellationToken);

            if (match == null)
            {
                line.ValidationMessage = Append(line.ValidationMessage,
                    $"Fatura em aberto não localizada (DocPN {source.DocPN}, Nota {source.NumeroNota}).");
                return false;
            }

            line.CardCode = match.CardCode;
            line.InvoiceDocEntry = match.DocEntry;
            line.VoucherNum = match.VoucherNum;
            line.BplId = match.BplId;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver fatura em aberto para nota {Nota} em {CompanyDb}.", source.NumeroNota, companyDb);
            line.ValidationMessage = Append(line.ValidationMessage, $"Erro ao consultar fatura no SAP: {ex.Message}");
            return false;
        }
    }

    public Task<SettlementConfirmResult> ConfirmAsync(long settlementFileId, CancellationToken cancellationToken = default)
        => ConfirmInternalAsync(settlementFileId, cancellationToken);

    public Task<SettlementConfirmResult> ReprocessAsync(long settlementFileId, CancellationToken cancellationToken = default)
        => ConfirmInternalAsync(settlementFileId, cancellationToken);

    private async Task<SettlementConfirmResult> ConfirmInternalAsync(long settlementFileId, CancellationToken cancellationToken)
    {
        _ = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var file = await _repository.GetFileAsync(settlementFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Arquivo de baixa {settlementFileId} nao encontrado.");

        var correlationId = file.CorrelationId ?? _correlation.Current?.CorrelationId ?? Guid.NewGuid().ToString("N");

        var result = await _processor.ExecuteAsync(settlementFileId, cancellationToken);
        return new SettlementConfirmResult
        {
            SettlementFileId = settlementFileId,
            Accepted = true,
            IsAsync = false,
            CorrelationId = correlationId,
            SynchronousResult = result
        };
    }

    private static string Append(string? current, string message)
        => string.IsNullOrWhiteSpace(current) ? message : current + "; " + message;
}
