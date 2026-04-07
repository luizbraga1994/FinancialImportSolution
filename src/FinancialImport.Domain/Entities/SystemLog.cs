namespace FinancialImport.Domain.Entities;

public sealed class SystemLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public long? UserId { get; set; }
    public string? CompanyDb { get; set; }
    public string? CorrelationId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
