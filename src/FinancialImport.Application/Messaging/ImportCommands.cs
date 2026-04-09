using FinancialImport.Shared.Messaging;

namespace FinancialImport.Application.Messaging;

/// <summary>
/// Command published to RabbitMQ telling the ImportProcessorWorker to
/// pick up the given ImportFile and push all journal entries to SAP.
/// </summary>
public sealed class ProcessImportCommand : IAsyncCommand
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");
    public string CommandType => "import.process";

    public long ImportFileId { get; init; }
    public long UserId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;

    /// <summary>Set when the command represents a reprocess attempt.</summary>
    public bool IsReprocess { get; init; }
}

/// <summary>
/// Command sent to the SAP dispatcher worker when a specific journal
/// entry group must be pushed (or retried) to SAP Service Layer. The
/// DispatchId is the natural idempotency key: the worker will refuse to
/// dispatch the same group twice.
/// </summary>
public sealed class DispatchJournalEntryCommand : IAsyncCommand
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");
    public string CommandType => "sap.dispatch";

    public long DispatchId { get; init; }
    public long ImportFileId { get; init; }
    public string CompanyDb { get; init; } = string.Empty;
    public long UserId { get; init; }
    public string GroupKey { get; init; } = string.Empty;
}
