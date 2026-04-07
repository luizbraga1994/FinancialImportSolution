namespace FinancialImport.Application.Models;

public sealed class SapLoginResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public SapSessionContext? Session { get; init; }

    public static SapLoginResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    public static SapLoginResult Ok(SapSessionContext session) => new() { Success = true, Session = session };
}
