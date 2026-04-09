using FinancialImport.Application.Imports;
using FinancialImport.Infrastructure.Imports;
using FinancialImport.Shared.Imports;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialImport.Tests;

public class BusinessKeyBuilderTests
{
    private static BusinessKeyBuilder Create(DeduplicationFields? fields = null)
    {
        var options = Options.Create(new ImportProcessingOptions
        {
            DeduplicationKey = fields ?? new DeduplicationFields()
        });
        return new BusinessKeyBuilder(options);
    }

    [Fact]
    public void BusinessKey_should_include_seq_lancamento_when_present()
    {
        // Two lines identical except for SeqLancamento MUST produce
        // distinct business keys — this is the core guarantee of the
        // "id de controle" improvement.
        var builder = Create();
        var a = Line("REF", "2026-01-01", "100.00", seq: "AAA");
        var b = Line("REF", "2026-01-01", "100.00", seq: "BBB");

        var keyA = builder.BuildBusinessKey("TESTDB", a);
        var keyB = builder.BuildBusinessKey("TESTDB", b);

        keyA.Should().NotBe(keyB);
    }

    [Fact]
    public void BusinessKey_should_be_identical_for_identical_lines_without_seq()
    {
        var builder = Create();
        var a = Line("REF", "2026-01-01", "100.00", seq: null);
        var b = Line("REF", "2026-01-01", "100.00", seq: null);

        var keyA = builder.BuildBusinessKey("TESTDB", a);
        var keyB = builder.BuildBusinessKey("TESTDB", b);

        keyA.Should().Be(keyB);
    }

    [Fact]
    public void BusinessKey_should_change_when_seq_is_disabled_and_content_matches()
    {
        var builder = Create(new DeduplicationFields { IncludeSeqLancamento = false });
        var a = Line("REF", "2026-01-01", "100.00", seq: "AAA");
        var b = Line("REF", "2026-01-01", "100.00", seq: "BBB");

        var keyA = builder.BuildBusinessKey("TESTDB", a);
        var keyB = builder.BuildBusinessKey("TESTDB", b);

        keyA.Should().Be(keyB, "SeqLancamento is excluded by configuration");
    }

    [Fact]
    public void GroupKey_should_differ_per_seq_lancamento()
    {
        var builder = Create();
        var (_, hashA) = builder.BuildGroupKey(
            "TESTDB", "REF",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            "AAA");
        var (_, hashB) = builder.BuildGroupKey(
            "TESTDB", "REF",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            "BBB");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void GroupKey_should_be_stable_across_invocations()
    {
        var builder = Create();
        var (_, first) = builder.BuildGroupKey(
            "TESTDB", "REF-001",
            new DateTime(2026, 2, 10),
            new DateTime(2026, 2, 20),
            new DateTime(2026, 2, 10),
            "SEQ-1");
        var (_, second) = builder.BuildGroupKey(
            "TESTDB", "REF-001",
            new DateTime(2026, 2, 10),
            new DateTime(2026, 2, 20),
            new DateTime(2026, 2, 10),
            "SEQ-1");

        first.Should().Be(second);
    }

    private static LancamentoContabilImportado Line(
        string referencia, string data, string valor, string? seq) => new()
    {
        Referencia = referencia,
        ContaContabil = "1100",
        ContaContrapartida = "2200",
        DataLancamento = DateTime.Parse(data),
        DataVencimento = DateTime.Parse(data),
        DataDocumento = DateTime.Parse(data),
        Valor = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture),
        HistoricoLinha = "memo",
        Filial = "01",
        SeqLancamento = seq
    };
}
