using System.Text;
using FinancialImport.Api.Context;
using FinancialImport.Api.Middleware;
using FinancialImport.Application.DependencyInjection;
using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Infrastructure.Observability;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.WithProperty("Application", "FinancialImport.Api")
        .Enrich.FromLogContext());

builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();

// --- JWT Authentication ---
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecretKey = jwtSection.GetValue<string>("SecretKey")
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

if (jwtSecretKey.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey deve ter pelo menos 32 caracteres.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection.GetValue<string>("Issuer") ?? "FinancialImport",
            ValidAudience = jwtSection.GetValue<string>("Audience") ?? "FinancialImportClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ClockSkew = TimeSpan.FromMinutes(jwtSection.GetValue<int?>("ClockSkewMinutes") ?? 1)
        };
    });

// --- Authorization policies built from permission codes (no hardcode) ---
builder.Services.AddAuthorization(options =>
{
    foreach (var code in PermissionCodes.All)
    {
        options.AddPolicy(code, policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.HasClaim("global_admin", "true") ||
                ctx.User.HasClaim("permission", code)));
    }
});

// --- Swagger / OpenAPI ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Financial Import API",
        Version = "v1",
        Description = "API para importacao contabil e integracao com SAP Business One"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe o token JWT no formato: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWeb", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "https://localhost:7000", "http://localhost:5000" };

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.IUserContext, HttpUserContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ICompanyContext, HttpCompanyContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ILoginAuditContextAccessor, HttpLoginAuditContextAccessor>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSapIntegration(builder.Configuration);
builder.Services.AddHanaIntegration(builder.Configuration);
builder.Services.AddFinancialImportWorkers();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// --- Auto-migrate and Seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying database migrations...");
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "MigrateAsync failed; falling back to EnsureCreated.");
        await db.Database.EnsureCreatedAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

app.Services.ProvisionMessagingTopology();

// --- Middleware pipeline ---
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Financial Import API v1");
    options.RoutePrefix = "swagger";
});

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.UseCors("AllowWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
