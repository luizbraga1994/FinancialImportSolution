using FinancialImport.Application.DependencyInjection;
using FinancialImport.Application.Settings;
using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Infrastructure.Observability;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.WithProperty("Application", "FinancialImport.Web")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
        .Enrich.FromLogContext());

builder.Services.AddControllersWithViews();

// Cookie expiration comes from DB (Cookie:ExpirationHours). The default is 8h.
// IPostConfigureOptions<CookieAuthenticationOptions> cannot easily read async DB settings,
// so we use a reasonable default here and let admins adjust via the settings page.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12); // default; overridden after settings load
        options.SlidingExpiration = true;
        options.Cookie.MaxAge = TimeSpan.FromHours(12);
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var code in PermissionCodes.All)
    {
        options.AddPolicy(code, policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.HasClaim("global_admin", "True") ||
                ctx.User.HasClaim("global_admin", "true") ||
                ctx.User.HasClaim("permission", code)));
    }
});
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<FinancialImport.Application.Abstractions.IUserContext, HttpUserContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ICompanyContext, HttpCompanyContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ILoginAuditContextAccessor, HttpLoginAuditContextAccessor>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSapIntegration(builder.Configuration);
builder.Services.AddHanaIntegration(builder.Configuration);

// Background workers (outbox dispatcher + import processor). They
// honor the Messaging options — if RabbitMQ is disabled they simply
// stay idle and no broker connection is attempted.
builder.Services.AddFinancialImportWorkers();

// Health checks — db ping + a placeholder for the broker state.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // ── DIAGNÓSTICO DE CONEXÃO ────────────────────────────────────────────────
    // Mostra, a partir da conexão REAL do app, qual servidor/porta/banco ele está
    // usando e se a tabela Usuarios que ele enxerga tem a coluna Login. Serve para
    // provar, sem achismo, contra qual banco o app fala. Nunca imprime a senha.
    try
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        await using var diag = conn.CreateCommand();
        diag.CommandText =
            "SELECT @@hostname, @@port, DATABASE(), @@lower_case_table_names, " +
            "(SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuarios' AND COLUMN_NAME = 'Login'), " +
            "(SELECT COUNT(*) FROM information_schema.TABLES  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuarios'), " +
            "(SELECT COUNT(*) FROM `__EFMigrationsHistory`);";
        await using var r = await diag.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            logger.LogWarning(
                "DIAGNÓSTICO CONEXÃO → DataSource={DataSource} | hostname={Hostname} port={Port} database={Database} lctn={Lctn} | UsuariosExiste={UsuariosExiste} UsuariosTemLogin={TemLogin} | MigrationsHistory={History}",
                conn.DataSource, r.GetValue(0), r.GetValue(1), r.GetValue(2), r.GetValue(3),
                r.GetValue(5), r.GetValue(4), r.GetValue(6));
        }
    }
    catch (Exception diagEx)
    {
        logger.LogError(diagEx, "DIAGNÓSTICO CONEXÃO falhou.");
    }
    // ──────────────────────────────────────────────────────────────────────────

    try
    {
        // Self-heal: se o schema base já existe mas o __EFMigrationsHistory está
        // vazio/desatualizado (banco criado fora do EF, restaurado, ou meia-aplicado),
        // reconcilia o histórico antes de migrar para evitar o erro
        // "Table 'usuarios' already exists".
        await MigrationBaseline.EnsureBaselineAsync(db, logger);

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("No pending migrations.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply migrations.");
        throw;
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();

    // Preload DB-driven settings cache so IConfigureOptions<T> adapters resolve correctly
    var sysSettings = app.Services.GetRequiredService<ISystemSettingsService>();
    await sysSettings.PreloadCacheAsync();
}

// Declare RabbitMQ topology (exchanges, queues, DLX) once at startup.
app.Services.ProvisionMessagingTopology();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.IncludeQueryInRequestPath = true;
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (ex != null) return Serilog.Events.LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 500) return Serilog.Events.LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400) return Serilog.Events.LogEventLevel.Warning;
        return Serilog.Events.LogEventLevel.Information;
    };
});

//// NÃO redireciona para HTTPS quando for desafio do Let's Encrypt/ACME
//app.UseWhen(
//    context => !context.Request.Path.StartsWithSegments("/.well-known/acme-challenge"),
//    appBuilder =>
//    {
//        appBuilder.UseHttpsRedirection();
//    });

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

app.Run();