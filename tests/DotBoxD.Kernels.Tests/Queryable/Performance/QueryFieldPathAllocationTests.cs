using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryFieldPathAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "AllocationMeasurement")]
    [InlineData("AttackerId")]
    [InlineData("_Id9")]
    [InlineData("Source.Id")]
    [InlineData("Source.Owner.Id")]
    [InlineData("Δelta")]
    [InlineData("源.識別")]
    public void Repeated_field_path_validation_does_not_allocate(string path)
    {
        var filter = CreateFilter(path);
        for (var i = 0; i < 1_000; i++)
        {
            QueryFilterEvaluator.EnsureWithinLimits(filter);
        }

        const int iterations = 1_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            QueryFilterEvaluator.EnsureWithinLimits(filter);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{path}: {allocated / iterations} bytes per validation.");
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".Id")]
    [InlineData("Source.")]
    [InlineData("Source..Id")]
    [InlineData("9Id")]
    [InlineData("Source.9Id")]
    [InlineData("Source. Id")]
    [InlineData("Source.Id ")]
    [InlineData("Source.Id-2")]
    public void Invalid_identifier_segments_remain_rejected(string path)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            QueryFilterEvaluator.EnsureWithinLimits(CreateFilter(path)));

        Assert.Contains("Field path", error.Message, StringComparison.Ordinal);
    }

    private static QueryFilter CreateFilter(string path) => new()
    {
        Kind = QueryFilterKind.Compare,
        Field = path,
        Operator = QueryComparisonOperator.Equal,
        Value = QueryValue.FromString("alice")
    };
}
