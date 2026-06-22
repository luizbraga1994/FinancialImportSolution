using System.Globalization;
using System.Text;
using FinancialImport.Application.Models;
using FinancialImport.Domain.Entities;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Translates a <see cref="ReceivableSettlementLine"/> (with its open invoice
/// already resolved) into the SAP Incoming Payment payload. Kept separate from
/// the processor so the payment-means mapping rules can be unit tested without
/// touching the database or SAP.
///
/// Mapping (confirmed from real SAP payloads):
///   Dinheiro      -> CashAccount/CashSum
///   Transferência -> TransferAccount/TransferSum/TransferDate
///   CartaoC/D     -> PaymentCreditCards (requires a CardBrandMapping)
/// Discount goes into the invoice line (SumApplied = Valor - Desconto,
/// TotalDiscount = Desconto). Interest (Juros) is not posted in this version.
/// </summary>
public sealed class IncomingPaymentBuilder
{
    public IncomingPaymentBuildResult Build(
        ReceivableSettlementLine line,
        CardBrandMapping? cardMapping,
        int? seriesOverride)
    {
        if (string.IsNullOrWhiteSpace(line.CardCode) || !line.InvoiceDocEntry.HasValue)
        {
            return IncomingPaymentBuildResult.Failure("Fatura não resolvida (CardCode/DocEntry ausentes).");
        }

        var net = decimal.Round(line.Amount - line.Discount, 2, MidpointRounding.AwayFromZero);
        if (net <= 0m)
        {
            return IncomingPaymentBuildResult.Failure("Valor líquido (Valor - Desconto) deve ser maior que zero.");
        }

        var payload = new SapIncomingPayment
        {
            DocType = "rCustomer",
            CardCode = line.CardCode!,
            DocDate = line.PaymentDate,
            TaxDate = line.PaymentDate,
            Series = seriesOverride,
            JournalRemarks = $"Contas a receber - {line.CardCode}",
            BPLID = line.BplId,
            U_ReferenciaPgto = string.IsNullOrWhiteSpace(line.Reference) ? null : line.Reference,
            PaymentInvoices =
            {
                new SapPaymentInvoice
                {
                    DocEntry = line.InvoiceDocEntry.Value,
                    InvoiceType = "it_Invoice",
                    SumApplied = net,
                    TotalDiscount = line.Discount > 0m ? line.Discount : null,
                    DiscountPercent = line.Discount > 0m && line.Amount > 0m
                        ? decimal.Round(line.Discount / line.Amount * 100m, 4, MidpointRounding.AwayFromZero)
                        : null
                }
            }
        };

        var means = ClassifyPaymentMeans(line.PaymentMeans);
        switch (means)
        {
            case PaymentMeans.Cash:
                payload.CashAccount = line.ReceivingAccount;
                payload.CashSum = net;
                break;

            case PaymentMeans.Transfer:
                payload.TransferAccount = line.ReceivingAccount;
                payload.TransferSum = net;
                payload.TransferDate = line.PaymentDate;
                break;

            case PaymentMeans.Card:
                if (cardMapping == null)
                {
                    return IncomingPaymentBuildResult.Failure(
                        $"Bandeira '{line.CardBrand}' não mapeada para um cartão do SAP.");
                }

                payload.PaymentCreditCards.Add(new SapPaymentCreditCard
                {
                    CreditCard = cardMapping.SapCreditCardCode,
                    CreditAcct = string.IsNullOrWhiteSpace(cardMapping.CreditAccount)
                        ? line.ReceivingAccount
                        : cardMapping.CreditAccount,
                    CreditCardNumber = line.CardLastDigits,
                    VoucherNum = line.VoucherNum,
                    PaymentMethodCode = cardMapping.PaymentMethodCode,
                    NumOfPayments = line.Installments > 0 ? line.Installments : 1,
                    FirstPaymentDue = line.PaymentDate,
                    CreditSum = net
                });
                break;

            default:
                return IncomingPaymentBuildResult.Failure(
                    $"Forma de pagamento não reconhecida: '{line.PaymentMeans}'.");
        }

        return IncomingPaymentBuildResult.Ok(payload, net);
    }

    public static PaymentMeans ClassifyPaymentMeans(string? raw)
    {
        var norm = RemoveAccents(raw ?? string.Empty).Trim().ToLowerInvariant();
        if (norm.Length == 0) return PaymentMeans.Unknown;
        if (norm.Contains("dinheiro") || norm.Contains("cash") || norm.Contains("especie")) return PaymentMeans.Cash;
        if (norm.Contains("transfer")) return PaymentMeans.Transfer;
        if (norm.Contains("cartao") || norm.Contains("card")) return PaymentMeans.Card;
        return PaymentMeans.Unknown;
    }

    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public enum PaymentMeans
{
    Unknown = 0,
    Cash = 1,
    Transfer = 2,
    Card = 3
}

public sealed class IncomingPaymentBuildResult
{
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }
    public SapIncomingPayment? Payload { get; private init; }
    public decimal NetAmount { get; private init; }

    public static IncomingPaymentBuildResult Ok(SapIncomingPayment payload, decimal net)
        => new() { IsValid = true, Payload = payload, NetAmount = net };

    public static IncomingPaymentBuildResult Failure(string error)
        => new() { IsValid = false, Error = error };
}
