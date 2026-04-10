using FinancialImport.Application.Settings;
using FinancialImport.Domain.Constants;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Shared.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Data;

public sealed class DatabaseSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly PasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    private readonly ISystemSettingsService _settingsService;

    public DatabaseSeeder(
        AppDbContext dbContext,
        PasswordHasher hasher,
        IClock clock,
        IConfiguration configuration,
        ISystemSettingsService settingsService,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _clock = clock;
        _configuration = configuration;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);
        await SeedProfilesAsync(cancellationToken);
        await SeedGlobalAdminAsync(cancellationToken);
        await SeedDefaultSystemSettingsAsync(cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var permissions = new (string Code, string Name, string Group)[]
        {
            (PermissionCodes.ImportarLancamentos, "Importar Lancamentos", "Importacao"),
            (PermissionCodes.VisualizarHistorico, "Visualizar Historico", "Importacao"),
            (PermissionCodes.ReprocessarImportacao, "Reprocessar Importacao", "Importacao"),
            (PermissionCodes.TrocarCompany, "Trocar Company", "Empresa"),
            (PermissionCodes.GerenciarUsuarios, "Gerenciar Usuarios", "Administracao"),
            (PermissionCodes.GerenciarPerfis, "Gerenciar Perfis", "Administracao"),
            (PermissionCodes.GerenciarPermissoes, "Gerenciar Permissoes", "Administracao"),
            (PermissionCodes.VisualizarLogs, "Visualizar Logs", "Sistema"),
        };

        foreach (var (code, name, group) in permissions)
        {
            if (!await _dbContext.Permissions.AnyAsync(p => p.Code == code, cancellationToken))
            {
                _dbContext.Permissions.Add(new Permission
                {
                    Code = code,
                    Name = name,
                    Group = group,
                    IsActive = true
                });
                _logger.LogInformation("Permissao '{Code}' criada.", code);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedProfilesAsync(CancellationToken cancellationToken)
    {
        if (!await _dbContext.Profiles.AnyAsync(p => p.Name == "Administrador", cancellationToken))
        {
            var adminProfile = new Profile
            {
                Name = "Administrador",
                Description = "Perfil com acesso total ao sistema",
                IsActive = true
            };
            _dbContext.Profiles.Add(adminProfile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var allPermissions = await _dbContext.Permissions.Where(p => p.IsActive).ToListAsync(cancellationToken);
            foreach (var permission in allPermissions)
            {
                _dbContext.ProfilePermissions.Add(new ProfilePermission
                {
                    ProfileId = adminProfile.Id,
                    PermissionId = permission.Id
                });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Perfil 'Administrador' criado com {Count} permissoes.", allPermissions.Count);
        }

        if (!await _dbContext.Profiles.AnyAsync(p => p.Name == "Operador", cancellationToken))
        {
            var operatorProfile = new Profile
            {
                Name = "Operador",
                Description = "Perfil operacional para importacoes",
                IsActive = true
            };
            _dbContext.Profiles.Add(operatorProfile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var operatorPermissions = await _dbContext.Permissions
                .Where(p => p.IsActive && (
                    p.Code == PermissionCodes.ImportarLancamentos ||
                    p.Code == PermissionCodes.VisualizarHistorico ||
                    p.Code == PermissionCodes.TrocarCompany))
                .ToListAsync(cancellationToken);

            foreach (var permission in operatorPermissions)
            {
                _dbContext.ProfilePermissions.Add(new ProfilePermission
                {
                    ProfileId = operatorProfile.Id,
                    PermissionId = permission.Id
                });
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Perfil 'Operador' criado com {Count} permissoes.", operatorPermissions.Count);
        }
    }

    private async Task SeedGlobalAdminAsync(CancellationToken cancellationToken)
    {
        var adminLogin = _configuration["AdminSeed:Login"];
        var adminPassword = _configuration["AdminSeed:Password"];
        var adminEmail = _configuration["AdminSeed:Email"];
        var adminName = _configuration["AdminSeed:Name"];

        if (string.IsNullOrWhiteSpace(adminLogin) || string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogWarning("AdminSeed:Login ou AdminSeed:Password nao configurado. Seed de admin ignorado.");
            return;
        }

        if (string.IsNullOrWhiteSpace(adminEmail))
            adminEmail = $"{adminLogin}@financialimport.local";
        if (string.IsNullOrWhiteSpace(adminName))
            adminName = "Administrador Global";

        if (await _dbContext.Users.AnyAsync(u => u.Login == adminLogin, cancellationToken))
        {
            _logger.LogInformation("Usuario admin '{Login}' ja existe. Seed ignorado.", adminLogin);
            return;
        }

        var (hash, salt) = _hasher.HashPassword(adminPassword);

        var adminUser = new User
        {
            Login = adminLogin,
            Name = adminName,
            Email = adminEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = true,
            IsBlocked = false,
            IsGlobalAdmin = true,
            CreatedAt = _clock.Now,
            CreatedBy = "SYSTEM"
        };

        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var adminProfile = await _dbContext.Profiles.FirstOrDefaultAsync(p => p.Name == "Administrador", cancellationToken);
        if (adminProfile != null)
        {
            _dbContext.UserProfiles.Add(new UserProfile
            {
                UserId = adminUser.Id,
                ProfileId = adminProfile.Id
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Usuario admin global '{Login}' criado com sucesso.", adminLogin);
    }

    private async Task SeedDefaultSystemSettingsAsync(CancellationToken cancellationToken)
    {
        // Only seed rows that do not yet exist — never overwrite user-configured values.
        var existing = await _dbContext.SystemSettings
            .Select(s => s.Chave)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        var defaults = new List<SystemSetting>
        {
            // ── SAP Service Layer ────────────────────────────────────────────
            new() { Chave = "Sap:BaseUrl",              Categoria = "SAP",        TipoDado = "string",   Obrigatorio = true,  Descricao = "URL base da SAP Service Layer (ex: https://hana:50000/b1s/v1)" },
            new() { Chave = "Sap:UserName",             Categoria = "SAP",        TipoDado = "string",   Obrigatorio = true,  Descricao = "Usuario do SAP Business One" },
            new() { Chave = "Sap:Password",             Categoria = "SAP",        TipoDado = "password", Obrigatorio = true,  Descricao = "Senha do SAP Business One" },
            new() { Chave = "Sap:Language",             Categoria = "SAP",        TipoDado = "int",      Obrigatorio = false, Valor = "29",  Descricao = "Codigo de idioma SAP (29 = Portugues)" },
            new() { Chave = "Sap:IgnoreSslErrors",      Categoria = "SAP",        TipoDado = "bool",     Obrigatorio = false, Valor = "false", Descricao = "Ignorar erros de certificado SSL" },
            new() { Chave = "Sap:TimeoutSeconds",       Categoria = "SAP",        TipoDado = "int",      Obrigatorio = false, Valor = "180", Descricao = "Timeout de requisicao em segundos" },
            new() { Chave = "Sap:MaxRetryAttempts",     Categoria = "SAP",        TipoDado = "int",      Obrigatorio = false, Valor = "3",   Descricao = "Numero maximo de tentativas de reconexao" },
            new() { Chave = "Sap:SessionTimeoutMinutes",Categoria = "SAP",        TipoDado = "int",      Obrigatorio = false, Valor = "25",  Descricao = "Minutos de inatividade antes de renovar sessao SAP" },

            // ── Seguranca (JWT + Cookie) ─────────────────────────────────────
            new() { Chave = "Jwt:SecretKey",            Categoria = "Seguranca",  TipoDado = "password", Obrigatorio = true,  Descricao = "Chave secreta JWT (minimo 32 caracteres)" },
            new() { Chave = "Jwt:Issuer",               Categoria = "Seguranca",  TipoDado = "string",   Obrigatorio = false, Valor = "FinancialImport",        Descricao = "Emissor do token JWT" },
            new() { Chave = "Jwt:Audience",             Categoria = "Seguranca",  TipoDado = "string",   Obrigatorio = false, Valor = "FinancialImportClients", Descricao = "Audiencia do token JWT" },
            new() { Chave = "Jwt:ExpirationMinutes",    Categoria = "Seguranca",  TipoDado = "int",      Obrigatorio = false, Valor = "480",  Descricao = "Validade do token JWT em minutos" },
            new() { Chave = "Jwt:RefreshExpirationMinutes", Categoria = "Seguranca", TipoDado = "int",   Obrigatorio = false, Valor = "1440", Descricao = "Validade do refresh token em minutos" },
            new() { Chave = "Jwt:ClockSkewMinutes",     Categoria = "Seguranca",  TipoDado = "int",      Obrigatorio = false, Valor = "1",    Descricao = "Tolerancia de clock em minutos" },
            new() { Chave = "Cookie:ExpirationHours",   Categoria = "Seguranca",  TipoDado = "int",      Obrigatorio = false, Valor = "8",    Descricao = "Horas de validade do cookie de autenticacao" },

            // ── Importacao ───────────────────────────────────────────────────
            new() { Chave = "Import:MaxFileSizeBytes",   Categoria = "Importacao", TipoDado = "int",     Obrigatorio = false, Valor = "10485760",    Descricao = "Tamanho maximo do arquivo (bytes)" },
            new() { Chave = "Import:AllowedExtensions",  Categoria = "Importacao", TipoDado = "list",    Obrigatorio = false, Valor = ".csv,.txt,.xlsx", Descricao = "Extensoes permitidas (separadas por virgula)" },
            new() { Chave = "Import:MemoMaxLength",      Categoria = "Importacao", TipoDado = "int",     Obrigatorio = false, Valor = "254",         Descricao = "Tamanho maximo do memo principal" },
            new() { Chave = "Import:ReferenceMaxLength", Categoria = "Importacao", TipoDado = "int",     Obrigatorio = false, Valor = "27",          Descricao = "Tamanho maximo da referencia" },
            new() { Chave = "Import:LineMemoMaxLength",  Categoria = "Importacao", TipoDado = "int",     Obrigatorio = false, Valor = "50",          Descricao = "Tamanho maximo do historico de linha" },
            new() { Chave = "Import:JournalBalanceTolerance", Categoria = "Importacao", TipoDado = "string", Obrigatorio = false, Valor = "0.01",   Descricao = "Tolerancia de balanco do lancamento contabil" },
            new() { Chave = "Import:Dedup:IncludeSeqLancamento", Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir SeqLancamento na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeCompanyDb",     Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir CompanyDb na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeReference",     Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir Referencia na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeAccounts",      Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir contas contabeis na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeDates",         Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir datas na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeAmount",        Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir valor na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeMemo",          Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir memo na chave de deduplicacao" },
            new() { Chave = "Import:Dedup:IncludeBranch",        Categoria = "Importacao", TipoDado = "bool", Obrigatorio = false, Valor = "true",  Descricao = "Incluir filial na chave de deduplicacao" },

            // ── Layout ───────────────────────────────────────────────────────
            new() { Chave = "Layout:DefaultTipoLancLayout1", Categoria = "Layout", TipoDado = "string", Obrigatorio = false, Valor = "D", Descricao = "Tipo de lancamento padrao para o Layout 1 (D=Debito, C=Credito)" },

            // ── Mensageria (RabbitMQ) ────────────────────────────────────────
            new() { Chave = "RabbitMq:Enabled",          Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "false",                         Descricao = "Habilitar integracao com RabbitMQ" },
            new() { Chave = "RabbitMq:HostName",         Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "localhost",                      Descricao = "Host do servidor RabbitMQ" },
            new() { Chave = "RabbitMq:Port",             Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "5672",                           Descricao = "Porta AMQP do RabbitMQ" },
            new() { Chave = "RabbitMq:VirtualHost",      Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "/",                              Descricao = "VirtualHost do RabbitMQ" },
            new() { Chave = "RabbitMq:UserName",         Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Descricao = "Usuario do RabbitMQ" },
            new() { Chave = "RabbitMq:Password",         Categoria = "Mensageria", TipoDado = "password",Obrigatorio = false, Descricao = "Senha do RabbitMQ" },
            new() { Chave = "RabbitMq:ExchangeName",     Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.exchange",       Descricao = "Nome do exchange principal" },
            new() { Chave = "RabbitMq:DeadLetterExchangeName", Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.dlx",       Descricao = "Nome do Dead Letter Exchange" },
            new() { Chave = "RabbitMq:PrefetchCount",    Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "16",                             Descricao = "Mensagens em prefetch por consumidor" },
            new() { Chave = "RabbitMq:MaxRetryAttempts", Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "5",                              Descricao = "Tentativas maximas antes do DLQ" },
            new() { Chave = "RabbitMq:InitialRetryDelaySeconds", Categoria = "Mensageria", TipoDado = "int", Obrigatorio = false, Valor = "2",                         Descricao = "Atraso inicial entre tentativas (s)" },
            new() { Chave = "RabbitMq:RetryBackoffMultiplier",   Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "2",                      Descricao = "Multiplicador exponencial de backoff" },
            new() { Chave = "RabbitMq:MaxRetryDelaySeconds",     Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "300",                    Descricao = "Atraso maximo entre tentativas (s)" },
            new() { Chave = "RabbitMq:ConnectionRecoveryIntervalSeconds", Categoria = "Mensageria", TipoDado = "int", Obrigatorio = false, Valor = "10",  Descricao = "Intervalo de recuperacao de conexao (s)" },
            new() { Chave = "RabbitMq:NetworkRecoveryIntervalSeconds",    Categoria = "Mensageria", TipoDado = "int", Obrigatorio = false, Valor = "10",  Descricao = "Intervalo de recuperacao de rede (s)" },
            new() { Chave = "RabbitMq:UseSsl",                            Categoria = "Mensageria", TipoDado = "bool", Obrigatorio = false, Valor = "false", Descricao = "Usar SSL na conexao com RabbitMQ" },

            // ── Mensageria (RabbitMQ Channels) ──────────────────────────────
            new() { Chave = "RabbitMq:Channels:import.process.command:Queue",      Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.import-process",   Descricao = "Fila do comando de processamento de importacao" },
            new() { Chave = "RabbitMq:Channels:import.process.command:RoutingKey",  Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "import.process.command",           Descricao = "Routing key do comando de processamento" },
            new() { Chave = "RabbitMq:Channels:import.process.command:Durable",     Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "true",                            Descricao = "Fila duravel para processamento" },

            new() { Chave = "RabbitMq:Channels:import.reprocess.command:Queue",     Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.import-reprocess", Descricao = "Fila do comando de reprocessamento" },
            new() { Chave = "RabbitMq:Channels:import.reprocess.command:RoutingKey", Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "import.reprocess.command",        Descricao = "Routing key do comando de reprocessamento" },
            new() { Chave = "RabbitMq:Channels:import.reprocess.command:Durable",    Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "true",                            Descricao = "Fila duravel para reprocessamento" },

            new() { Chave = "RabbitMq:Channels:sap.dispatch.command:Queue",         Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.sap-dispatch",     Descricao = "Fila do comando de despacho SAP" },
            new() { Chave = "RabbitMq:Channels:sap.dispatch.command:RoutingKey",    Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "sap.dispatch.command",             Descricao = "Routing key do comando de despacho SAP" },
            new() { Chave = "RabbitMq:Channels:sap.dispatch.command:Durable",       Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "true",                            Descricao = "Fila duravel para despacho SAP" },

            new() { Chave = "RabbitMq:Channels:audit.write.command:Queue",          Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.audit-write",      Descricao = "Fila do comando de escrita de auditoria" },
            new() { Chave = "RabbitMq:Channels:audit.write.command:RoutingKey",     Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "audit.write.command",              Descricao = "Routing key do comando de auditoria" },
            new() { Chave = "RabbitMq:Channels:audit.write.command:Durable",        Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "true",                            Descricao = "Fila duravel para auditoria" },

            // ── Mensageria (Kafka) ───────────────────────────────────────────
            new() { Chave = "Kafka:Enabled",             Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "false",           Descricao = "Habilitar integracao com Kafka" },
            new() { Chave = "Kafka:BootstrapServers",    Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "localhost:9092",   Descricao = "Servidores bootstrap do Kafka" },
            new() { Chave = "Kafka:ClientId",            Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport",  Descricao = "Client ID do Kafka" },
            new() { Chave = "Kafka:ConsumerGroupId",     Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport",  Descricao = "Consumer Group ID do Kafka" },
            new() { Chave = "Kafka:LingerMs",            Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "20",               Descricao = "Linger antes de enviar batch (ms)" },
            new() { Chave = "Kafka:BatchSize",           Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "32768",            Descricao = "Tamanho do batch em bytes" },
            new() { Chave = "Kafka:EnableIdempotence",   Categoria = "Mensageria", TipoDado = "bool",   Obrigatorio = false, Valor = "true",             Descricao = "Habilitar producao idempotente" },
            new() { Chave = "Kafka:Acks",                Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "all",              Descricao = "Acks do Kafka (all, 1, 0)" },

            // ── Mensageria (Kafka Topics) ───────────────────────────────────
            new() { Chave = "Kafka:Topics:import.events:Topic",        Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.import-events",   Descricao = "Topic Kafka para eventos de importacao" },
            new() { Chave = "Kafka:Topics:import.events:Partitions",   Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "3",                               Descricao = "Particoes do topic de importacao" },

            new() { Chave = "Kafka:Topics:sap.events:Topic",           Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.sap-events",      Descricao = "Topic Kafka para eventos SAP" },
            new() { Chave = "Kafka:Topics:sap.events:Partitions",      Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "3",                               Descricao = "Particoes do topic SAP" },

            new() { Chave = "Kafka:Topics:security.events:Topic",      Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.security-events",  Descricao = "Topic Kafka para eventos de seguranca" },
            new() { Chave = "Kafka:Topics:security.events:Partitions", Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "3",                               Descricao = "Particoes do topic de seguranca" },

            new() { Chave = "Kafka:Topics:audit.events:Topic",         Categoria = "Mensageria", TipoDado = "string", Obrigatorio = false, Valor = "financialimport.audit-events",     Descricao = "Topic Kafka para eventos de auditoria" },
            new() { Chave = "Kafka:Topics:audit.events:Partitions",    Categoria = "Mensageria", TipoDado = "int",    Obrigatorio = false, Valor = "3",                               Descricao = "Particoes do topic de auditoria" },

            // ── Outbox dispatcher ────────────────────────────────────────────
            new() { Chave = "Outbox:Enabled",                Categoria = "Mensageria", TipoDado = "bool", Obrigatorio = false, Valor = "true", Descricao = "Habilitar o dispatcher de outbox" },
            new() { Chave = "Outbox:PollingIntervalSeconds",  Categoria = "Mensageria", TipoDado = "int",  Obrigatorio = false, Valor = "5",    Descricao = "Intervalo de polling do outbox em segundos" },
            new() { Chave = "Outbox:BatchSize",               Categoria = "Mensageria", TipoDado = "int",  Obrigatorio = false, Valor = "100",  Descricao = "Mensagens processadas por ciclo" },
            new() { Chave = "Outbox:MaxAttempts",             Categoria = "Mensageria", TipoDado = "int",  Obrigatorio = false, Valor = "10",   Descricao = "Tentativas maximas antes de mover para DLQ" },
            new() { Chave = "Outbox:ClaimTimeoutSeconds",     Categoria = "Mensageria", TipoDado = "int",  Obrigatorio = false, Valor = "120",  Descricao = "Timeout de reserva de mensagem em segundos" },

            // ── CORS (API) ───────────────────────────────────────────────────
            new() { Chave = "Cors:AllowedOrigins",       Categoria = "Seguranca",  TipoDado = "list",   Obrigatorio = false, Valor = "http://localhost:5000,https://localhost:7000", Descricao = "Origens permitidas pelo CORS (separadas por virgula)" },
        };

        int added = 0;
        foreach (var setting in defaults)
        {
            if (existing.Contains(setting.Chave)) continue;
            _dbContext.SystemSettings.Add(setting);
            added++;
        }

        if (added > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("{Count} configuracoes padrao inseridas em ConfiguracaoSistema.", added);
        }
        else
        {
            _logger.LogInformation("Configuracoes do sistema ja existem — seed ignorado.");
        }

        // Invalidate cache so new values are visible immediately
        _settingsService.InvalidateCache();
    }
}
