using FinancialImport.Application.Imports;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Imports;

public sealed class ImportRepository : IImportRepository
{
    private readonly AppDbContext _dbContext;

    public ImportRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ImportFile?> GetImportFileAsync(long id, CancellationToken cancellationToken = default)
        => _dbContext.ImportFiles
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<ImportFile?> GetImportFileWithLinesAsync(long id, CancellationToken cancellationToken = default)
        => _dbContext.ImportFiles
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<bool> ExistsFileHashAsync(string companyDb, string fileHash, CancellationToken cancellationToken = default)
        => _dbContext.ImportFiles.AnyAsync(i => i.CompanyDb == companyDb && i.FileHash == fileHash, cancellationToken);

    public Task<bool> ExistsBusinessKeyAsync(string companyDb, string businessKeyHash, CancellationToken cancellationToken = default)
        => _dbContext.ImportLines.AnyAsync(i => i.CompanyDb == companyDb && i.BusinessKeyHash == businessKeyHash, cancellationToken);

    public async Task<IReadOnlySet<string>> GetExistingBusinessKeysAsync(
        string companyDb,
        IReadOnlyCollection<string> businessKeyHashes,
        CancellationToken cancellationToken = default)
    {
        if (businessKeyHashes.Count == 0)
            return new HashSet<string>();

        var found = await _dbContext.ImportLines
            .AsNoTracking()
            .Where(l => l.CompanyDb == companyDb && businessKeyHashes.Contains(l.BusinessKeyHash))
            .Select(l => l.BusinessKeyHash)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<string>(found, StringComparer.Ordinal);
    }

    public async Task<long> AddImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default)
    {
        _dbContext.ImportFiles.Add(importFile);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return importFile.Id;
    }

    public async Task AddImportLinesAsync(IEnumerable<ImportLine> lines, CancellationToken cancellationToken = default)
    {
        _dbContext.ImportLines.AddRange(lines);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateImportFileAsync(ImportFile importFile, CancellationToken cancellationToken = default)
    {
        _dbContext.ImportFiles.Update(importFile);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLinesForFileAsync(long importFileId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.ImportLines
            .Where(l => l.ImportFileId == importFileId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return;

        _dbContext.ImportLines.RemoveRange(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
