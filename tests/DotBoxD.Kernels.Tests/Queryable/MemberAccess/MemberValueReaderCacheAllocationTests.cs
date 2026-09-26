using System.Runtime.CompilerServices;
using DotBoxD.Queryable.Execution;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Queryable;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MemberValueReaderCacheAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Value", false)]
    [InlineData("Field", false)]
    [InlineData("Child.Value", false)]
    [InlineData("Value", true)]
    [InlineData("Field", true)]
    [InlineData("Child.Value", true)]
    public void Warm_member_path_reads_remain_allocation_free(string path, bool declaredType)
    {
        var reader = declaredType ? new MemberValueReader(typeof(EventState)) : new MemberValueReader();
        var value = new EventState();
        Assert.Equal("value", reader.Read(value, path));
        _ = Measure(reader, value, path);

        var bytes = Measure(reader, value, path);

        output.WriteLine($"{path}, declaredType={declaredType}: {bytes / 1000D} B/read");
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
