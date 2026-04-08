using FinancialImport.Application.Imports;
using FluentValidation;

namespace FinancialImport.Application.Validators;

public sealed class LancamentoContabilImportadoValidator : AbstractValidator<LancamentoContabilImportado>
{
    public LancamentoContabilImportadoValidator()
    {
        RuleFor(x => x.Referencia)
            .NotEmpty().WithMessage("Referencia e obrigatoria.");

        RuleFor(x => x.ContaContabil)
            .NotEmpty().WithMessage("Conta contabil e obrigatoria.");

        RuleFor(x => x.ContaContrapartida)
            .NotEmpty().WithMessage("Conta contrapartida e obrigatoria.");

        RuleFor(x => x.ContaContabil)
            .NotEqual(x => x.ContaContrapartida)
            .When(x => !string.IsNullOrWhiteSpace(x.ContaContabil))
            .WithMessage("Conta contabil e contrapartida nao podem ser iguais.");

        RuleFor(x => x.Valor)
            .GreaterThanOrEqualTo(0).WithMessage("Valor nao pode ser negativo.");

        RuleFor(x => x)
            .Must(x => x.Valor > 0 || (x.ValorCredito.GetValueOrDefault() > 0 || x.ValorDebito.GetValueOrDefault() > 0))
            .WithMessage("Valor, ValorCredito ou ValorDebito deve ser maior que zero.");

        RuleFor(x => x.DataLancamento)
            .NotEqual(DateTime.MinValue).WithMessage("Data de lancamento e obrigatoria.")
            .LessThanOrEqualTo(DateTime.Today.AddDays(1)).WithMessage("Data de lancamento nao pode ser no futuro.");

        RuleFor(x => x.DataVencimento)
            .NotEqual(DateTime.MinValue).WithMessage("Data de vencimento e obrigatoria.")
            .GreaterThanOrEqualTo(x => x.DataLancamento)
            .When(x => x.DataVencimento != DateTime.MinValue && x.DataLancamento != DateTime.MinValue)
            .WithMessage("Data de vencimento nao pode ser anterior a data de lancamento.");

        RuleFor(x => x.DataDocumento)
            .NotEqual(DateTime.MinValue).WithMessage("Data do documento e obrigatoria.");

        RuleFor(x => x.HistoricoLinha)
            .NotEmpty().WithMessage("Historico da linha e obrigatorio.");
    }
}
