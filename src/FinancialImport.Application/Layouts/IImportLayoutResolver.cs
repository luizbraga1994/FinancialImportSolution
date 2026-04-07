using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Layouts;

public interface IImportLayoutResolver
{
    ILayoutImportParser Resolve(ImportFileContext context);
    IReadOnlyCollection<ILayoutImportParser> GetParsers();
}
