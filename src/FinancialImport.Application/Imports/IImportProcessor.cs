namespace FinancialImport.Application.Imports;

/// <summary>
/// Port implemented by the Infrastructure layer to actually turn a
/// confirmed ImportFile into SAP Journal Entries. The Application layer
/// does not know how this happens (HTTP to SAP? message bus? batch?),
/// which keeps the orchestration concerns out of the use-case code.
/// </summary>
public interface IImportProcessor
{
    Task<ImportProcessResult> ExecuteAsync(long importFileId, CancellationToken cancellationToken = default);
}
