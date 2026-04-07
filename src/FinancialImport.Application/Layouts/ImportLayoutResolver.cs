using FinancialImport.Application.Imports;

namespace FinancialImport.Application.Layouts;

public sealed class ImportLayoutResolver : IImportLayoutResolver
{
    private readonly IReadOnlyCollection<ILayoutImportParser> _parsers;

    public ImportLayoutResolver(IEnumerable<ILayoutImportParser> parsers)
    {
        _parsers = parsers.ToArray();
    }

    public ILayoutImportParser Resolve(ImportFileContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.DetectedLayout))
        {
            var explicitParser = _parsers.FirstOrDefault(p =>
                p.LayoutName.Equals(context.DetectedLayout, StringComparison.OrdinalIgnoreCase));
            if (explicitParser != null)
            {
                return explicitParser;
            }
        }

        var parser = _parsers.FirstOrDefault(p => p.CanParse(context));
        if (parser == null)
        {
            throw new InvalidOperationException("Layout não reconhecido.");
        }

        return parser;
    }

    public IReadOnlyCollection<ILayoutImportParser> GetParsers() => _parsers;
}
