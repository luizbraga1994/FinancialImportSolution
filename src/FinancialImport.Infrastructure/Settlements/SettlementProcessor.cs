using System.Text.Json;
using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Models;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Settings;
using FinancialImport.Application.Settlements;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Shared.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Idempotent SAP dispatcher for receivable settlements. Each settlement line
/// (one outgoing note) becomes one SAP Incoming Payment. A
/// <see cref="IncomingPaymentDispatch"/> row is created BEFORE the SAP call; the
/// unique index on (SettlementFileId, GroupKeyHash) guarantees a note is never
/// settled twice even under retries or crashes.
/// </summary>
public sealed class SettlementProcessor : ISettlementProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly ISettlementRepository _repository;
    private readonly ISapSessionStore _sapSessionStore;
    private readonly ISapCompanySessionService _sapSessionService;
    private readonly ISapIncomingPaymentService _sapService;
    private readonly IncomingPaymentBuilder _builder;
    private readonly IUserContext _userContext;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;
    private readonly ILogger<SettlementProcessor> _logger;

    public SettlementProcessor(
        AppDbContext dbContext,
        ISettlementRepository repository,
        ISapSessionStore sapSessionStore,
        ISapCompanySessionService sapSessionService,
        ISapIncomingPaymentService sapService,
        IncomingPaymentBuilder builder,
        IUserContext userContext,
        ISystemSettingsService settings,
        IAuditLogger audit,
        ILogger<SettlementProcessor> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sapSessionStore = sapSessionStore;
        _sapSessionService = sapSessionService;
        _sapService = sapService;
        _builder = builder;
        _userContext = userContext;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    public async Task<SettlementProcessResult> ExecuteAsync(long settlementFileId, CancellationToken cancellationToken = default)
    {
        var start = DateTime.Now;
        var userId = _userContext.UserId ?? throw new InvalidOperationException("Usuario nao autenticado.");

        var file = await _repository.GetFileWithLinesAsync(settlementFileId, cancellationToken)
            ?? throw new KeyNotFoundException($"Arquivo de baixa {settlementFileId} nao encontrado.");

        var sapSession = await EnsureSessionAsync(userId, file.CompanyDb, cancellationToken);

        file.Status = SettlementStatus.Processing;
        file.ProcessingStartedAtUtc = start;
        file.ProcessingCompletedAtUtc = null;
        await _repository.UpdateFileAsync(file, cancellationToken);

        var lines = file.Lines
            .Where(l => l.Status == SettlementLineStatus.Valid || l.Status == SettlementLineStatus.SapError)
            .OrderBy(l => l.Id)
            .ToList();

        var cardMappings = await _dbContext.CardBrandMappings
            .Where(m => m.CompanyDb == file.CompanyDb && m.IsActive)
            .ToListAsync(cancellationToken);

        int? series = int.TryParse(_settings.Get("Settlement:IncomingPaymentSeries"), out var s) ? s : null;

        int settled = 0, sapErrors = 0;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentStatus = await _dbContext.ReceivableSettlementFiles
                .AsNoTracking().Where(f => f.Id == settlementFileId).Select(f => f.Status)
                .FirstAsync(cancellationToken);
            if (currentStatus == SettlementStatus.Cancelled)
            {
                _logger.LogInformation("Baixa {FileId} cancelada pelo usuario apos {Settled} pagamentos.", settlementFileId, settled);
                break;
            }

            var groupKeyHash = line.GroupKeyHash ?? line.BusinessKeyHash;

            var existingDispatch = await _dbContext.IncomingPaymentDispatches
                .FirstOrDefaultAsync(d => d.SettlementFileId == file.Id && d.GroupKeyHash == groupKeyHash, cancellationToken);

            // Already dispatched once: verify in SAP whether the payment really
            // exists and is not cancelled. If it is still active, skip (inform).
            // If it was cancelled (or no longer exists), fall through to re-launch.
            if (existingDispatch is { Status: IncomingPaymentDispatchStatus.Dispatched })
            {
                if (!existingDispatch.SapDocEntry.HasValue)
                {
                    // Dispatched without a DocEntry is ambiguous — do not risk a
                    // double payment; keep it as settled and move on.
                    line.Status = SettlementLineStatus.Settled;
                    line.SapReturnMessage = "Já baixado (idempotente).";
                    settled++;
                    continue;
                }

                var (status, refreshedSession) = await VerifyExistingPaymentAsync(sapSession, existingDispatch.SapDocEntry.Value, file.CompanyDb, cancellationToken);
                sapSession = refreshedSession;

                if (status.IsActive)
                {
                    line.Status = SettlementLineStatus.Settled;
                    line.SapDocEntry = existingDispatch.SapDocEntry;
                    line.SapReturnMessage = $"Pagamento já existe no SAP (DocEntry {existingDispatch.SapDocEntry}).";
                    settled++;
                    continue;
                }

                if (!status.Exists || status.Cancelled)
                {
                    _logger.LogInformation(
                        "Pagamento {DocEntry} cancelado/inexistente no SAP — relançando baixa da nota {GroupKey}.",
                        existingDispatch.SapDocEntry, existingDispatch.GroupKey);
                    existingDispatch.SapDocEntry = null;
                    // fall through to re-launch below
                }
                else
                {
                    // Could not determine status (transient error) — be safe and skip.
                    line.Status = SettlementLineStatus.Settled;
                    line.SapDocEntry = existingDispatch.SapDocEntry;
                    line.SapReturnMessage = $"Pagamento já registrado (status SAP não confirmado: {status.Error}).";
                    settled++;
                    continue;
                }
            }

            var dispatch = existingDispatch ?? new IncomingPaymentDispatch
            {
                SettlementFileId = file.Id,
                CompanyDb = file.CompanyDb,
                GroupKeyHash = groupKeyHash,
                GroupKey = SettlementKeyBuilder.BuildGroupKeyLabel(line.CustomerDoc, line.InvoiceSerial, line.InvoiceSeries, line.InvoiceModel),
                Status = IncomingPaymentDispatchStatus.InFlight,
                AttemptCount = 1,
                CreatedAtUtc = DateTime.Now,
                LastAttemptAtUtc = DateTime.Now,
                CorrelationId = file.CorrelationId
            };

            if (existingDispatch != null)
            {
                dispatch.AttemptCount += 1;
                dispatch.LastAttemptAtUtc = DateTime.Now;
                dispatch.Status = IncomingPaymentDispatchStatus.InFlight;
            }
            else
            {
                await _dbContext.IncomingPaymentDispatches.AddAsync(dispatch, cancellationToken);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Dispatch já existe para grupo {GroupKey} — outro worker está tratando.", dispatch.GroupKey);
                continue;
            }

            // Build the payload (resolve the card mapping for card means).
            var cardMapping = !string.IsNullOrWhiteSpace(line.CardBrand)
                ? cardMappings.FirstOrDefault(m => m.BrandName.Equals(line.CardBrand, StringComparison.OrdinalIgnoreCase))
                : null;

            var build = _builder.Build(line, cardMapping, series);
            if (!build.IsValid)
            {
                dispatch.Status = IncomingPaymentDispatchStatus.Failed;
                dispatch.LastError = Truncate(build.Error, 2000);
                line.Status = SettlementLineStatus.SapError;
                line.SapReturnMessage = build.Error;
                sapErrors++;
                await _dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            SapResult sapResult;
            Exception? sapException = null;
            try
            {
                sapResult = await _sapService.CreateIncomingPaymentAsync(sapSession, build.Payload!, cancellationToken);
                if (sapResult.IsSessionExpired)
                {
                    var relogin = await _sapSessionService.SignInCompanyAsync(
                        file.CompanyDb, _settings.Get("Sap:UserName") ?? "", _settings.Get("Sap:Password") ?? "", cancellationToken);
                    if (relogin.Success)
                    {
                        sapSession = relogin.Session!;
                        sapResult = await _sapService.CreateIncomingPaymentAsync(sapSession, build.Payload!, cancellationToken);
                    }
                    else
                    {
                        _logger.LogError("Falha ao reautenticar no SAP para '{CompanyDb}': {Error}", file.CompanyDb, relogin.ErrorMessage);
                        sapResult = SapResult.Fail($"Falha ao reautenticar no SAP: {relogin.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                sapException = ex;
                _logger.LogError(ex, "Exceção ao baixar nota {GroupKey} (empresa {CompanyDb}).", dispatch.GroupKey, file.CompanyDb);
                sapResult = SapResult.Fail($"Erro de comunicacao: {ex.Message}");
            }

            if (sapResult.Success)
            {
                var docEntry = ExtractDocEntry(sapResult.RawResponse);
                dispatch.Status = IncomingPaymentDispatchStatus.Dispatched;
                dispatch.DispatchedAtUtc = DateTime.Now;
                dispatch.SapDocEntry = docEntry;
                dispatch.SapResponseSummary = Truncate(sapResult.RawResponse, 2000);
                dispatch.LastError = null;

                line.Status = SettlementLineStatus.Settled;
                line.SapReturnMessage = "OK";
                line.SapDocEntry = docEntry;
                settled++;
            }
            else
            {
                dispatch.Status = IncomingPaymentDispatchStatus.Failed;
                dispatch.LastError = Truncate(sapResult.ErrorMessage ?? "Erro SAP desconhecido", 2000);
                dispatch.SapResponseSummary = Truncate(sapResult.RawResponse, 2000);

                line.Status = SettlementLineStatus.SapError;
                line.SapReturnMessage = dispatch.LastError;
                sapErrors++;

                await _audit.WriteAsync(new AuditLogEntry
                {
                    Level = LogSeverities.Error,
                    Category = LogCategories.Integration,
                    Source = nameof(SettlementProcessor),
                    Operation = "DispatchIncomingPayment",
                    Message = $"SAP rejeitou a baixa da nota '{dispatch.GroupKey}': {dispatch.LastError}",
                    Details = sapResult.RawResponse ?? sapException?.ToString(),
                    StackTrace = sapException?.StackTrace,
                    CompanyDb = file.CompanyDb,
                    CorrelationId = file.CorrelationId,
                    BusinessKey = dispatch.GroupKeyHash,
                    StatusAfter = dispatch.Status.ToString()
                }, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _dbContext.Entry(file).ReloadAsync(cancellationToken);

        file.SettledLines = settled;
        file.LinesWithError = sapErrors;
        file.ProcessingCompletedAtUtc = DateTime.Now;

        if (file.Status != SettlementStatus.Cancelled)
        {
            file.Status = sapErrors > 0
                ? (settled > 0 ? SettlementStatus.PartiallyCompleted : SettlementStatus.Failed)
                : (settled > 0 ? SettlementStatus.Completed : SettlementStatus.Failed);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var duration = (long)(DateTime.Now - start).TotalMilliseconds;

        await _audit.WriteAsync(new AuditLogEntry
        {
            Level = file.Status == SettlementStatus.Failed ? LogSeverities.Error : sapErrors > 0 ? LogSeverities.Warning : LogSeverities.Info,
            Category = LogCategories.Integration,
            Source = nameof(SettlementProcessor),
            Operation = "ProcessSettlement",
            Message = $"Baixa '{file.OriginalFileName}' processada — {settled} baixada(s), {sapErrors} com erro. Status: {file.Status}.",
            CompanyDb = file.CompanyDb,
            CorrelationId = file.CorrelationId,
            DurationMs = duration,
            StatusAfter = file.Status.ToString()
        }, cancellationToken);

        return new SettlementProcessResult
        {
            SettlementFileId = settlementFileId,
            Settled = settled,
            Duplicated = file.DuplicatedLines,
            Invalid = file.InvalidLines,
            SapErrors = sapErrors,
            DurationMs = duration,
            Status = file.Status.ToString()
        };
    }

    private async Task<SapSessionContext> EnsureSessionAsync(long userId, string companyDb, CancellationToken cancellationToken)
    {
        var session = await _sapSessionStore.GetActiveSessionAsync(userId, cancellationToken);
        if (session != null && session.CompanyDb.Equals(companyDb, StringComparison.OrdinalIgnoreCase))
            return session;

        _logger.LogInformation("Sessão SAP ausente/diferente. Login automático em '{CompanyDb}'...", companyDb);
        var loginResult = await _sapSessionService.SignInCompanyAsync(
            companyDb, _settings.Get("Sap:UserName") ?? "", _settings.Get("Sap:Password") ?? "", cancellationToken);

        if (!loginResult.Success)
            throw new InvalidOperationException($"Nao foi possivel conectar ao SAP para '{companyDb}': {loginResult.ErrorMessage}");

        return loginResult.Session!;
    }

    /// <summary>
    /// Checks an existing Incoming Payment's status in SAP, re-logging in once if
    /// the session expired. Returns the status and the (possibly refreshed) session.
    /// </summary>
    private async Task<(IncomingPaymentStatus Status, SapSessionContext Session)> VerifyExistingPaymentAsync(
        SapSessionContext session, int docEntry, string companyDb, CancellationToken cancellationToken)
    {
        var status = await _sapService.GetIncomingPaymentStatusAsync(session, docEntry, cancellationToken);
        if (status.IsSessionExpired)
        {
            var relogin = await _sapSessionService.SignInCompanyAsync(
                companyDb, _settings.Get("Sap:UserName") ?? "", _settings.Get("Sap:Password") ?? "", cancellationToken);
            if (relogin.Success)
            {
                session = relogin.Session!;
                status = await _sapService.GetIncomingPaymentStatusAsync(session, docEntry, cancellationToken);
            }
            else
            {
                _logger.LogError("Falha ao reautenticar no SAP para '{CompanyDb}': {Error}", companyDb, relogin.ErrorMessage);
                status = IncomingPaymentStatus.Failure($"Falha ao reautenticar: {relogin.ErrorMessage}");
            }
        }
        return (status, session);
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
