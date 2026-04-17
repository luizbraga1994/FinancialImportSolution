using System.Text;
using FinancialImport.Application.Layouts;
using FinancialImport.Application.Settings;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Integration.Sap.Options;
using FinancialImport.Shared.Imports;
using FinancialImport.Shared.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinancialImport.Infrastructure.Settings;

// NOTE: HANA connection settings remain in appsettings.json (section HanaDbConnection)
// because HANA is the "discovery" source — we read the list of SAP companies from it
// before the user has even logged in, and the MySQL settings cache may not be fully
// loaded yet on fresh startup. All other integration settings (SAP, JWT, messaging,
// imports, layout) live in the ConfiguracaoSistema table and are edited via the UI.

// ---------------------------------------------------------------------------
// SAP Service Layer
// ---------------------------------------------------------------------------
public sealed class DbConfigureSapOptions : IConfigureOptions<SapServiceLayerOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureSapOptions(ISystemSettingsService s) => _s = s;

    public void Configure(SapServiceLayerOptions o)
    {
        o.BaseUrl                = _s.Get("Sap:BaseUrl") ?? "";
        o.UserName               = _s.Get("Sap:UserName") ?? "";
        o.Password               = _s.Get("Sap:Password") ?? "";
        o.Language               = int.TryParse(_s.Get("Sap:Language"), out var lang) ? lang : 29;
        o.IgnoreSslErrors        = bool.TryParse(_s.Get("Sap:IgnoreSslErrors"), out var ssl) && ssl;
        o.TimeoutSeconds         = int.TryParse(_s.Get("Sap:TimeoutSeconds"), out var ts) ? ts : 180;
        o.MaxRetryAttempts       = int.TryParse(_s.Get("Sap:MaxRetryAttempts"), out var ra) ? ra : 3;
        o.SessionTimeoutMinutes  = int.TryParse(_s.Get("Sap:SessionTimeoutMinutes"), out var stm) ? stm : 25;
    }
}

// ---------------------------------------------------------------------------
// JWT Bearer (API). JwtBearerOptions is a NAMED option indexed by the
// authentication scheme ("Bearer" by default), so we must implement
// IConfigureNamedOptions<T> and only configure when the name matches.
// ---------------------------------------------------------------------------
public sealed class DbConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureJwtBearerOptions(ISystemSettingsService s) => _s = s;

    public void Configure(string? name, JwtBearerOptions o)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        Configure(o);
    }

    public void Configure(JwtBearerOptions o)
    {
        var secretKey = _s.Get("Jwt:SecretKey") ?? "";
        if (secretKey.Length < 32)
            secretKey = new string('0', 32); // safe fallback; login will fail gracefully

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer      = _s.Get("Jwt:Issuer") ?? "FinancialImport",
            ValidAudience    = _s.Get("Jwt:Audience") ?? "FinancialImportClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew        = TimeSpan.FromMinutes(int.TryParse(_s.Get("Jwt:ClockSkewMinutes"), out var cs) ? cs : 1)
        };
    }
}

// ---------------------------------------------------------------------------
// JWT options (for JwtTokenService / sign-in)
// ---------------------------------------------------------------------------
public sealed class DbConfigureJwtOptions : IConfigureOptions<JwtOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureJwtOptions(ISystemSettingsService s) => _s = s;

    public void Configure(JwtOptions o)
    {
        o.SecretKey              = _s.Get("Jwt:SecretKey") ?? "";
        o.Issuer                 = _s.Get("Jwt:Issuer") ?? "FinancialImport";
        o.Audience               = _s.Get("Jwt:Audience") ?? "FinancialImportClients";
        o.ExpirationMinutes      = int.TryParse(_s.Get("Jwt:ExpirationMinutes"), out var em) ? em : 480;
        o.RefreshExpirationMinutes = int.TryParse(_s.Get("Jwt:RefreshExpirationMinutes"), out var rem) ? rem : 1440;
    }
}

