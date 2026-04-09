namespace FinancialImport.Shared.Correlation;

/// <summary>
/// Accessor that exposes the ambient <see cref="CorrelationContext"/> for the
/// current logical operation. Implementations must use <see cref="AsyncLocal{T}"/>
/// semantics so the context flows across awaits and background work queued from
/// the same logical unit of work.
/// </summary>
public interface ICorrelationContextAccessor
{
    CorrelationContext? Current { get; }

    IDisposable Push(CorrelationContext context);
}
