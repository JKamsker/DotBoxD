using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledAwaitPumpAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 144)]
    [InlineData(true, 312)]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void Inline_cleanup_keeps_only_necessary_snapshot_allocations(bool queued, int bytesPerCall)
    {
        var result = CompiledAwaitPumpFixture.Result();
        Func<SandboxExecutionResult> execute = () =>
        {
            if (queued)
            {
                SynchronizationContext.Current!.Post(static _ => { }, null);
            }
            return result;
        };
        _ = Run(execute, result, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var completed = Run(execute, result, 10_000);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(10_000, completed);
            minimum = Math.Min(minimum, allocated);
        }

        output.WriteLine($"Inline worker queued={queued}: {minimum / 10_000d} B/call");
        Assert.InRange(minimum, 1, bytesPerCall * 10_000L);
    }

    [Fact]
    public void Repeated_disposal_does_not_allocate()
    {
        var probe = CompiledAwaitPumpFixture.CreateDisposedQueue(0);
        var disposed = (IDisposable)probe.Context;
        _ = MeasureRepeatedDisposal(disposed, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            minimum = Math.Min(minimum, MeasureRepeatedDisposal(disposed, 10_000));
        }

        output.WriteLine($"Repeated pump disposal: {minimum / 10_000d} B/call");
        Assert.Equal(0, minimum);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long MeasureRepeatedDisposal(IDisposable disposed, int count)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < count; index++)
        {
            disposed.Dispose();
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static int Run(Func<SandboxExecutionResult> execute, SandboxExecutionResult expected, int count)
    {
        var completed = 0;
        for (var index = 0; index < count; index++)
        {
            if (ReferenceEquals(expected, CompiledAsyncWorker.RunInline(execute)))
            {
                completed++;
            }
        }
        return completed;
    }
}
