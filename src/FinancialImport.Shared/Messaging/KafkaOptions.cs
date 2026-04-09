namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Strongly-typed configuration for the Kafka producer/consumer stack.
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Messaging:Kafka";

    public bool Enabled { get; set; }

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ClientId { get; set; } = "financialimport";
    public string ConsumerGroupId { get; set; } = "financialimport-workers";

    public string? SecurityProtocol { get; set; }
    public string? SaslMechanism { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    public int LingerMs { get; set; } = 20;
    public int BatchSize { get; set; } = 32_768;
    public int MaxInFlightRequests { get; set; } = 5;
    public bool EnableIdempotence { get; set; } = true;
    public string Acks { get; set; } = "all";

    /// <summary>Mapping between logical channel names and topic names.</summary>
    public Dictionary<string, KafkaTopicOptions> Topics { get; set; } = new();
}

public sealed class KafkaTopicOptions
{
    public string Topic { get; set; } = string.Empty;
    public int Partitions { get; set; } = 3;
    public short ReplicationFactor { get; set; } = 1;
    public bool AutoCreate { get; set; } = true;
}
