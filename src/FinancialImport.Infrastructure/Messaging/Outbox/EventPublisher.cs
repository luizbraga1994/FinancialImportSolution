using System.Text.Json;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Outbox;
using FinancialImport.Domain.Entities;
using FinancialImport.Shared.Correlation;
using FinancialImport.Shared.Messaging;

namespace FinancialImport.Infrastructure.Messaging.Outbox;

/// <summary>
/// Default implementation of <see cref="IEventPublisher"/> and
/// <see cref="ICommandBus"/>. Both routes go through the transactional
/// outbox: the Application layer writes the event or command alongside
/// the business change, and the OutboxDispatcher background worker
/// relays it to the appropriate broker (Kafka for events, RabbitMQ for
/// commands) with retry and dead-letter support.
/// </summary>
public sealed class OutboxPublisher : IEventPublisher, ICommandBus
{
    private readonly IOutboxRepository _outbox;
    private readonly ICorrelationContextAccessor _correlation;

    public OutboxPublisher(IOutboxRepository outbox, ICorrelationContextAccessor correlation)
    {
        _outbox = outbox;
        _correlation = correlation;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        var channel = MessagingChannels.Kafka.ImportEvents;
        if (@event.EventType.StartsWith("sap.", StringComparison.OrdinalIgnoreCase))
            channel = MessagingChannels.Kafka.SapEvents;
        else if (@event.EventType.StartsWith("security.", StringComparison.OrdinalIgnoreCase))
            channel = MessagingChannels.Kafka.SecurityEvents;
        else if (@event.EventType.StartsWith("audit.", StringComparison.OrdinalIgnoreCase))
            channel = MessagingChannels.Kafka.AuditEvents;

        return EnqueueAsync(@event, channel, broker: "kafka", cancellationToken);
    }

    public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, IAsyncCommand
    {
        var channel = command.CommandType switch
        {
            "import.process"   => MessagingChannels.RabbitMq.ImportProcessCommand,
            "import.reprocess" => MessagingChannels.RabbitMq.ImportReprocessCommand,
            "sap.dispatch"     => MessagingChannels.RabbitMq.SapDispatchCommand,
            "audit.write"      => MessagingChannels.RabbitMq.AuditWriteCommand,
            _ => throw new InvalidOperationException(
                $"Unknown command type '{command.CommandType}'. Add a mapping in OutboxPublisher.")
        };

        return EnqueueAsync(command, channel, broker: "rabbitmq", cancellationToken);
    }

    private async Task EnqueueAsync<T>(T payload, string channel, string broker, CancellationToken cancellationToken)
        where T : class
    {
        var correlation = _correlation.Current;
        var envelope = new MessageEnvelope<T>
        {
            MessageType = typeof(T).FullName!,
            CorrelationId = correlation?.CorrelationId ?? Guid.NewGuid().ToString("N"),
            CausationId = correlation?.CausationId,
            UserId = correlation?.UserId,
            CompanyDb = correlation?.CompanyDb,
            OccurredAtUtc = DateTime.Now,
            Payload = payload
        };

        var outbox = new OutboxMessage
        {
            Channel = channel,
            MessageType = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName!,
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Payload = JsonSerializer.Serialize(envelope),
            Broker = broker,
            Status = OutboxMessageStatus.Pending,
            CreatedAtUtc = DateTime.Now,
            UserId = envelope.UserId,
            CompanyDb = envelope.CompanyDb
        };

        await _outbox.EnqueueAsync(outbox, cancellationToken);
    }
}
