using FinancialImport.Application.Settlements;
using FinancialImport.Application.Validators;
using FinancialImport.Domain.Entities;
using FinancialImport.Infrastructure.Settlements;
using FluentAssertions;
using Xunit;

namespace FinancialImport.Tests;

public class SettlementKeyBuilderTests
{
    private static SettlementLancamento Line(string forma, decimal valor, string conta, string reference) => new()
    {
        DocPN = "100.692.536-84",
        NumeroNota = "692261",
        Serie = null,
        Modelo = "NFS-e",
        FormaPagamento = forma,
        ContaContabil = conta,
        Valor = valor,
        Referencia = reference
    };

    [Fact]
    public void Two_payments_for_same_note_should_produce_distinct_business_keys()
    {
        // Same note, different payment means/account/amount/reference: these are
        // two legitimate payments and must NOT collide / be flagged as duplicates.
        var cash = SettlementKeyBuilder.BuildBusinessKey("DB", Line("Dinheiro", 110.00m, "111010010001", "HF123470"));
        var transfer = SettlementKeyBuilder.BuildBusinessKey("DB", Line("Transferência", 200.60m, "111020020013", "HF123471"));

        cash.Should().NotBe(transfer);
    }

    [Fact]
    public void Group_key_should_differ_by_ordinal_even_when_business_key_is_identical()
    {
        var bk = SettlementKeyBuilder.BuildBusinessKey("DB", Line("Dinheiro", 110.00m, "111010010001", "HF123470"));

        var g0 = SettlementKeyBuilder.BuildGroupKey(bk, 0);
        var g1 = SettlementKeyBuilder.BuildGroupKey(bk, 1);

        g0.Should().NotBe(g1);
    }
}

public class IncomingPaymentBuilderTests
{
    private static ReceivableSettlementLine ResolvedLine(string forma, decimal valor, decimal desconto = 0m) => new()
    {
        CustomerDoc = "100.692.536-84",
        InvoiceSerial = "692261",
        InvoiceModel = "NFS-e",
        PaymentMeans = forma,
        ReceivingAccount = "111010010001",
        Amount = valor,
        Discount = desconto,
        PaymentDate = new DateTime(2026, 6, 22),
        Reference = "HF123470",
        CardCode = "C006594",
        InvoiceDocEntry = 11371,
        BplId = 2
    };

    [Fact]
    public void Cash_payment_maps_to_cash_account_and_sum()
    {
        var result = new IncomingPaymentBuilder().Build(ResolvedLine("Dinheiro", 110.00m), null, 15);

        result.IsValid.Should().BeTrue();
        result.Payload!.CashAccount.Should().Be("111010010001");
        result.Payload.CashSum.Should().Be(110.00m);
        result.Payload.TransferAccount.Should().BeNull();
        result.Payload.PaymentInvoices.Should().ContainSingle();
        result.Payload.PaymentInvoices[0].DocEntry.Should().Be(11371);
        result.Payload.PaymentInvoices[0].SumApplied.Should().Be(110.00m);
        result.Payload.U_ReferenciaPgto.Should().Be("HF123470");
    }

    [Fact]
    public void Transfer_payment_maps_to_transfer_account_and_sum()
    {
        var result = new IncomingPaymentBuilder().Build(ResolvedLine("Transferência", 200.60m), null, 15);

        result.IsValid.Should().BeTrue();
        result.Payload!.TransferAccount.Should().Be("111010010001");
        result.Payload.TransferSum.Should().Be(200.60m);
        result.Payload.TransferDate.Should().Be(new DateTime(2026, 6, 22));
        result.Payload.CashAccount.Should().BeNull();
    }

    [Fact]
    public void Discount_reduces_applied_sum_and_sets_total_discount()
    {
        // Invoice 39.95, discount 2.00 -> net paid 37.95.
        var result = new IncomingPaymentBuilder().Build(ResolvedLine("Transferência", 39.95m, 2.00m), null, 15);

        result.IsValid.Should().BeTrue();
        result.Payload!.TransferSum.Should().Be(37.95m);
        result.Payload.PaymentInvoices[0].SumApplied.Should().Be(37.95m);
        result.Payload.PaymentInvoices[0].TotalDiscount.Should().Be(2.00m);
    }

