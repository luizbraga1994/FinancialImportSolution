namespace FinancialImport.Web.Models;

public sealed class PreviewViewModel
{
    public long ImportFileId { get; set; }
    public string LayoutDetected { get; set; } = string.Empty;
    public int TotalLines { get; set; }
    public int InvalidLines { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
}
