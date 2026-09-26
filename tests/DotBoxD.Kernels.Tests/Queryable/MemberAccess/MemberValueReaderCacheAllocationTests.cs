using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MemberValueReaderCacheAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Value")]
    [InlineData("Field")]
    [InlineData("Child.Value")]
    public void Warm_member_path_reads_remain_allocation_free(string path)
    {
        var reader = new MemberValueReader();
        var value = new EventState();
        Assert.Equal("value", reader.Read(value, path));
        _ = Measure(reader, value, path);

        var bytes = Measure(reader, value, path);

        output.WriteLine($"{path}: {bytes / 1000D} B/read");
        Assert.Equal(0, bytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(MemberValueReader reader, object value, string path)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            GC.KeepAlive(reader.Read(value, path));
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class EventState
    {
        public string Value => "value";
        public readonly string Field = "value";
        public ChildState Child { get; } = new();
    }

    private sealed class ChildState
    {
        public string Value => "value";
    }
}
