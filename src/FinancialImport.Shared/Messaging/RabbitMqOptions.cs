namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Strongly-typed configuration for the RabbitMQ publisher/consumer
/// stack. All runtime values must come from <c>appsettings.json</c> or
/// environment variables — never hardcoded.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Messaging:RabbitMq";

    public bool Enabled { get; set; }

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public string ExchangeName { get; set; } = "financialimport.exchange";
    public string DeadLetterExchangeName { get; set; } = "financialimport.dlx";

    public int PrefetchCount { get; set; } = 16;
    public int MaxRetryAttempts { get; set; } = 5;
    public int InitialRetryDelaySeconds { get; set; } = 2;
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int MaxRetryDelaySeconds { get; set; } = 300;

    public int ConnectionRecoveryIntervalSeconds { get; set; } = 10;
    public int NetworkRecoveryIntervalSeconds { get; set; } = 10;
    public bool UseSsl { get; set; }

    public Dictionary<string, RabbitMqChannelOptions> Channels { get; set; } = new();
}

public sealed class RabbitMqChannelOptions
{
    /// <summary>Queue name used by consumers.</summary>
    public string Queue { get; set; } = string.Empty;

    /// <summary>Routing key used when publishing.</summary>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>Optional dead letter queue override.</summary>
    public string? DeadLetterQueue { get; set; }

    /// <summary>Whether the queue should be declared as durable.</summary>
    public bool Durable { get; set; } = true;
}
