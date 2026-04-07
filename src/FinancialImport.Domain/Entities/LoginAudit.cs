namespace FinancialImport.Domain.Entities;

public sealed class LoginAudit
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string LoginProvided { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? FailureReason { get; set; }

    public User? User { get; set; }
}
