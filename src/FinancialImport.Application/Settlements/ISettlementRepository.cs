using FinancialImport.Domain.Entities;

namespace FinancialImport.Application.Settlements;

public interface ISettlementRepository
{
    Task<ReceivableSettlementFile?> GetFileAsync(long id, CancellationToken cancellationToken = default);
    Task<ReceivableSettlementFile?> GetFileWithLinesAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> ExistsFileHashAsync(string companyDb, string fileHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of business keys that were ALREADY settled
    /// (status Settled) for the company, so they can be flagged as duplicates.
    /// </summary>
    Task<IReadOnlySet<string>> GetExistingBusinessKeysAsync(
        string companyDb,
        IReadOnlyCollection<string> businessKeyHashes,
        CancellationToken cancellationToken = default);

    Task UpdateFileAsync(ReceivableSettlementFile file, CancellationToken cancellationToken = default);
    Task RemoveLinesForFileAsync(long settlementFileId, CancellationToken cancellationToken = default);
}
