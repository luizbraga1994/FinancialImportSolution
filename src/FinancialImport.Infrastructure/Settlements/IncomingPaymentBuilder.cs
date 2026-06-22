using FinancialImport.Application.Models;
using FinancialImport.Application.Settlements;
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
///   CartaoC/D     -> PaymentCreditCards (card resolved from OCRC/OCRP via HANA)
/// Discount goes into the invoice line (SumApplied = Valor - Desconto,
/// TotalDiscount = Desconto). Interest (Juros) is not posted in this version.
/// </summary>
public sealed class IncomingPaymentBuilder
{
    public IncomingPaymentBuildResult Build(
        ReceivableSettlementLine line,
        CardPaymentInfo? card,
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

        var means = PaymentMeansClassifier.Classify(line.PaymentMeans);
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
                if (card == null)
                {
                    return IncomingPaymentBuildResult.Failure(
                        $"Bandeira '{line.CardBrand}' não encontrada no cadastro de cartões do SAP (OCRC).");
                }

                // For cards the receiving account comes from OCRC (AcctCode);
                // ContaContabil is optional and only used as a fallback.
                var creditAcct = !string.IsNullOrWhiteSpace(card.CreditAcct)
                    ? card.CreditAcct
                    : line.ReceivingAccount;
                if (string.IsNullOrWhiteSpace(creditAcct))
                {
                    return IncomingPaymentBuildResult.Failure(
                        $"Conta do cartão não definida para a bandeira '{line.CardBrand}' (sem AcctCode na OCRC e sem ContaContabil).");
                }

                payload.PaymentCreditCards.Add(new SapPaymentCreditCard
                {
                    CreditCard = card.CreditCard,
                    CreditAcct = creditAcct,
                    CreditCardNumber = line.CardLastDigits,
                    VoucherNum = line.VoucherNum,
                    PaymentMethodCode = card.PaymentMethodCode,
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
