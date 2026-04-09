using System.Text;
using System.Text.Json;
using FinancialImport.Application.Idempotency;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinancialImport.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Reusable base class for RabbitMQ consumers. Handles:
/// <list type="bullet">
///   <item>queue discovery from <see cref="RabbitMqOptions.Channels"/></item>
///   <item>message envelope deserialization</item>
///   <item>correlation-id propagation into the async scope</item>
///   <item>idempotency check via <see cref="IIdempotencyStore"/></item>
///   <item>retry / DLQ routing via RabbitMQ's native dead-letter support</item>
/// </list>
/// Subclasses only need to implement <see cref="HandleAsync"/>.
/// </summary>
public abstract class RabbitMqConsumerBase<TPayload> : BackgroundService
    where TPayload : class
{
    private readonly IServiceProvider _services;
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger _logger;
    private readonly string _channelKey;
    private IModel? _channel;

    protected RabbitMqConsumerBase(
        string channelKey,
        IServiceProvider services,
        RabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger logger)
    {
        _channelKey = channelKey;
        _services = services;
        _connectionFactory = connectionFactory;
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
        if (!_options.Enabled)
        {
            _logger.LogInformation("RabbitMQ disabled — {Consumer} skipped.", ConsumerName);
            return Task.CompletedTask;
        }

        if (!_options.Channels.TryGetValue(_channelKey, out var channelOptions)
            || string.IsNullOrWhiteSpace(channelOptions.Queue))
        {
            _logger.LogWarning(
                "RabbitMQ channel '{Channel}' not configured — {Consumer} will not start.",
                _channelKey, ConsumerName);
            return Task.CompletedTask;
        }

        _channel = _connectionFactory.GetConnection().CreateModel();
        _channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)_options.PrefetchCount, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            var deliveryTag = ea.DeliveryTag;
            try
            {
                await HandleDeliveryAsync(ea, stoppingToken);
                _channel.BasicAck(deliveryTag, multiple: false);
            }
            catch (OperationCanceledException)
            {
                _channel.BasicNack(deliveryTag, multiple: false, requeue: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Consumer} failed to process message id={MessageId} — sending to DLQ.",
                    ConsumerName, ea.BasicProperties?.MessageId);
                // requeue=false => dead-letter exchange will pick it up thanks to DLX.
                _channel.BasicNack(deliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(
            queue: channelOptions.Queue,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation(
            "{Consumer} started on queue {Queue} (prefetch={Prefetch})",
            ConsumerName, channelOptions.Queue, _options.PrefetchCount);

        return Task.CompletedTask;
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(ea.Body.Span);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<TPayload>>(json)
            ?? throw new InvalidOperationException("Failed to deserialize message envelope.");

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
        var registered = await idempotency.TryRegisterAsync(ConsumerName, envelope.MessageId, cancellationToken);
        if (!registered)
        {
            _logger.LogInformation(
                "{Consumer} skipped duplicate message id={MessageId} correlation={CorrelationId}",
                ConsumerName, envelope.MessageId, envelope.CorrelationId);
            return;
        }

        await HandleAsync(envelope, scope.ServiceProvider, cancellationToken);
    }

    public override void Dispose()
    {
        try { _channel?.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing consumer channel for {Consumer}.", ConsumerName); }
        _channel?.Dispose();
        base.Dispose();
    }
}
