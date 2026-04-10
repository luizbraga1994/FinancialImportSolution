namespace FinancialImport.Shared.Messaging;

/// <summary>
/// Options that govern the transactional outbox dispatcher. The dispatcher
/// polls persisted messages in the same database as the business data and
/// publishes them to the broker, guaranteeing "at-least-once" delivery
/// without risking dual-write inconsistency.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Messaging:Outbox";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 10;
    public int ClaimTimeoutSeconds { get; set; } = 120;
}
