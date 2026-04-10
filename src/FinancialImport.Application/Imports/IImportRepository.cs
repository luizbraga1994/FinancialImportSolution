using FinancialImport.Domain.Entities;

namespace FinancialImport.Application.Imports;

public interface IImportRepository
{
    Task<ImportFile?> GetImportFileAsync(long id, CancellationToken cancellationToken = default);

    Task<ImportFile?> GetImportFileWithLinesAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> ExistsFileHashAsync(string companyDb, string fileHash, CancellationToken cancellationToken = default);

    Task<bool> ExistsBusinessKeyAsync(string companyDb, string businessKeyHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of business keys that ALREADY exist in the
    /// database for the given company. This replaces the previous N+1
    /// loop with a single set-based lookup.
    /// </summary>
    Task<IReadOnlySet<string>> GetExistingBusinessKeysAsync(
        string companyDb,
        IReadOnlyCollection<string> businessKeyHashes,
        CancellationToken cancellationToken = default);

    Task<long> AddImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default);

    Task AddImportLinesAsync(IEnumerable<ImportLine> lines, CancellationToken cancellationToken = default);

    Task UpdateImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default);

    Task RemoveLinesForFileAsync(long importFileId, CancellationToken cancellationToken = default);
}
