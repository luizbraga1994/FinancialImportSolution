namespace FinancialImport.Domain.Entities;

public sealed class LayoutFieldConfig
{
    public long Id { get; set; }
    public long LayoutId { get; set; }
    public string SourceColumnName { get; set; } = string.Empty;
    public string InternalFieldName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string DataType { get; set; } = string.Empty;
    public int Order { get; set; }

    public LayoutConfig? Layout { get; set; }
}
