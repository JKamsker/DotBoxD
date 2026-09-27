using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledAwaitPumpAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 168)]
    [InlineData(true, 336)]
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
        for (var index = 0; index < 2_000; index++)
        {
            disposed.Dispose();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            disposed.Dispose();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Repeated pump disposal: {allocated / 10_000d} B/call");
        Assert.Equal(0, allocated);
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
