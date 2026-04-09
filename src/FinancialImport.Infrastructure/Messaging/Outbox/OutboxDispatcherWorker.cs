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
                            throw new InvalidOperationException("RabbitMQ publisher is not enabled.");
                        rabbit.Publish(message);
                        break;
                    case "kafka":
                        if (kafka == null || !kafka.IsEnabled)
                            throw new InvalidOperationException("Kafka producer is not enabled.");
                        await kafka.PublishAsync(message, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown broker '{message.Broker}'.");
                }

                await repo.MarkDispatchedAsync(message.Id, cancellationToken);

                await audit.WriteAsync(new AuditLogEntry
                {
                    Level = LogSeverities.Info,
                    Category = LogCategories.Messaging,
                    Source = nameof(OutboxDispatcherWorker),
                    Operation = "Dispatch",
                    Message = $"Outbox message dispatched to {message.Broker}.",
                    MessageId = message.MessageId,
                    CorrelationId = message.CorrelationId,
                    CausationId = message.CausationId,
                    CompanyDb = message.CompanyDb
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                var delay = ComputeBackoffDelay(message.AttemptCount);
                _logger.LogWarning(ex,
                    "Outbox dispatch failed message={MessageId} attempt={Attempt} nextAttemptInSec={Delay}",
                    message.MessageId, message.AttemptCount, delay);

                if (message.AttemptCount >= _options.MaxAttempts)
                {
                    await repo.MarkDeadLetteredAsync(message.Id, ex.Message, cancellationToken);
                }
                else
                {
                    await repo.MarkFailedAsync(message.Id, ex.Message, delay, cancellationToken);
                }

                await audit.WriteAsync(new AuditLogEntry
                {
                    Level = LogSeverities.Error,
                    Category = LogCategories.Messaging,
                    Source = nameof(OutboxDispatcherWorker),
                    Operation = "Dispatch",
                    Message = $"Outbox dispatch failed: {ex.Message}",
                    Details = ex.ToString(),
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
}
