using System.Text;
using FinancialImport.Domain.Entities;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FinancialImport.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Low-level RabbitMQ publisher used by the OutboxDispatcher worker.
/// The OutboxDispatcher owns the retry loop, so this class is a thin
/// wrapper around <c>BasicPublish</c> that only worries about reliable
/// channel creation and proper header propagation.
/// </summary>
public sealed class RabbitMqPublisher : IDisposable
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IModel? _channel;
    private readonly object _lock = new();
    private bool _disposed;

    public RabbitMqPublisher(
        RabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public void Publish(OutboxMessage message)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("RabbitMQ is disabled — cannot publish.");

        if (!_options.Channels.TryGetValue(message.Channel, out var channelOptions))
            throw new InvalidOperationException(
                $"No RabbitMQ channel configured for '{message.Channel}'. " +
                "Add it under Messaging:RabbitMq:Channels in appsettings.");

        var model = EnsureChannel();

        var body = Encoding.UTF8.GetBytes(message.Payload);
        var props = model.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = message.MessageId;
        props.CorrelationId = message.CorrelationId ?? string.Empty;
        props.Type = message.MessageType;
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        props.Headers = new Dictionary<string, object>
        {
            [CorrelationContext.HeaderName] = message.CorrelationId ?? string.Empty,
            [CorrelationContext.CausationHeaderName] = message.CausationId ?? string.Empty,
            ["x-attempt-count"] = message.AttemptCount,
            ["x-original-company"] = message.CompanyDb ?? string.Empty
        };

        model.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: channelOptions.RoutingKey,
            mandatory: false,
            basicProperties: props,
            body: body);

        _logger.LogDebug(
            "Published {MessageType} id={MessageId} correlation={CorrelationId} to {Exchange}/{RoutingKey}",
            message.MessageType, message.MessageId, message.CorrelationId,
            _options.ExchangeName, channelOptions.RoutingKey);
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true }) return _channel;

        lock (_lock)
        {
            if (_channel is { IsOpen: true }) return _channel;

            _channel = _connectionFactory.GetConnection().CreateModel();
            _channel.ConfirmSelect();
            return _channel;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _channel?.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing RabbitMQ publisher channel."); }
        _channel?.Dispose();
    }
}
