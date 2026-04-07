using FinancialImport.Domain.Enums;

namespace FinancialImport.Domain.Entities;

public sealed class ImportLine
{
    public long Id { get; set; }
    public long ImportFileId { get; set; }
    public string LineHash { get; set; } = string.Empty;
    public string BusinessKeyHash { get; set; } = string.Empty;
    public string? SeqLancamento { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string ContraAccountCode { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime DocumentDate { get; set; }
    public decimal Amount { get; set; }
    public decimal? CreditAmount { get; set; }
    public decimal? DebitAmount { get; set; }
    public string LineMemo { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
    public string CompanyDb { get; set; } = string.Empty;
    public ImportLineStatus Status { get; set; }
    public string? ValidationMessage { get; set; }
    public string? SapReturnMessage { get; set; }
    public int? SapDocEntry { get; set; }
    public string? SourceJson { get; set; }

    public ImportFile? ImportFile { get; set; }
}
