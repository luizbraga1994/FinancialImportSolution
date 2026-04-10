using FinancialImport.Application.Imports;
using FinancialImport.Application.Messaging;
using FinancialImport.Infrastructure.Messaging.RabbitMq;
using FinancialImport.Shared.Logging;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Workers;

/// <summary>
/// RabbitMQ consumer that processes <see cref="ProcessImportCommand"/>
/// messages. Uses the idempotent <see cref="IImportProcessor"/> so
/// retries are safe.
/// </summary>
public sealed class ImportProcessWorker : RabbitMqConsumerBase<ProcessImportCommand>
{
    private readonly ILogger<ImportProcessWorker> _typedLogger;

    public ImportProcessWorker(
        IServiceProvider services,
        RabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<ImportProcessWorker> logger)
        : base(MessagingChannels.RabbitMq.ImportProcessCommand, services, connectionFactory, options, logger)
    {
        _typedLogger = logger;
    }

    protected override string ConsumerName => "import-process-worker";

    protected override async Task HandleAsync(
        MessageEnvelope<ProcessImportCommand> envelope,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var processor = scopedServices.GetRequiredService<IImportProcessor>();
        var audit = scopedServices.GetRequiredService<IAuditLogger>();
        var cmd = envelope.Payload;

        _typedLogger.LogInformation(
            "ImportProcessWorker processing file={FileId} correlation={CorrelationId}",
            cmd.ImportFileId, envelope.CorrelationId);

        try
        {
            var result = await processor.ExecuteAsync(cmd.ImportFileId, cancellationToken);
            await audit.WriteAsync(new AuditLogEntry
            {
                Level = result.SapErrors == 0 ? LogSeverities.Info : LogSeverities.Warning,
                Category = LogCategories.Messaging,
                Source = nameof(ImportProcessWorker),
                Operation = "ProcessImportCommand",
                Message = $"File processed asynchronously: imported={result.Imported} sapErrors={result.SapErrors}",
                ImportFileId = cmd.ImportFileId,
                UserId = cmd.UserId,
                CompanyDb = cmd.CompanyDb,
                CorrelationId = envelope.CorrelationId,
                MessageId = envelope.MessageId,
                DurationMs = result.DurationMs,
                StatusAfter = result.Status
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await audit.WriteAsync(new AuditLogEntry
            {
                Level = LogSeverities.Error,
                Category = LogCategories.Messaging,
                Source = nameof(ImportProcessWorker),
                Operation = "ProcessImportCommand",
                Message = $"Processing failed: {ex.Message}",
                Details = ex.ToString(),
                ImportFileId = cmd.ImportFileId,
                UserId = cmd.UserId,
                CompanyDb = cmd.CompanyDb,
                CorrelationId = envelope.CorrelationId,
                MessageId = envelope.MessageId
            }, cancellationToken);
            throw;
        }
    }
}
