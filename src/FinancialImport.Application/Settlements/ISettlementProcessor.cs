namespace FinancialImport.Application.Settlements;

/// <summary>
/// Port implemented by the Infrastructure layer to turn a confirmed settlement
/// file into SAP Incoming Payments.
/// </summary>
public interface ISettlementProcessor
{
    Task<SettlementProcessResult> ExecuteAsync(long settlementFileId, CancellationToken cancellationToken = default);
}
