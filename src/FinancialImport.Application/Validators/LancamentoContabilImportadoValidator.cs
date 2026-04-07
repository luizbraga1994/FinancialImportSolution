using FinancialImport.Application.Imports;
using FluentValidation;

namespace FinancialImport.Application.Validators;

public sealed class LancamentoContabilImportadoValidator : AbstractValidator<LancamentoContabilImportado>
{
    public LancamentoContabilImportadoValidator()
    {
        RuleFor(x => x.Referencia).NotEmpty();
        RuleFor(x => x.ContaContabil).NotEmpty();
        RuleFor(x => x.ContaContrapartida).NotEmpty();
        RuleFor(x => x.Valor).GreaterThan(0);
        RuleFor(x => x.DataLancamento).NotEqual(DateTime.MinValue);
        RuleFor(x => x.DataVencimento).NotEqual(DateTime.MinValue);
        RuleFor(x => x.DataDocumento).NotEqual(DateTime.MinValue);
        RuleFor(x => x.HistoricoLinha).NotEmpty();
    }
}
