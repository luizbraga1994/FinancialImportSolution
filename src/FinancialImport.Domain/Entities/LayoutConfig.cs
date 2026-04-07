namespace FinancialImport.Domain.Entities;

public sealed class LayoutConfig
{
    public long Id { get; set; }
    public string LayoutName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Description { get; set; }

    public ICollection<LayoutFieldConfig> Fields { get; set; } = new List<LayoutFieldConfig>();
}
