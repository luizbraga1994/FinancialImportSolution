using System.Text;
using Confluent.Kafka;
using FinancialImport.Domain.Entities;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Messaging.Kafka;

/// <summary>
/// Reliable Kafka producer used by the OutboxDispatcher worker. Enables
/// idempotent production by default so retries inside the OutboxDispatcher
/// do not create duplicate events on the topic, and uses acks=all for
/// durability.
/// </summary>
public sealed class KafkaProducer : IDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaProducer> _logger;
    private IProducer<string, byte[]>? _producer;
    private readonly object _lock = new();
    private bool _disposed;

    public KafkaProducer(IOptions<KafkaOptions> options, ILogger<KafkaProducer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("Kafka is disabled — cannot publish.");

        if (!_options.Topics.TryGetValue(message.Channel, out var topicOptions))
            throw new InvalidOperationException(
                $"No Kafka topic configured for channel '{message.Channel}'. " +
                "Add it under Messaging:Kafka:Topics in appsettings.");

        var producer = EnsureProducer();

        var kafkaMessage = new Message<string, byte[]>
        {
            // Partition by companyDb (or correlationId) so events for the
            // same tenant land on the same partition and are processed in
            // order by downstream consumers.
            Key = message.CompanyDb ?? message.CorrelationId ?? message.MessageId,
            Value = Encoding.UTF8.GetBytes(message.Payload),
            Headers = new Headers
            {
                { CorrelationContext.HeaderName, Encoding.UTF8.GetBytes(message.CorrelationId ?? string.Empty) },
                { CorrelationContext.CausationHeaderName, Encoding.UTF8.GetBytes(message.CausationId ?? string.Empty) },
                { "x-message-id", Encoding.UTF8.GetBytes(message.MessageId) },
                { "x-message-type", Encoding.UTF8.GetBytes(message.MessageType) },
                { "x-attempt-count", Encoding.UTF8.GetBytes(message.AttemptCount.ToString()) }
            }
        };

        var result = await producer.ProduceAsync(topicOptions.Topic, kafkaMessage, cancellationToken);

        _logger.LogDebug(
            "Kafka published {MessageType} id={MessageId} topic={Topic} partition={Partition} offset={Offset}",
            message.MessageType, message.MessageId, topicOptions.Topic, result.Partition.Value, result.Offset.Value);
    }

    private IProducer<string, byte[]> EnsureProducer()
    {
        if (_producer != null) return _producer;
        lock (_lock)
        {
            if (_producer != null) return _producer;

            var config = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                ClientId = _options.ClientId,
                LingerMs = _options.LingerMs,
                BatchSize = _options.BatchSize,
                MaxInFlight = _options.MaxInFlightRequests,
                EnableIdempotence = _options.EnableIdempotence,
                Acks = _options.Acks switch
                {
                    "0" or "none" => Acks.None,
                    "1" or "leader" => Acks.Leader,
                    _ => Acks.All
                }
            };

            ApplySaslIfConfigured(config);

            _producer = new ProducerBuilder<string, byte[]>(config)
                .SetErrorHandler((_, err) => _logger.LogError(
                    "Kafka producer error: {Code} {Reason}", err.Code, err.Reason))
                .Build();

            return _producer;
        }
    }

    private void ApplySaslIfConfigured(ClientConfig config)
    {
        if (string.IsNullOrWhiteSpace(_options.SecurityProtocol)) return;

        if (Enum.TryParse<SecurityProtocol>(_options.SecurityProtocol, ignoreCase: true, out var protocol))
            config.SecurityProtocol = protocol;

        if (!string.IsNullOrWhiteSpace(_options.SaslMechanism) &&
            Enum.TryParse<SaslMechanism>(_options.SaslMechanism, ignoreCase: true, out var mech))
            config.SaslMechanism = mech;

        if (!string.IsNullOrWhiteSpace(_options.SaslUsername))
            config.SaslUsername = _options.SaslUsername;
        if (!string.IsNullOrWhiteSpace(_options.SaslPassword))
            config.SaslPassword = _options.SaslPassword;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _producer?.Flush(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error flushing Kafka producer."); }
        _producer?.Dispose();
    }
}
