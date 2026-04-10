namespace FinancialImport.Application.Imports;

/// <summary>
/// Reads an uploaded file and produces an <see cref="ImportFileContext"/>
/// that the layout parsers can consume. Exposing it as an interface lets
/// us swap the implementation (CSV, XLSX, Parquet, JSON) without
/// touching the orchestration layer and enables mocking in unit tests.
/// </summary>
public interface IImportFileReader
{
    Task<ImportFileContext> ReadAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
