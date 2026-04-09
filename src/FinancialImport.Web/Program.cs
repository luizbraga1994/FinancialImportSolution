using FinancialImport.Application.DependencyInjection;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Infrastructure.Observability;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(
            builder.Configuration.GetValue<int?>("Security:Cookie:ExpirationHours") ?? 8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();
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

    try
    {
        logger.LogInformation("Applying database migrations...");
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync();
        logger.LogInformation("Migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "MigrateAsync failed; falling back to EnsureCreated.");
        await db.Database.EnsureCreatedAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// Declare RabbitMQ topology (exchanges, queues, DLX) once at startup.
app.Services.ProvisionMessagingTopology();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

app.Run();
