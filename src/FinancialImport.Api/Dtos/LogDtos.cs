namespace FinancialImport.Api.Dtos;

public sealed class SystemLogDto
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

public sealed class LoginAuditDto
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string LoginProvided { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? FailureReason { get; set; }
}
