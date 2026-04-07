using FinancialImport.Application.DependencyInjection;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FinancialImport.Web.Context;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

// --- MVC ---
builder.Services.AddControllersWithViews();

// --- Cookie Authentication ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// --- HttpContext and Context Accessors ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.IUserContext, HttpUserContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ICompanyContext, HttpCompanyContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ILoginAuditContextAccessor, HttpLoginAuditContextAccessor>();

// --- Layer Registration ---
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSapIntegration(builder.Configuration);
builder.Services.AddHanaIntegration(builder.Configuration);

var app = builder.Build();

// --- Auto-migrate and Seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Aplicando migrations automaticamente...");
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync();
        logger.LogInformation("Migrations aplicadas com sucesso.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Falha ao aplicar migrations. Tentando EnsureCreated...");
        await db.Database.EnsureCreatedAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// --- Middleware Pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
