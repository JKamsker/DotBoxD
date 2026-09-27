using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Runtime;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledAwaitPumpScopeAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Installing_and_restoring_the_thread_pump_does_not_allocate()
    {
        var pump = new CompiledAwaitPumpScopeFixture.Pump();
        InstallScopes(pump, 2_000);
        var minimum = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            InstallScopes(pump, 10_000);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"Pump scope installation/disposal: {minimum / 10_000d} B/call");
        Assert.Equal(0, minimum);
        Assert.Equal(0, pump.DisposeCalls);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void InstallScopes(ICompiledAwaitPump pump, int count)
    {
        for (var index = 0; index < count; index++)
        {
            using var scope = CompiledBindingDispatcher.InstallAwaitPump(pump);
        }
    }
}
