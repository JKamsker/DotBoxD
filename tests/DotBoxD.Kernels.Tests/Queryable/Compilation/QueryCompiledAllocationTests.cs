using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCompiledAllocationTests
{
    [Theory]
    [InlineData("Value")]
    [InlineData("Nested.Value")]
    [Trait("Category", "AllocationMeasurement")]
    public void Promoted_scalar_paths_do_not_box_member_values(string path)
    {
        var filter = QueryFilter.Compare(path, QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(10));
        var compiled = QueryFilterCompiler.Compile(filter, new MemberValueReader(typeof(Sample)));
        var sample = new Sample();
        for (var i = 0; i < 20_000; i++)
        {
            _ = compiled(sample);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = 0;
        for (var i = 0; i < 10_000; i++)
        {
            matches += compiled(sample) ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10_000, matches);
        Assert.Equal(0, allocated);
    }

    private sealed class Sample
    {
        public int Value => 11;
        public Leaf Nested { get; } = new();
    }

    private sealed class Leaf { public int Value => 11; }
}
