using FinancialImport.Application.Settlements;
using FluentValidation;

namespace FinancialImport.Application.Validators;

public sealed class SettlementLancamentoValidator : AbstractValidator<SettlementLancamento>
{
    public SettlementLancamentoValidator()
    {
        RuleFor(x => x.DocPN)
            .NotEmpty().WithMessage("Documento do cliente (DocPN) e obrigatorio.");

        RuleFor(x => x.NumeroNota)
            .NotEmpty().WithMessage("Numero da nota e obrigatorio.");

        RuleFor(x => x.Modelo)
            .NotEmpty().WithMessage("Modelo da nota e obrigatorio.");

        RuleFor(x => x.FormaPagamento)
            .NotEmpty().WithMessage("Forma de pagamento e obrigatoria.");

        RuleFor(x => x.ContaContabil)
            .NotEmpty().WithMessage("Conta contabil de recebimento e obrigatoria.");

        RuleFor(x => x.Valor)
            .GreaterThan(0).WithMessage("Valor deve ser maior que zero.");

        RuleFor(x => x.Desconto)
            .GreaterThanOrEqualTo(0).WithMessage("Desconto nao pode ser negativo.")
            .LessThanOrEqualTo(x => x.Valor).When(x => x.Valor > 0)
            .WithMessage("Desconto nao pode ser maior que o valor.");

        RuleFor(x => x.Juros)
            .GreaterThanOrEqualTo(0).WithMessage("Juros nao pode ser negativo.");

        RuleFor(x => x.DataPagamento)
            .NotEqual(DateTime.MinValue).WithMessage("Data de pagamento e obrigatoria.");
    }
}
