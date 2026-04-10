namespace FinancialImport.Shared.Correlation;

public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContext?> Holder = new();

    public CorrelationContext? Current => Holder.Value;

    public IDisposable Push(CorrelationContext context)
    {
        var previous = Holder.Value;
        Holder.Value = context ?? throw new ArgumentNullException(nameof(context));
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly CorrelationContext? _previous;
        private bool _disposed;

        public Pop(CorrelationContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            Holder.Value = _previous;
            _disposed = true;
        }
    }
}
