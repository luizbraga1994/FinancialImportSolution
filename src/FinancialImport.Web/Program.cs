using FinancialImport.Application.DependencyInjection;
using FinancialImport.Domain.Constants;
using FinancialImport.Infrastructure.DependencyInjection;
using FinancialImport.Integration.Hana.DependencyInjection;
using FinancialImport.Integration.Sap.DependencyInjection;
using FinancialImport.Web.Context;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add<FinancialImport.Web.Filters.CompanyRequiredFilter>();
    })
    .AddFluentValidationAutoValidation()
    .AddFluentValidationClientsideAdapters();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PermissionCodes.ImportarLancamentos, policy => policy.RequireClaim("permission", PermissionCodes.ImportarLancamentos));
    options.AddPolicy(PermissionCodes.VisualizarHistorico, policy => policy.RequireClaim("permission", PermissionCodes.VisualizarHistorico));
    options.AddPolicy(PermissionCodes.ReprocessarImportacao, policy => policy.RequireClaim("permission", PermissionCodes.ReprocessarImportacao));
    options.AddPolicy(PermissionCodes.TrocarCompany, policy => policy.RequireClaim("permission", PermissionCodes.TrocarCompany));
    options.AddPolicy(PermissionCodes.GerenciarUsuarios, policy => policy.RequireClaim("permission", PermissionCodes.GerenciarUsuarios));
    options.AddPolicy(PermissionCodes.GerenciarPerfis, policy => policy.RequireClaim("permission", PermissionCodes.GerenciarPerfis));
    options.AddPolicy(PermissionCodes.GerenciarPermissoes, policy => policy.RequireClaim("permission", PermissionCodes.GerenciarPermissoes));
    options.AddPolicy(PermissionCodes.VisualizarLogs, policy => policy.RequireClaim("permission", PermissionCodes.VisualizarLogs));
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<FinancialImport.Application.Abstractions.IUserContext, HttpUserContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ICompanyContext, HttpCompanyContext>();
builder.Services.AddScoped<FinancialImport.Application.Abstractions.ILoginAuditContextAccessor, HttpLoginAuditContextAccessor>();
builder.Services.AddScoped<FinancialImport.Web.Services.IImportFileReader, FinancialImport.Web.Services.CsvImportFileReader>();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSapIntegration(builder.Configuration);
builder.Services.AddHanaIntegration(builder.Configuration);

var app = builder.Build();

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
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
