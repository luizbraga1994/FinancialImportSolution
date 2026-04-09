using FinancialImport.Application.Models;

namespace FinancialImport.Application.Imports;

public interface IImportService
{
    Task<ImportPreviewResult> PreviewAsync(ImportFileContext context, CancellationToken cancellationToken = default);
    Task<ImportProcessResult> ProcessAsync(long importFileId, CancellationToken cancellationToken = default);
}

public sealed class ImportPreviewResult
{
    public long ImportFileId { get; init; }
    public string LayoutDetected { get; init; } = string.Empty;
    public IReadOnlyCollection<LancamentoContabilImportado> Lines { get; init; } = Array.Empty<LancamentoContabilImportado>();
    public IReadOnlyCollection<string> Errors { get; init; } = Array.Empty<string>();
    public int ValidLines { get; init; }
    public int InvalidLines { get; init; }
    public int DuplicatedLines { get; init; }
    public int TotalLines => Lines.Count; // Adicionado para compatibilidade
}

public sealed class ImportProcessResult
{
    public long ImportFileId { get; init; }
    public int Imported { get; init; }
    public int Duplicated { get; init; }
    public int Invalid { get; init; }
    public int SapErrors { get; init; }
}