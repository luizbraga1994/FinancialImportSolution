using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

/// <summary>
/// Reads the SAP credit-card catalog (OCRC + OCRP) from HANA for a company,
/// used to resolve a settlement line's card brand into the SAP CreditCard code,
/// G/L account and payment-method code.
/// </summary>
public interface ISapCardCatalogService
{
    Task<SapCardCatalog> GetCardCatalogAsync(string companyDb, CancellationToken cancellationToken = default);
}
