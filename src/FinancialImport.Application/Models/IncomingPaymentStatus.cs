namespace FinancialImport.Application.Models;

/// <summary>
/// Status of an existing SAP Incoming Payment, used to decide whether a
/// settlement that already has a dispatch can be re-launched (when the payment
/// was cancelled / no longer exists) or must be skipped (still active).
/// </summary>
public sealed class IncomingPaymentStatus
{
    /// <summary>The payment exists in SAP.</summary>
    public bool Exists { get; init; }

    /// <summary>The payment exists but was cancelled (OINV/ORCT Cancelled = tYES).</summary>
    public bool Cancelled { get; init; }

    public bool IsSessionExpired { get; init; }
    public string? Error { get; init; }

    /// <summary>True when the existing payment is still active (exists and not cancelled).</summary>
    public bool IsActive => Exists && !Cancelled;

    public static IncomingPaymentStatus Active() => new() { Exists = true, Cancelled = false };
    public static IncomingPaymentStatus CancelledPayment() => new() { Exists = true, Cancelled = true };
    public static IncomingPaymentStatus NotFound() => new() { Exists = false };
    public static IncomingPaymentStatus SessionExpired() => new() { IsSessionExpired = true };
    public static IncomingPaymentStatus Failure(string error) => new() { Error = error };
}
