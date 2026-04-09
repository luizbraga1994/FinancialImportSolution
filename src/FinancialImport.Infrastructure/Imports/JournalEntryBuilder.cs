using FinancialImport.Application.Models;
using FinancialImport.Domain.Entities;
using FinancialImport.Shared.Imports;
using Microsoft.Extensions.Options;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Translates a group of <see cref="ImportLine"/>s into the SAP
/// JournalEntry payload. Lives outside of the ImportService so the
/// balance/truncation rules can be unit tested without touching the
/// database or SAP.
/// </summary>
public sealed class JournalEntryBuilder
{
    private readonly ImportProcessingOptions _options;

    public JournalEntryBuilder(IOptions<ImportProcessingOptions> options)
    {
        _options = options.Value;
    }

    public JournalEntryBuildResult Build(
        string groupKey,
        string groupKeyHash,
        IReadOnlyList<ImportLine> lines,
        int? bplId)
    {
        if (lines.Count == 0)
            throw new ArgumentException("Cannot build a journal entry for an empty group.", nameof(lines));

        var firstLine = lines[0];
        var payload = new SapJournalEntry
        {
            ReferenceDate = firstLine.PostingDate,
            DueDate = firstLine.DueDate,
            TaxDate = firstLine.DocumentDate,
            Memo = Truncate(
                BuildMemo(firstLine),
                _options.MemoMaxLength),
            // Use the GroupKeyHash as SAP Reference so the SAP side has
            // a deterministic identifier and duplicates can be spotted
            // even if the integration retries.
            Reference = Truncate(groupKeyHash, _options.ReferenceMaxLength),
            BPLID = bplId,
            JournalEntryLines = new List<SapJournalEntryLine>()
        };

        foreach (var line in lines)
        {
            // Account-side entry
            var debit = line.DebitAmount ?? 0m;
            var credit = line.CreditAmount ?? 0m;

            if (debit == 0m && credit == 0m)
                continue;

            payload.JournalEntryLines.Add(new SapJournalEntryLine
            {
                AccountCode = line.AccountCode,
                Debit = debit,
                Credit = credit,
                LineMemo = Truncate(line.LineMemo, _options.LineMemoMaxLength)
            });

            // Counterparty line: flipped debit/credit
            payload.JournalEntryLines.Add(new SapJournalEntryLine
            {
                AccountCode = line.ContraAccountCode,
                Debit = credit,
                Credit = debit,
                LineMemo = Truncate(line.LineMemo, _options.LineMemoMaxLength)
            });
        }

        var totalDebit = payload.JournalEntryLines.Sum(l => l.Debit);
        var totalCredit = payload.JournalEntryLines.Sum(l => l.Credit);
        var imbalance = totalDebit - totalCredit;
        var balanced = Math.Abs(imbalance) <= _options.JournalBalanceTolerance;

        return new JournalEntryBuildResult
        {
            Payload = payload,
            GroupKey = groupKey,
            GroupKeyHash = groupKeyHash,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            IsBalanced = balanced
        };
    }

    private static string BuildMemo(ImportLine firstLine)
        => $"{firstLine.Reference} - Venc:{firstLine.DueDate:dd/MM/yyyy} - Doc:{firstLine.DocumentDate:dd/MM/yyyy}";

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}

public sealed class JournalEntryBuildResult
{
    public SapJournalEntry Payload { get; init; } = new();
    public string GroupKey { get; init; } = string.Empty;
    public string GroupKeyHash { get; init; } = string.Empty;
    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }
    public bool IsBalanced { get; init; }
}
