using FinancialImport.Application.Abstractions;
using FinancialImport.Application.Idempotency;
using FinancialImport.Application.Imports;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Messaging;
using FinancialImport.Application.Outbox;
using FinancialImport.Application.Sap;
using FinancialImport.Application.Security;
using FinancialImport.Application.Settings;
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
using FinancialImport.Infrastructure.Settings;
using FinancialImport.Infrastructure.Workers;
using FinancialImport.Integration.Sap.Options;
using FinancialImport.Shared.Abstractions;
using FinancialImport.Shared.Imports;
using FinancialImport.Shared.Logging;
using FinancialImport.Shared.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // --- System settings service (singleton, in-memory cache, DB-backed) ---
        services.AddSingleton<ISystemSettingsService, DbSystemSettingsService>();

        // --- DB-backed IConfigureOptions<T> for all option classes ---
        // HANA stays in appsettings.json (section HanaDbConnection) since it is the
        // discovery source consulted before user login. Everything else lives in DB.
        services.AddSingleton<IConfigureOptions<SapServiceLayerOptions>, DbConfigureSapOptions>();
        services.AddSingleton<IConfigureOptions<JwtOptions>, DbConfigureJwtOptions>();
        services.AddSingleton<IConfigureNamedOptions<JwtBearerOptions>, DbConfigureJwtBearerOptions>();
        services.AddSingleton<IConfigureOptions<RabbitMqOptions>, DbConfigureRabbitMqOptions>();
        services.AddSingleton<IConfigureOptions<KafkaOptions>, DbConfigureKafkaOptions>();
        services.AddSingleton<IPostConfigureOptions<RabbitMqOptions>, RabbitMqConfigOverride>();
        services.AddSingleton<IPostConfigureOptions<KafkaOptions>, KafkaConfigOverride>();
        services.AddSingleton<IConfigureOptions<ImportProcessingOptions>, DbConfigureImportOptions>();
        services.AddSingleton<IConfigureOptions<LayoutParsingOptions>, DbConfigureLayoutOptions>();
        services.AddSingleton<IConfigureOptions<OutboxOptions>, DbConfigureOutboxOptions>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection nao configurado em appsettings.json.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options
                .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
                // EF Core 9+ emits PendingModelChangesWarning at runtime
                // whenever the live model differs from the last
                // committed snapshot. We treat it as a design-time
                // concern only: the Migration class itself still drives
                // schema evolution via its [Migration] attribute, and we
                // don't want the check to spam startup logs or stop
                // production deploys. The warning still surfaces in
                // `dotnet ef migrations add`.
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

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

        try
        {
            var provisioner = provider.GetRequiredService<RabbitMqTopologyProvisioner>();
            provisioner.Provision();
        }
        catch (Exception ex)
        {
            // RabbitMQ unreachable at startup must NOT crash the app.
            // The outbox dispatcher will retry delivery later; topology
            // will be provisioned on the first successful connection.
            var logger = provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("FinancialImport.Messaging");
            logger.LogWarning(ex,
                "RabbitMQ topology provisioning failed (broker unreachable?). " +
                "The app will continue; the outbox dispatcher retries delivery automatically.");
        }

        return provider;
    }
}
