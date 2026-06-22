using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

/// <summary>
/// Creates Incoming Payments (ORCT) in SAP Business One via the Service Layer,
/// settling outgoing invoices ("Baixa de Notas de Saída").
/// </summary>
public interface ISapIncomingPaymentService
{
    Task<SapResult> CreateIncomingPaymentAsync(SapSessionContext session, SapIncomingPayment payload, CancellationToken cancellationToken = default);
}
