namespace FinancialImport.Shared.Logging;

/// <summary>
/// Logical classification of a log entry. Used both by the structured
/// logger and by the database audit sink to separate technical, business,
/// audit, integration, messaging and security logs.
/// </summary>
public static class LogCategories
{
    public const string Technical    = "TECHNICAL";
    public const string Functional   = "FUNCTIONAL";
    public const string Audit        = "AUDIT";
    public const string Integration  = "INTEGRATION";
    public const string Messaging    = "MESSAGING";
    public const string Security     = "SECURITY";
    public const string Performance  = "PERFORMANCE";
}

/// <summary>
/// Structured log levels used by the database sink so UI filters share
/// the exact same vocabulary as the writer.
/// </summary>
public static class LogSeverities
{
    public const string Debug    = "Debug";
    public const string Info     = "Info";
    public const string Warning  = "Warning";
    public const string Error    = "Error";
    public const string Critical = "Critical";
    public const string Audit    = "Audit";
}
