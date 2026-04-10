namespace FinancialImport.Shared.Imports;

/// <summary>
/// Configurable layout definitions loaded from appsettings. This replaces
/// the hardcoded column names previously baked into Layout1Parser and
/// Layout2Parser. Any change to an incoming file format can now be made
/// by editing configuration instead of recompiling.
/// </summary>
public sealed class LayoutDefinitionsOptions
{
    public const string SectionName = "Imports:Layouts";

    public List<LayoutDefinition> Definitions { get; set; } = new();
}

public sealed class LayoutDefinition
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum set of headers required to detect this layout.</summary>
    public string[] RequiredHeaders { get; set; } = Array.Empty<string>();

    /// <summary>Culture used to parse dates and numbers.</summary>
    public string Culture { get; set; } = "pt-BR";

    /// <summary>Date formats to try, in order.</summary>
    public string[] DateFormats { get; set; } =
    {
        "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyyMMdd"
    };

    /// <summary>
    /// Mapping from the internal field name (Reference, AccountCode, ...)
    /// to the list of accepted header names in the source file.
    /// </summary>
    public Dictionary<string, string[]> FieldMap { get; set; } = new();

    /// <summary>
    /// Default value for the lancamento type (D/C) when the column is
    /// absent. Previously hardcoded to "D" — now configurable per layout.
    /// </summary>
    public string? DefaultTipoLanc { get; set; }
}
