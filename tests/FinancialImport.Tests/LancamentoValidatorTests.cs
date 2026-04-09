using FinancialImport.Application.Imports;
using FinancialImport.Application.Validators;
using FluentAssertions;
using Xunit;

namespace FinancialImport.Tests;

public class LancamentoValidatorTests
{
    private readonly LancamentoContabilImportadoValidator _validator = new();

    [Fact]
    public void Rejects_same_debit_and_credit_account()
    {
        var result = _validator.Validate(new LancamentoContabilImportado
        {
            Referencia = "R",
            ContaContabil = "1100",
            ContaContrapartida = "1100",
            DataLancamento = DateTime.Today,
            DataVencimento = DateTime.Today,
            DataDocumento = DateTime.Today,
            Valor = 1m,
            HistoricoLinha = "memo"
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("nao podem ser iguais"));
    }

    [Fact]
    public void Accepts_valid_payload()
    {
        var result = _validator.Validate(new LancamentoContabilImportado
        {
            Referencia = "R",
            ContaContabil = "1100",
            ContaContrapartida = "2200",
            DataLancamento = DateTime.Today,
            DataVencimento = DateTime.Today,
            DataDocumento = DateTime.Today,
            Valor = 42.5m,
            HistoricoLinha = "memo"
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_missing_historico()
    {
        var result = _validator.Validate(new LancamentoContabilImportado
        {
            Referencia = "R",
            ContaContabil = "1100",
            ContaContrapartida = "2200",
            DataLancamento = DateTime.Today,
            DataVencimento = DateTime.Today,
            DataDocumento = DateTime.Today,
            Valor = 1m,
            HistoricoLinha = ""
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LancamentoContabilImportado.HistoricoLinha));
    }
}
