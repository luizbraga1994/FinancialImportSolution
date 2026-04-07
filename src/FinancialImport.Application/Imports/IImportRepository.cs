using FinancialImport.Domain.Entities;

namespace FinancialImport.Application.Imports;

public interface IImportRepository
{
    Task<ImportFile?> GetImportFileAsync(long id, CancellationToken cancellationToken = default);
    Task<bool> ExistsFileHashAsync(string companyDb, string fileHash, CancellationToken cancellationToken = default);
    Task<bool> ExistsBusinessKeyAsync(string companyDb, string businessKeyHash, CancellationToken cancellationToken = default);
    Task<long> AddImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default);
    Task AddImportLinesAsync(IEnumerable<ImportLine> lines, CancellationToken cancellationToken = default);
    Task UpdateImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default);
}
