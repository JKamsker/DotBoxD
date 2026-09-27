using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Ast;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class QueryBooleanValueAllocationTests(ITestOutputHelper output)
{
    private volatile QueryValue? _observed;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Boolean_value_factories_do_not_allocate_per_call(bool value, bool fromObject)
    {
        object boxed = value;
        Func<QueryValue> factory = fromObject ? () => FromObject(boxed) : () => QueryValue.FromBoolean(value);
        _ = Measure(factory);

        var bytes = Measure(factory);

        var observed = Assert.IsType<QueryValue>(_observed);
        Assert.Equal(QueryValueKind.Boolean, observed.Kind);
        Assert.Equal(value, observed.Boolean);
        Assert.Null(observed.ParameterKey);
        output.WriteLine($"value={value}, fromObject={fromObject}: {bytes / 1000D} B/call.");
        Assert.Equal(0, bytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private long Measure(Func<QueryValue> factory)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _observed = factory();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static QueryValue FromObject(object value)
        => QueryValue.TryFromObject(value, out var result) ? result : throw new InvalidOperationException("Expected a boolean value.");
}
