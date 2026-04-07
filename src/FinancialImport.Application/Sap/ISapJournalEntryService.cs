using FinancialImport.Application.Models;

namespace FinancialImport.Application.Sap;

public interface ISapJournalEntryService
{
    Task<SapResult> CreateJournalEntryAsync(SapSessionContext session, SapJournalEntry payload, CancellationToken cancellationToken = default);
}
