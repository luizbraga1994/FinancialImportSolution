using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

/// <summary>
/// Resolves open outgoing invoices directly from SAP HANA, matching the fiscal
/// key (customer document + serial + series + model) provided by a settlement
/// line. Returns the SAP DocEntry/CardCode/VoucherNum needed to build the
/// Incoming Payment.
/// </summary>
public interface ISapOpenInvoiceService
{
    /// <summary>
    /// Finds the open invoice that matches the fiscal key within the given
    /// company database. Returns <c>null</c> when no invoice matches.
    /// Throws <see cref="InvalidOperationException"/> when the key is ambiguous
    /// (more than one match).
    /// </summary>
    Task<OpenInvoiceMatch?> FindOpenInvoiceAsync(string companyDb, OpenInvoiceQuery query, CancellationToken cancellationToken = default);
}
