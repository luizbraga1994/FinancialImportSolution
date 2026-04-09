namespace FinancialImport.Shared.Correlation;

/// <summary>
/// Immutable snapshot of the correlation identifiers that must flow
/// end-to-end through Web/API -> Application -> Messaging -> Workers -> SAP.
/// </summary>
public sealed record CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";
    public const string CausationHeaderName = "X-Causation-Id";

    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? CausationId { get; init; }
    public long? UserId { get; init; }
    public string? CompanyDb { get; init; }
    public string? TenantId { get; init; }

    public static CorrelationContext NewRoot() => new();

    public CorrelationContext Child() =>
        this with { CausationId = CorrelationId, CorrelationId = Guid.NewGuid().ToString("N") };
}
