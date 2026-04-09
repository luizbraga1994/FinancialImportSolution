using FinancialImport.Shared.Correlation;
using Microsoft.AspNetCore.Http;

namespace FinancialImport.Infrastructure.Observability;

/// <summary>
/// Extracts or generates a correlation id for every incoming HTTP
/// request, stamps it on the response, and makes it available through
/// <see cref="ICorrelationContextAccessor"/> for the rest of the call
/// chain (services, Serilog enrichers, broker envelopes).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor accessor)
    {
        var correlationId =
            context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var header)
                && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : Guid.NewGuid().ToString("N");

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationContext.HeaderName))
                context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        long? userId = null;
        var userIdClaim = context.User?.FindFirst("user_id")?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && long.TryParse(userIdClaim, out var parsed))
            userId = parsed;

        var companyDb = context.User?.FindFirst("company_db")?.Value;

        using var _ = accessor.Push(new CorrelationContext
        {
            CorrelationId = correlationId,
            UserId = userId,
            CompanyDb = companyDb
        });

        await _next(context);
    }
}
