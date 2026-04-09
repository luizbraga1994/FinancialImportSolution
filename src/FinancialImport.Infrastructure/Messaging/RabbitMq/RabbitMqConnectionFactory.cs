using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FinancialImport.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Lazily creates a single shared <see cref="IConnection"/> wrapper for
/// the whole process. Uses automatic recovery (native RabbitMQ.Client
/// feature) and recreates the connection on failure. When RabbitMQ is
/// disabled in configuration, <see cref="GetConnection"/> throws so
/// callers can skip the messaging pipeline gracefully.
/// </summary>
public sealed class RabbitMqConnectionFactory : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionFactory> _logger;
    private readonly object _lock = new();
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionFactory(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public IConnection GetConnection()
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("RabbitMQ is disabled in configuration.");

        if (_connection is { IsOpen: true }) return _connection;

        lock (_lock)
        {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds),
                DispatchConsumersAsync = true,
                Ssl = new SslOption { Enabled = _options.UseSsl, ServerName = _options.HostName }
            };

            _connection = factory.CreateConnection(clientProvidedName: "FinancialImport");
            _connection.ConnectionShutdown += (_, args) =>
                _logger.LogWarning("RabbitMQ connection shutdown: {Reason}", args.ReplyText);

            _logger.LogInformation("RabbitMQ connection established to {Host}:{Port}{Vhost}",
                _options.HostName, _options.Port, _options.VirtualHost);

            return _connection;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _connection?.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing RabbitMQ connection."); }
        _connection?.Dispose();
    }
}