// ---------------------------------------------------------------------------
// RabbitMQ
// ---------------------------------------------------------------------------
public sealed class DbConfigureRabbitMqOptions : IConfigureOptions<RabbitMqOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureRabbitMqOptions(ISystemSettingsService s) => _s = s;

    public void Configure(RabbitMqOptions o)
    {
        o.Enabled          = bool.TryParse(_s.Get("RabbitMq:Enabled"), out var en) && en;
        o.HostName         = _s.Get("RabbitMq:HostName") ?? "localhost";
        o.Port             = int.TryParse(_s.Get("RabbitMq:Port"), out var p) ? p : 5672;
        o.VirtualHost      = _s.Get("RabbitMq:VirtualHost") ?? "/";
        o.UserName         = _s.Get("RabbitMq:UserName") ?? "";
        o.Password         = _s.Get("RabbitMq:Password") ?? "";
        o.ExchangeName     = _s.Get("RabbitMq:ExchangeName") ?? "financialimport.exchange";
        o.DeadLetterExchangeName = _s.Get("RabbitMq:DeadLetterExchangeName") ?? "financialimport.dlx";
        o.PrefetchCount    = ushort.TryParse(_s.Get("RabbitMq:PrefetchCount"), out var pc) ? pc : (ushort)16;
        o.MaxRetryAttempts = int.TryParse(_s.Get("RabbitMq:MaxRetryAttempts"), out var mra) ? mra : 5;
        o.InitialRetryDelaySeconds  = int.TryParse(_s.Get("RabbitMq:InitialRetryDelaySeconds"), out var ird) ? ird : 2;
        o.RetryBackoffMultiplier    = double.TryParse(_s.Get("RabbitMq:RetryBackoffMultiplier"), out var rbm) ? rbm : 2.0;
        o.MaxRetryDelaySeconds      = int.TryParse(_s.Get("RabbitMq:MaxRetryDelaySeconds"), out var mrd) ? mrd : 300;
        o.ConnectionRecoveryIntervalSeconds = int.TryParse(_s.Get("RabbitMq:ConnectionRecoveryIntervalSeconds"), out var cri) ? cri : 10;
        o.NetworkRecoveryIntervalSeconds    = int.TryParse(_s.Get("RabbitMq:NetworkRecoveryIntervalSeconds"), out var nri) ? nri : 10;
        o.UseSsl           = bool.TryParse(_s.Get("RabbitMq:UseSsl"), out var ssl) && ssl;

        // --- Channel definitions ---
        // DB keys follow the pattern: RabbitMq:Channels:<channelKey>:<Property>
        // e.g. RabbitMq:Channels:import.process.command:Queue
        const string prefix = "RabbitMq:Channels:";
        var channelEntries = _s.GetByPrefix(prefix);

        foreach (var (key, value) in channelEntries)
        {
            // key = "RabbitMq:Channels:import.process.command:Queue"
            var remainder = key[prefix.Length..]; // "import.process.command:Queue"
            var colonIdx = remainder.LastIndexOf(':');
            if (colonIdx <= 0) continue;

            var channelKey = remainder[..colonIdx];    // "import.process.command"
            var property   = remainder[(colonIdx + 1)..]; // "Queue"

            if (!o.Channels.TryGetValue(channelKey, out var channelOpts))
            {
                channelOpts = new RabbitMqChannelOptions();
                o.Channels[channelKey] = channelOpts;
            }

            switch (property)
            {
                case "Queue":
                    channelOpts.Queue = value ?? string.Empty;
                    break;
                case "RoutingKey":
                    channelOpts.RoutingKey = value ?? string.Empty;
                    break;
                case "DeadLetterQueue":
                    channelOpts.DeadLetterQueue = value;
                    break;
                case "Durable":
                    channelOpts.Durable = !bool.TryParse(value, out var dur) || dur;
                    break;
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Kafka
// ---------------------------------------------------------------------------
public sealed class DbConfigureKafkaOptions : IConfigureOptions<KafkaOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureKafkaOptions(ISystemSettingsService s) => _s = s;

    public void Configure(KafkaOptions o)
    {
        o.Enabled           = bool.TryParse(_s.Get("Kafka:Enabled"), out var en) && en;
        o.BootstrapServers  = _s.Get("Kafka:BootstrapServers") ?? "localhost:9092";
        o.ClientId          = _s.Get("Kafka:ClientId") ?? "financialimport";
        o.ConsumerGroupId   = _s.Get("Kafka:ConsumerGroupId") ?? "financialimport";
        o.LingerMs          = int.TryParse(_s.Get("Kafka:LingerMs"), out var lm) ? lm : 20;
        o.BatchSize         = int.TryParse(_s.Get("Kafka:BatchSize"), out var bs) ? bs : 32768;
        o.EnableIdempotence = !bool.TryParse(_s.Get("Kafka:EnableIdempotence"), out var ei) || ei;
        o.Acks              = _s.Get("Kafka:Acks") ?? "all";
        o.SecurityProtocol  = _s.Get("Kafka:SecurityProtocol");
        o.SaslMechanism     = _s.Get("Kafka:SaslMechanism");
        o.SaslUsername       = _s.Get("Kafka:SaslUsername");
        o.SaslPassword       = _s.Get("Kafka:SaslPassword");

        // --- Topic definitions ---
        // DB keys follow the pattern: Kafka:Topics:<channelKey>:<Property>
        // e.g. Kafka:Topics:import.events:Topic
        const string prefix = "Kafka:Topics:";
        var topicEntries = _s.GetByPrefix(prefix);

        foreach (var (key, value) in topicEntries)
        {
            var remainder = key[prefix.Length..];
            var colonIdx = remainder.LastIndexOf(':');
            if (colonIdx <= 0) continue;

            var channelKey = remainder[..colonIdx];
            var property   = remainder[(colonIdx + 1)..];

            if (!o.Topics.TryGetValue(channelKey, out var topicOpts))
            {
                topicOpts = new KafkaTopicOptions();
                o.Topics[channelKey] = topicOpts;
            }

            switch (property)
            {
                case "Topic":
                    topicOpts.Topic = value ?? string.Empty;
                    break;
                case "Partitions":
                    topicOpts.Partitions = int.TryParse(value, out var p) ? p : 3;
                    break;
                case "ReplicationFactor":
                    topicOpts.ReplicationFactor = short.TryParse(value, out var rf) ? rf : (short)1;
                    break;
                case "AutoCreate":
                    topicOpts.AutoCreate = !bool.TryParse(value, out var ac) || ac;
                    break;
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Import processing
// ---------------------------------------------------------------------------
public sealed class DbConfigureImportOptions : IConfigureOptions<ImportProcessingOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureImportOptions(ISystemSettingsService s) => _s = s;

    public void Configure(ImportProcessingOptions o)
    {
        o.MaxFileSizeBytes   = long.TryParse(_s.Get("Import:MaxFileSizeBytes"), out var mfs) ? mfs : 10_485_760;
        o.MemoMaxLength      = int.TryParse(_s.Get("Import:MemoMaxLength"), out var mml) ? mml : 254;
        o.ReferenceMaxLength = int.TryParse(_s.Get("Import:ReferenceMaxLength"), out var rml) ? rml : 200;
        o.LineMemoMaxLength  = int.TryParse(_s.Get("Import:LineMemoMaxLength"), out var lml) ? lml : 254;
        o.JournalBalanceTolerance = decimal.TryParse(_s.Get("Import:JournalBalanceTolerance"), out var jbt) ? jbt : 0.01m;

        var ext = _s.Get("Import:AllowedExtensions");
        if (!string.IsNullOrWhiteSpace(ext))
            o.AllowedExtensions = ext.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();

        o.DeduplicationKey.IncludeSeqLancamento = !bool.TryParse(_s.Get("Import:Dedup:IncludeSeqLancamento"), out var isl) || isl;
        o.DeduplicationKey.IncludeCompanyDb   = !bool.TryParse(_s.Get("Import:Dedup:IncludeCompanyDb"), out var icd) || icd;
        o.DeduplicationKey.IncludeReference   = !bool.TryParse(_s.Get("Import:Dedup:IncludeReference"), out var ir) || ir;
        o.DeduplicationKey.IncludeAccounts    = !bool.TryParse(_s.Get("Import:Dedup:IncludeAccounts"), out var ia) || ia;
        o.DeduplicationKey.IncludeDates       = !bool.TryParse(_s.Get("Import:Dedup:IncludeDates"), out var id) || id;
        o.DeduplicationKey.IncludeAmount      = !bool.TryParse(_s.Get("Import:Dedup:IncludeAmount"), out var iam) || iam;
        o.DeduplicationKey.IncludeMemo        = !bool.TryParse(_s.Get("Import:Dedup:IncludeMemo"), out var im) || im;
        o.DeduplicationKey.IncludeBranch      = !bool.TryParse(_s.Get("Import:Dedup:IncludeBranch"), out var ib) || ib;
    }
}

// ---------------------------------------------------------------------------
// Layout parsing
// ---------------------------------------------------------------------------
public sealed class DbConfigureLayoutOptions : IConfigureOptions<LayoutParsingOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureLayoutOptions(ISystemSettingsService s) => _s = s;

    public void Configure(LayoutParsingOptions o)
    {
        o.DefaultTipoLancLayout1 = _s.Get("Layout:DefaultTipoLancLayout1") ?? "D";
    }
}

// ---------------------------------------------------------------------------
// Outbox dispatcher
// ---------------------------------------------------------------------------
public sealed class DbConfigureOutboxOptions : IConfigureOptions<OutboxOptions>
{
    private readonly ISystemSettingsService _s;
    public DbConfigureOutboxOptions(ISystemSettingsService s) => _s = s;

    public void Configure(OutboxOptions o)
    {
        o.Enabled                = !bool.TryParse(_s.Get("Outbox:Enabled"), out var en) || en;
        o.PollingIntervalSeconds = int.TryParse(_s.Get("Outbox:PollingIntervalSeconds"), out var pi) ? pi : 5;
        o.BatchSize              = int.TryParse(_s.Get("Outbox:BatchSize"), out var bs) ? bs : 100;
        o.MaxAttempts            = int.TryParse(_s.Get("Outbox:MaxAttempts"), out var ma) ? ma : 10;
        o.ClaimTimeoutSeconds    = int.TryParse(_s.Get("Outbox:ClaimTimeoutSeconds"), out var ct) ? ct : 120;
    }
}
