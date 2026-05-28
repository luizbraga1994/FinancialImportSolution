using FinancialImport.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Settings;

// Allows appsettings.json / environment variables to override the DB-backed
// options after IConfigureOptions<T> has run. Useful for disabling brokers
// in development without touching the database.

public sealed class RabbitMqConfigOverride : IPostConfigureOptions<RabbitMqOptions>
{
    private readonly IConfiguration _configuration;

    public RabbitMqConfigOverride(IConfiguration configuration) => _configuration = configuration;

    public void PostConfigure(string? name, RabbitMqOptions options)
    {
        var section = _configuration.GetSection("RabbitMq");
        if (!section.Exists()) return;

        if (bool.TryParse(section["Enabled"], out var enabled))
            options.Enabled = enabled;
    }
}

public sealed class KafkaConfigOverride : IPostConfigureOptions<KafkaOptions>
{
    private readonly IConfiguration _configuration;

    public KafkaConfigOverride(IConfiguration configuration) => _configuration = configuration;

    public void PostConfigure(string? name, KafkaOptions options)
    {
        var section = _configuration.GetSection("Kafka");
        if (!section.Exists()) return;

        if (bool.TryParse(section["Enabled"], out var enabled))
            options.Enabled = enabled;
    }
}
