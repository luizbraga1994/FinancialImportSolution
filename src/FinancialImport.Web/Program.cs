using System.Text;
using FinancialImport.Application.DependencyInjection;
using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.Data;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Infrastructure.Security;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FinancialImport.Web.Context;
using FinancialImport.Web.Middleware;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

// --- API Controllers ---
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();

// --- JWT Authentication ---
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecretKey = jwtSection.GetValue<string>("SecretKey")
    ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

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
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// --- Authorization Policies ---
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PermissionCodes.ImportarLancamentos, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.ImportarLancamentos)));

    options.AddPolicy(PermissionCodes.VisualizarHistorico, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.VisualizarHistorico)));

    options.AddPolicy(PermissionCodes.ReprocessarImportacao, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.ReprocessarImportacao)));

    options.AddPolicy(PermissionCodes.TrocarCompany, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.TrocarCompany)));

    options.AddPolicy(PermissionCodes.GerenciarUsuarios, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.GerenciarUsuarios)));

    options.AddPolicy(PermissionCodes.GerenciarPerfis, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.GerenciarPerfis)));

    options.AddPolicy(PermissionCodes.GerenciarPermissoes, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.GerenciarPermissoes)));

    options.AddPolicy(PermissionCodes.VisualizarLogs, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim("global_admin", "true") ||
            ctx.User.HasClaim("permission", PermissionCodes.VisualizarLogs)));
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
        await db.Database.MigrateAsync();
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
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Financial Import API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
