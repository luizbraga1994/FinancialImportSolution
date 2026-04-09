using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FinancialImport.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Declares exchanges, queues, bindings and the dead-letter topology
/// required by the messaging pipeline. Idempotent — safe to invoke on
/// every startup. The DLX uses a dedicated exchange so poisoned
/// messages are isolated from the main traffic flow.
/// </summary>
public sealed class RabbitMqTopologyProvisioner
{
    private readonly RabbitMqOptions _options;
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqTopologyProvisioner> _logger;

    public RabbitMqTopologyProvisioner(
        IOptions<RabbitMqOptions> options,
        RabbitMqConnectionFactory connectionFactory,
        ILogger<RabbitMqTopologyProvisioner> logger)
    {
        _options = options.Value;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public void Provision()
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("RabbitMQ disabled — skipping topology provisioning.");
            return;
        }

        using var channel = _connectionFactory.GetConnection().CreateModel();

        // Main + dead letter exchanges
        channel.ExchangeDeclare(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        channel.ExchangeDeclare(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        foreach (var (key, channelOptions) in _options.Channels)
        {
            if (string.IsNullOrWhiteSpace(channelOptions.Queue))
            {
                _logger.LogWarning("Channel '{Channel}' has no queue configured — skipping.", key);
                continue;
            }

            var deadLetterQueue = channelOptions.DeadLetterQueue ?? channelOptions.Queue + ".dlq";
            var deadLetterRoutingKey = channelOptions.RoutingKey + ".dlq";

            // Main queue with DLX redirection
            var arguments = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = deadLetterRoutingKey
            };

            channel.QueueDeclare(
                queue: channelOptions.Queue,
                durable: channelOptions.Durable,
                exclusive: false,
                autoDelete: false,
                arguments: arguments);

            channel.QueueBind(
                queue: channelOptions.Queue,
                exchange: _options.ExchangeName,
                routingKey: channelOptions.RoutingKey);

            // Dead letter queue (no redirection)
            channel.QueueDeclare(
                queue: deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false);

            channel.QueueBind(
                queue: deadLetterQueue,
                exchange: _options.DeadLetterExchangeName,
                routingKey: deadLetterRoutingKey);

            _logger.LogInformation(
                "RabbitMQ topology provisioned: exchange={Exchange} queue={Queue} routingKey={RoutingKey} dlq={Dlq}",
                _options.ExchangeName, channelOptions.Queue, channelOptions.RoutingKey, deadLetterQueue);
        }
    }
}
