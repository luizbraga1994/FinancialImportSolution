using FinancialImport.Shared.Correlation;
using FluentAssertions;
using Xunit;

namespace FinancialImport.Tests;

public class CorrelationContextTests
{
    [Fact]
    public void Push_and_Current_return_same_instance_within_scope()
    {
        var accessor = new CorrelationContextAccessor();
        var ctx = new CorrelationContext { CorrelationId = "abc", UserId = 10, CompanyDb = "DBX" };

        using (accessor.Push(ctx))
        {
            accessor.Current.Should().NotBeNull();
            accessor.Current!.CorrelationId.Should().Be("abc");
            accessor.Current.UserId.Should().Be(10);
            accessor.Current.CompanyDb.Should().Be("DBX");
        }

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Nested_Push_restores_previous_context_on_Dispose()
    {
        var accessor = new CorrelationContextAccessor();
        var outer = new CorrelationContext { CorrelationId = "outer" };
        var inner = new CorrelationContext { CorrelationId = "inner" };

        using (accessor.Push(outer))
        {
            accessor.Current!.CorrelationId.Should().Be("outer");
            using (accessor.Push(inner))
            {
                accessor.Current!.CorrelationId.Should().Be("inner");
            }
            accessor.Current!.CorrelationId.Should().Be("outer");
        }

        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void Child_rolls_causation_from_current_correlation()
    {
        var parent = CorrelationContext.NewRoot() with { CorrelationId = "root-id" };
        var child = parent.Child();

        child.CausationId.Should().Be("root-id");
        child.CorrelationId.Should().NotBe(parent.CorrelationId);
    }
}
