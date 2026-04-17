namespace FinancialImport.Application.Models;

public sealed class SapResult
{
    public bool Success { get; init; }
    public bool IsSessionExpired { get; init; }
    public string? RawResponse { get; init; }
    public string? ErrorMessage { get; init; }

    public static SapResult Ok(string rawResponse) => new() { Success = true, RawResponse = rawResponse };
    public static SapResult Fail(string errorMessage, string? rawResponse = null) => new() { Success = false, ErrorMessage = errorMessage, RawResponse = rawResponse };
    public static SapResult SessionExpired(string errorMessage, string? rawResponse = null) => new() { Success = false, IsSessionExpired = true, ErrorMessage = errorMessage, RawResponse = rawResponse };
}
