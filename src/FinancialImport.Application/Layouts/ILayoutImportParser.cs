using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Layouts;

public interface ILayoutImportParser
{
    string LayoutName { get; }
    bool CanParse(ImportFileContext context);
    Task<IReadOnlyCollection<LancamentoContabilImportado>> ParseAsync(
        ImportFileContext context,
        CancellationToken cancellationToken = default);
}
