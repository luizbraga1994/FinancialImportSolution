namespace FinancialImport.Application.Settlements;

/// <summary>
/// Parses the "Baixa de Notas de Saída" spreadsheet into settlement rows.
/// A single layout is expected; new layouts can implement this interface and
/// be selected by header detection.
/// </summary>
public interface ISettlementParser
{
    string LayoutName { get; }
    bool CanParse(SettlementFileContext context);
    Task<IReadOnlyCollection<SettlementLancamento>> ParseAsync(
        SettlementFileContext context,
        CancellationToken cancellationToken = default);
}
