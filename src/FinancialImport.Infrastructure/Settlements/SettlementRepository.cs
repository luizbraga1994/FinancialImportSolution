using FinancialImport.Application.Settlements;
using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using FinancialImport.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Settlements;

public sealed class SettlementRepository : ISettlementRepository
{
    private readonly AppDbContext _dbContext;

    public SettlementRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ReceivableSettlementFile?> GetFileAsync(long id, CancellationToken cancellationToken = default)
        => _dbContext.ReceivableSettlementFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<ReceivableSettlementFile?> GetFileWithLinesAsync(long id, CancellationToken cancellationToken = default)
        => _dbContext.ReceivableSettlementFiles
            .Include(f => f.Lines)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<bool> ExistsFileHashAsync(string companyDb, string fileHash, CancellationToken cancellationToken = default)
        => _dbContext.ReceivableSettlementFiles.AnyAsync(f => f.CompanyDb == companyDb && f.FileHash == fileHash, cancellationToken);

    public async Task<IReadOnlySet<string>> GetExistingBusinessKeysAsync(
        string companyDb,
        IReadOnlyCollection<string> businessKeyHashes,
        CancellationToken cancellationToken = default)
    {
        if (businessKeyHashes.Count == 0)
            return new HashSet<string>();

        // Only notes that were actually settled in SAP constitute genuine
        // duplicates; other statuses can be re-uploaded as fresh entries.
        var found = await _dbContext.ReceivableSettlementLines
            .AsNoTracking()
            .Where(l => l.CompanyDb == companyDb
                        && businessKeyHashes.Contains(l.BusinessKeyHash)
                        && l.Status == SettlementLineStatus.Settled)
            .Select(l => l.BusinessKeyHash)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<string>(found, StringComparer.Ordinal);
    }

    public async Task UpdateFileAsync(ReceivableSettlementFile file, CancellationToken cancellationToken = default)
    {
        _dbContext.ReceivableSettlementFiles.Update(file);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLinesForFileAsync(long settlementFileId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.ReceivableSettlementLines
            .Where(l => l.SettlementFileId == settlementFileId)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return;

        _dbContext.ReceivableSettlementLines.RemoveRange(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
