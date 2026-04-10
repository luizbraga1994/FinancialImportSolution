using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FinancialImport.Application.Idempotency;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Messaging.Kafka;

/// <summary>
/// Base class for Kafka consumers. Analogous to the RabbitMQ consumer
/// base, but with manual offset commits so a failure does not silently
/// lose events.
/// </summary>
public abstract class KafkaConsumerBase<TPayload> : BackgroundService
    where TPayload : class
{
    private readonly IServiceProvider _services;
    private readonly KafkaOptions _options;
    private readonly ILogger _logger;
    private readonly string _channelKey;

    protected KafkaConsumerBase(
        string channelKey,
        IServiceProvider services,
        IOptions<KafkaOptions> options,
        ILogger logger)
    {
        _channelKey = channelKey;
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected abstract string ConsumerName { get; }

    protected abstract Task HandleAsync(
        MessageEnvelope<TPayload> envelope,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => Run(stoppingToken), stoppingToken);
    }

    private async Task Run(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Kafka disabled — {Consumer} skipped.", ConsumerName);
            return;
        }

        if (!_options.Topics.TryGetValue(_channelKey, out var topicOptions))
        {
            _logger.LogWarning(
                "Kafka channel '{Channel}' not configured — {Consumer} will not start.",
                _channelKey, ConsumerName);
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            ClientId = $"{_options.ClientId}-{ConsumerName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config)
            .SetErrorHandler((_, err) => _logger.LogError(
                "Kafka consumer error: {Code} {Reason}", err.Code, err.Reason))
            .Build();

        consumer.Subscribe(topicOptions.Topic);
        _logger.LogInformation("{Consumer} subscribed to {Topic}", ConsumerName, topicOptions.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]>? cr;
            try
            {
                cr = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "{Consumer} consume error.", ConsumerName);
                continue;
            }

            try
            {
                await ProcessAsync(cr, stoppingToken);
                consumer.StoreOffset(cr);
                consumer.Commit(cr);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Consumer} failed to process message topic={Topic} partition={Partition} offset={Offset}",
                    ConsumerName, cr?.Topic, cr?.Partition.Value, cr?.Offset.Value);
                // Poison-pill policy: skip the message by committing the
                // offset so we don't block the partition forever. Real
                // dead-letter topic wiring is done at the producer side
                // (see SapDispatchFailed events for audit trail).
                if (cr != null)
                {
                    consumer.StoreOffset(cr);
                    consumer.Commit(cr);
                }
            }
        }

        consumer.Close();
    }

    private async Task ProcessAsync(ConsumeResult<string, byte[]> cr, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(cr.Message.Value);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<TPayload>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Kafka envelope.");

        using var scope = _services.CreateScope();

        var correlationAccessor = scope.ServiceProvider.GetRequiredService<ICorrelationContextAccessor>();
        using var _ = correlationAccessor.Push(new CorrelationContext
        {
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            UserId = envelope.UserId,
            CompanyDb = envelope.CompanyDb
        });

        var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        if (!await idempotency.TryRegisterAsync(ConsumerName, envelope.MessageId, cancellationToken))
        {
            _logger.LogDebug(
                "{Consumer} skipped duplicate message id={MessageId}", ConsumerName, envelope.MessageId);
            return;
        }

        await HandleAsync(envelope, scope.ServiceProvider, cancellationToken);
    }
}