    [Fact]
    public void Card_payment_without_mapping_fails()
    {
        var line = ResolvedLine("CartaoC", 360.48m);
        line.CardBrand = "ELOCREDITO";

        var result = new IncomingPaymentBuilder().Build(line, null, 15);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("não mapeada");
    }

    [Fact]
    public void Card_payment_with_mapping_builds_credit_card_section()
    {
        var line = ResolvedLine("CartaoC", 360.48m);
        line.CardBrand = "ELOCREDITO";
        line.CardLastDigits = "1234";
        line.Installments = 5;
        line.VoucherNum = "69226610914";

        var mapping = new CardBrandMapping
        {
            BrandName = "ELOCREDITO",
            SapCreditCardCode = 3,
            PaymentMethodCode = 2,
            CreditAccount = "112020010003",
            IsActive = true
        };

        var result = new IncomingPaymentBuilder().Build(line, mapping, 15);

        result.IsValid.Should().BeTrue();
        result.Payload!.PaymentCreditCards.Should().ContainSingle();
        var card = result.Payload.PaymentCreditCards[0];
        card.CreditCard.Should().Be(3);
        card.CreditAcct.Should().Be("112020010003");
        card.CreditCardNumber.Should().Be("1234");
        card.NumOfPayments.Should().Be(5);
        card.VoucherNum.Should().Be("69226610914");
        card.CreditSum.Should().Be(360.48m);
    }

    [Fact]
    public void Card_payment_uses_mapping_account_when_conta_contabil_is_blank()
    {
        // ContaContabil is optional for cards: the account comes from the brand mapping.
        var line = ResolvedLine("CartaoD", 458.68m);
        line.ReceivingAccount = string.Empty;
        line.CardBrand = "ELODEBITO";

        var mapping = new CardBrandMapping
        {
            BrandName = "ELODEBITO",
            SapCreditCardCode = 6,
            PaymentMethodCode = 1,
            CreditAccount = "112020010003",
            IsActive = true
        };

        var result = new IncomingPaymentBuilder().Build(line, mapping, 15);

        result.IsValid.Should().BeTrue();
        result.Payload!.PaymentCreditCards[0].CreditAcct.Should().Be("112020010003");
    }

    [Fact]
    public void Card_payment_fails_when_no_account_anywhere()
    {
        var line = ResolvedLine("CartaoC", 100m);
        line.ReceivingAccount = string.Empty;
        line.CardBrand = "ELOCREDITO";

        var mapping = new CardBrandMapping
        {
            BrandName = "ELOCREDITO",
            SapCreditCardCode = 3,
            PaymentMethodCode = 2,
            CreditAccount = null,
            IsActive = true
        };

        var result = new IncomingPaymentBuilder().Build(line, mapping, 15);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Conta do cartão");
    }
}

public class SettlementLancamentoValidatorTests
{
    private static SettlementLancamento BaseLine() => new()
    {
        DocPN = "100.692.536-84",
        NumeroNota = "692261",
        Modelo = "NFS-e",
        Valor = 110m,
        DataPagamento = new DateTime(2026, 6, 22)
    };

    [Fact]
    public void Card_line_without_conta_contabil_is_valid()
    {
        var line = BaseLine();
        line.FormaPagamento = "CartaoC";
        line.ContaContabil = string.Empty;

        var result = new SettlementLancamentoValidator().Validate(line);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Non_card_line_without_conta_contabil_is_invalid()
    {
        var line = BaseLine();
        line.FormaPagamento = "Dinheiro";
        line.ContaContabil = string.Empty;

        var result = new SettlementLancamentoValidator().Validate(line);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SettlementLancamento.ContaContabil));
    }
}
