using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

/// <summary>
/// Creates Incoming Payments (ORCT) in SAP Business One via the Service Layer,
/// settling outgoing invoices ("Baixa de Notas de Saída").
/// </summary>
public interface ISapIncomingPaymentService
{
    Task<SapResult> CreateIncomingPaymentAsync(SapSessionContext session, SapIncomingPayment payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the status (exists / cancelled) of an Incoming Payment by DocEntry,
    /// so a settlement that already has a dispatch can decide between skipping
    /// (still active) and re-launching (cancelled or gone).
    /// </summary>
    Task<IncomingPaymentStatus> GetIncomingPaymentStatusAsync(SapSessionContext session, int docEntry, CancellationToken cancellationToken = default);
}
