using FinancialImport.Application.Outbox;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Messaging.Kafka;
using FinancialImport.Infrastructure.Messaging.RabbitMq;
using FinancialImport.Shared.Logging;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Messaging.Outbox;

/// <summary>
/// Background worker that polls the MensagensOutbox table, claims a
/// batch of pending messages and publishes them to the appropriate
/// broker. Uses exponential backoff with a cap (<see cref="OutboxOptions"/>)
/// and records every failure on the LogSistema audit sink.
/// </summary>
public sealed class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly OutboxOptions _options;
    private readonly RabbitMqOptions _rabbitOptions;
    private readonly ILogger<OutboxDispatcherWorker> _logger;

    public OutboxDispatcherWorker(
        IServiceProvider services,
        IOptions<OutboxOptions> options,
        IOptions<RabbitMqOptions> rabbitOptions,
        ILogger<OutboxDispatcherWorker> logger)
    {
        _services = services;
        _options = options.Value;
        _rabbitOptions = rabbitOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox dispatcher disabled.");
            return;
        }

        _logger.LogInformation(
            "Outbox dispatcher started (interval={IntervalSec}s, batch={BatchSize}).",
            _options.PollingIntervalSeconds, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher loop failed — backing off.");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds * 2), stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();

        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var rabbit = scope.ServiceProvider.GetService<RabbitMqPublisher>();
        var kafka = scope.ServiceProvider.GetService<KafkaProducer>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var batch = await repo.ClaimPendingAsync(
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.ClaimTimeoutSeconds),
            cancellationToken);

        if (batch.Count == 0) return 0;

        foreach (var message in batch)
        {
            try
            {
                switch (message.Broker.ToLowerInvariant())
                {
                    case "rabbitmq":
                        if (rabbit == null || !_rabbitOptions.Enabled)
                        {
                            // Broker intentionally disabled — mark as dispatched so
                            // the outbox does not retry forever. The message was
                            // "delivered to nowhere" which is the correct behavior
                            // when the integration is turned off.
                            _logger.LogDebug(
                                "RabbitMQ disabled — auto-completing outbox message {Id}.", message.Id);
                            await repo.MarkDispatchedAsync(message.Id, cancellationToken);
                            continue;
                        }
                        rabbit.Publish(message);
                        break;
                    case "kafka":
                        if (kafka == null || !kafka.IsEnabled)
                        {
                            _logger.LogDebug(
                                "Kafka disabled — auto-completing outbox message {Id}.", message.Id);
                            await repo.MarkDispatchedAsync(message.Id, cancellationToken);
                            continue;
                        }
                        await kafka.PublishAsync(message, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown broker '{message.Broker}'.");
                }

                await repo.MarkDispatchedAsync(message.Id, cancellationToken);

                // Only log the interesting cases: when a broker is enabled and
                // actually delivered a message, OR when a message was auto-completed
                // because a broker is disabled (already logged above at Debug level).
                // Do NOT emit an audit row for every routine dispatch — that floods
                // the /Log page with noise like "Outbox dispatched to kafka" that
                // tells operators nothing actionable.
                var friendlyType = ShortenMessageType(message.MessageType);
                _logger.LogDebug(
                    "Mensagem {Type} id={MessageId} publicada no {Broker} (canal={Channel}).",
                    friendlyType, message.MessageId, message.Broker, message.Channel);
            }
            catch (Exception ex)
            {
                var delay = ComputeBackoffDelay(message.AttemptCount);
                var friendlyType = ShortenMessageType(message.MessageType);
                var willDeadLetter = message.AttemptCount >= _options.MaxAttempts;

                _logger.LogWarning(ex,
                    "Falha ao publicar {Type} id={MessageId} no {Broker} (tentativa {Attempt}{Max}). Proxima em {Delay}s.",
                    friendlyType, message.MessageId, message.Broker,
                    message.AttemptCount, willDeadLetter ? " — ultima" : $"/{_options.MaxAttempts}",
                    delay);

                if (willDeadLetter)
                {
                    await repo.MarkDeadLetteredAsync(message.Id, ex.Message, cancellationToken);
                }
                else
                {
                    await repo.MarkFailedAsync(message.Id, ex.Message, delay, cancellationToken);
                }

                var detailsBuilder = new System.Text.StringBuilder();
                detailsBuilder.AppendLine($"Tipo da mensagem: {friendlyType}");
                detailsBuilder.AppendLine($"Broker destino: {message.Broker}");
                detailsBuilder.AppendLine($"Canal: {message.Channel}");
                detailsBuilder.AppendLine($"Tentativa: {message.AttemptCount} de {_options.MaxAttempts}");
                detailsBuilder.AppendLine($"Proxima tentativa em: {delay}s");
                detailsBuilder.AppendLine();
                detailsBuilder.AppendLine("--- Exception ---");
                detailsBuilder.AppendLine(ex.ToString());

                await audit.WriteAsync(new AuditLogEntry
                {
                    Level = willDeadLetter ? LogSeverities.Error : LogSeverities.Warning,
                    Category = LogCategories.Messaging,
                    Source = nameof(OutboxDispatcherWorker),
                    Operation = willDeadLetter ? "DeadLetter" : "DispatchFailed",
                    Message = willDeadLetter
                        ? $"Mensagem {friendlyType} movida para dead-letter apos {message.AttemptCount} tentativas: {ex.Message}"
                        : $"Falha ao publicar {friendlyType} no {message.Broker} (tentativa {message.AttemptCount}): {ex.Message}",
                    Details = detailsBuilder.ToString(),
                    StackTrace = ex.StackTrace,
                    MessageId = message.MessageId,
                    CorrelationId = message.CorrelationId,
                    CausationId = message.CausationId,
                    CompanyDb = message.CompanyDb
                }, cancellationToken);
            }
        }

        return batch.Count;
    }

    private int ComputeBackoffDelay(int attemptCount)
    {
        var initial = Math.Max(1, _rabbitOptions.InitialRetryDelaySeconds);
        var multiplier = _rabbitOptions.RetryBackoffMultiplier <= 1 ? 2 : _rabbitOptions.RetryBackoffMultiplier;
        var max = Math.Max(initial, _rabbitOptions.MaxRetryDelaySeconds);

        var delay = initial * Math.Pow(multiplier, Math.Max(0, attemptCount - 1));
        return (int)Math.Min(delay, max);
    }

    /// <summary>
    /// Turns a full assembly-qualified message type into a readable label
    /// like "ImportProcessedEvent" so log lines are useful to operators.
    /// </summary>
    private static string ShortenMessageType(string? fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName)) return "(desconhecido)";
        var commaIdx = fullTypeName.IndexOf(',');
        var typePart = commaIdx > 0 ? fullTypeName[..commaIdx] : fullTypeName;
        var lastDot = typePart.LastIndexOf('.');
        return lastDot >= 0 ? typePart[(lastDot + 1)..] : typePart;
    }
}
