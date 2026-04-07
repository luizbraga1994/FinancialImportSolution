using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapSessionStore
{
    Task<SapSessionContext?> GetActiveSessionAsync(long userId, CancellationToken cancellationToken = default);
    Task UpsertSessionAsync(long userId, SapSessionContext session, CancellationToken cancellationToken = default);
    Task DeactivateSessionAsync(long userId, CancellationToken cancellationToken = default);
}
