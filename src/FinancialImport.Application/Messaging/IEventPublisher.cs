using FinancialImport.Shared.Messaging;

namespace FinancialImport.Application.Messaging;

/// <summary>
/// Publishes integration events to the event backbone (Kafka). The
/// default implementation wraps the payload in an <see cref="MessageEnvelope{T}"/>,
/// stamps the current correlation/causation IDs and persists a copy to
/// the transactional outbox before dispatch.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>
/// Dispatches asynchronous commands to RabbitMQ. Commands are persisted
/// in the outbox first, then the outbox dispatcher forwards them to the
/// broker, guaranteeing exactly-once-with-retries semantics.
/// </summary>
public interface ICommandBus
{
    Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, IAsyncCommand;
}
