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
            // Memo (= "Observacoes" in SAP B1) shows the human-readable
            // Referencia from the import file. Dates already appear in
            // the journal header — appending them here would just be
            // visual noise.
            Memo = Truncate(firstLine.Reference, _options.MemoMaxLength),
            // SAP Reference field (Ref1) carries the same business
            // reference, truncated to its column width. Idempotency is
            // enforced by the LancamentoSapDispatch (CompanyDb,
            // GroupKeyHash) unique index, NOT by Ref1, so there is no
            // need to leak the hash into a user-visible field.
            Reference = Truncate(firstLine.Reference, _options.ReferenceMaxLength),
            JournalEntryLines = new List<SapJournalEntryLine>()
        };

        // Build debits and credits in two passes so the resulting SAP
        // journal lists ALL debits first then ALL credits — matching the
        // visual order users expect when reviewing entries in SAP B1.
        var debitLines = new List<SapJournalEntryLine>(lines.Count);
        var creditLines = new List<SapJournalEntryLine>(lines.Count);

        foreach (var line in lines)
        {
            // Resolve amount + direction. Each input row becomes a pair
            // of SAP journal lines (ContaContabil + ContraAccountCode)
            // whose sides MUST be mutually exclusive — never debit+credit
            // on the same line.
            //
            // Direction rules:
            //   - DebitAmount populated  → ContaContabil = debit
            //   - CreditAmount populated → ContaContabil = credit
            //   - Both populated with the same value (common user error
            //     when the template has both columns) → default to
            //     ContaContabil = debit (standard general-journal entry)
            //   - Both zero/null → skip line
            var debitAmount = line.DebitAmount ?? 0m;
            var creditAmount = line.CreditAmount ?? 0m;

            decimal accountDebit;
            decimal accountCredit;

            if (debitAmount > 0m)
            {
                accountDebit = debitAmount;
                accountCredit = 0m;
            }
            else if (creditAmount > 0m)
            {
                accountDebit = 0m;
                accountCredit = creditAmount;
            }
            else if (line.Amount > 0m)
            {
                // Fallback: neither credit nor debit column was parsed
                // but the Amount field is populated — treat as debit.
                accountDebit = line.Amount;
                accountCredit = 0m;
            }
            else
            {
                continue;
            }

            var memo = Truncate(line.LineMemo, _options.LineMemoMaxLength);

            // Main account (ContaContabil)
            var mainLine = new SapJournalEntryLine
            {
                AccountCode = line.AccountCode,
                Debit = accountDebit,
                Credit = accountCredit,
                LineMemo = memo,
                BPLID = bplId
            };

            // Counterparty (ContraAccountCode) — sides flipped
            var contraLine = new SapJournalEntryLine
            {
                AccountCode = line.ContraAccountCode,
                Debit = accountCredit,
                Credit = accountDebit,
                LineMemo = memo,
                BPLID = bplId
            };

            (mainLine.Debit > 0m ? debitLines : creditLines).Add(mainLine);
            (contraLine.Debit > 0m ? debitLines : creditLines).Add(contraLine);
        }

        payload.JournalEntryLines.AddRange(debitLines);
        payload.JournalEntryLines.AddRange(creditLines);

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
