using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Idempotency;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Outbox;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.Hashing;
using FinancialImport.Infrastructure.Imports;
using FinancialImport.Infrastructure.Messaging;
using FinancialImport.Infrastructure.Messaging.Idempotency;
using FinancialImport.Infrastructure.Messaging.Kafka;
using FinancialImport.Infrastructure.Messaging.Outbox;
using FinancialImport.Infrastructure.Messaging.RabbitMq;
using FinancialImport.Infrastructure.Observability;
using FinancialImport.Infrastructure.Sap;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Infrastructure.Workers;
using FinancialImport.Shared.Abstractions;
using FinancialImport.Shared.Logging;
using FinancialImport.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MySqlOptions>(configuration.GetSection("MySql"));
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetSection("MySql").GetValue<string>("ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection ou MySql:ConnectionString nao configurado.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // --- Core services ---
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<IHashService, Sha256HashService>();
        services.AddScoped<IApplicationAuthService, ApplicationAuthService>();
        services.AddScoped<ISapSessionStore, SapSessionStore>();

        // --- Imports pipeline ---
        services.AddScoped<IImportRepository, ImportRepository>();
        services.AddScoped<IImportFileReader, ImportFileReader>();
        services.AddScoped<BusinessKeyBuilder>();
        services.AddScoped<JournalEntryBuilder>();
        services.AddScoped<IImportProcessor, ImportProcessor>();
        services.AddScoped<IImportService, ImportService>();

        services.AddSingleton<JwtTokenService>();
        services.AddScoped<DatabaseSeeder>();

        // --- Observability ---
        services.AddScoped<IAuditLogger, DbAuditLogger>();

        // --- Messaging: serialization, outbox, idempotency ---
        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        // Single OutboxPublisher instance per scope that implements
        // both publisher APIs. Avoid two separate instances.
        services.AddScoped<OutboxPublisher>();
        services.AddScoped<IEventPublisher>(sp => sp.GetRequiredService<OutboxPublisher>());
        services.AddScoped<ICommandBus>(sp => sp.GetRequiredService<OutboxPublisher>());

        // --- Messaging: RabbitMQ ---
        services.AddSingleton<RabbitMqConnectionFactory>();
        services.AddSingleton<RabbitMqTopologyProvisioner>();
        services.AddSingleton<RabbitMqPublisher>();

        // --- Messaging: Kafka ---
        services.AddSingleton<KafkaProducer>();

        return services;
    }

    /// <summary>
    /// Registers the background workers that consume broker messages
    /// and dispatch the outbox. Invoked separately from Web/API so
    /// read-only APIs can skip worker startup if needed.
    /// </summary>
    public static IServiceCollection AddFinancialImportWorkers(this IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcherWorker>();
        services.AddHostedService<ImportProcessWorker>();
        return services;
    }

    /// <summary>
    /// Declares RabbitMQ exchanges/queues on startup so the first
    /// published message does not fail due to missing topology.
    /// </summary>
    public static IServiceProvider ProvisionMessagingTopology(this IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>();
        if (!options.Value.Enabled) return provider;

        var provisioner = provider.GetRequiredService<RabbitMqTopologyProvisioner>();
        provisioner.Provision();
        return provider;
    }
}
