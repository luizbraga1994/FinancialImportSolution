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
        int? bplId,
        IReadOnlyDictionary<string, string>? accountCodes = null)
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

        // Detect "pre-balanced" input: the file already contains both
        // debit and credit rows that balance within the group. In that
        // case, emitting a ContraAccount line per input would duplicate
        // the movement. We only generate contra lines when the input
        // has one-sided rows that need the counterparty to balance.
        var groupDebitTotal = lines.Sum(l => l.DebitAmount ?? 0m);
        var groupCreditTotal = lines.Sum(l => l.CreditAmount ?? 0m);
        var alreadyBalanced = groupDebitTotal > 0m
            && groupCreditTotal > 0m
            && Math.Abs(groupDebitTotal - groupCreditTotal) <= _options.JournalBalanceTolerance;

        foreach (var line in lines)
        {
            var debitAmount = line.DebitAmount ?? 0m;
            var creditAmount = line.CreditAmount ?? 0m;
            var memo = Truncate(line.LineMemo, _options.LineMemoMaxLength);

            if (alreadyBalanced)
            {
                // Pre-balanced detailed input. Column names map DIRECTLY
                // to the SAP journal side:
                //
                // (a) BOTH columns on same row → classic 2-line entry:
                //     ContaContabil  receives Debit  (= Valor Debito)
                //     Contrapartida  receives Credit (= Valor Credito)
                //
                // (b) Only ONE column on the row → 1 SAP line:
                //     "Valor Credito" → ContaContabil on CREDIT side
                //     "Valor Debito"  → Contrapartida on DEBIT side
                if (creditAmount > 0m && debitAmount > 0m)
                {
                    var dl = new SapJournalEntryLine { Debit = debitAmount, Credit = 0m, LineMemo = memo, BPLID = bplId, CostingCode = line.CostingCode };
                    ApplyCode(dl, line.AccountCode, accountCodes);
                    debitLines.Add(dl);

                    var cl = new SapJournalEntryLine { Debit = 0m, Credit = creditAmount, LineMemo = memo, BPLID = bplId, CostingCode = line.CostingCode };
                    ApplyCode(cl, line.ContraAccountCode, accountCodes);
                    creditLines.Add(cl);
                }
                else if (creditAmount > 0m)
                {
                    var cl = new SapJournalEntryLine { Debit = 0m, Credit = creditAmount, LineMemo = memo, BPLID = bplId, CostingCode = line.CostingCode };
                    ApplyCode(cl, line.AccountCode, accountCodes);
                    creditLines.Add(cl);
                }
                else if (debitAmount > 0m)
                {
                    var dl = new SapJournalEntryLine { Debit = debitAmount, Credit = 0m, LineMemo = memo, BPLID = bplId, CostingCode = line.CostingCode };
                    ApplyCode(dl, line.ContraAccountCode, accountCodes);
                    debitLines.Add(dl);
                }
                continue;
            }

            // One-sided input (legacy): emit main + contra lines to balance.
            // Direction rules:
            //   - DebitAmount populated  → ContaContabil = debit, Contrapartida = credit
            //   - CreditAmount populated → ContaContabil = credit, Contrapartida = debit
            //   - Amount fallback → treat as debit
            //   - Both zero/null → skip line
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
                accountDebit = line.Amount;
                accountCredit = 0m;
            }
            else
            {
                continue;
            }

            // Main account (ContaContabil)
            var mainLine = new SapJournalEntryLine
            {
                Debit = accountDebit,
                Credit = accountCredit,
                LineMemo = memo,
                BPLID = bplId,
                CostingCode = line.CostingCode
            };
            ApplyCode(mainLine, line.AccountCode, accountCodes);

            (mainLine.Debit > 0m ? debitLines : creditLines).Add(mainLine);

            var contraLine = new SapJournalEntryLine
            {
                Debit = accountCredit,
                Credit = accountDebit,
                LineMemo = memo,
                BPLID = bplId,
                CostingCode = line.CostingCode
            };
            ApplyCode(contraLine, line.ContraAccountCode, accountCodes);

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

    /// <summary>
    /// Sets either <see cref="SapJournalEntryLine.AccountCode"/> or
    /// <see cref="SapJournalEntryLine.ShortName"/> depending on whether
    /// <paramref name="code"/> is a G/L account or a Business Partner code.
    /// When <paramref name="accountCodes"/> is provided, a code found in the
    /// chart of accounts is always treated as a G/L account even if it
    /// contains letters. The letter heuristic is only used as a fallback.
    /// </summary>
    private static void ApplyCode(SapJournalEntryLine line, string? code, IReadOnlyDictionary<string, string>? accountCodes)
    {
        if (IsBusinessPartnerCode(code, accountCodes))
            line.ShortName = code;
        else
            line.AccountCode = code;
    }

    private static bool IsBusinessPartnerCode(string? code, IReadOnlyDictionary<string, string>? accountCodes)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        // A code present in the chart of accounts is always a G/L account.
        if (accountCodes != null && accountCodes.ContainsKey(code)) return false;
        // Fallback: no COA available (or code not in COA) — letters signal a Business Partner.
        return SapAccountCodeHelper.IsBusinessPartner(code);
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
