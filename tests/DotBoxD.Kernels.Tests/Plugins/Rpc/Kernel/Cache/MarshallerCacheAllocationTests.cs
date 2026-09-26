using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MarshallerCacheAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("dto", false, 24)]
    [InlineData("dto", true, 24)]
    [InlineData("list", false, 120)]
    [InlineData("list", true, 88)]
    [InlineData("readonly-list", false, 144)]
    [InlineData("readonly-list", true, 112)]
    [InlineData("map", false, 240)]
    [InlineData("map", true, 240)]
    [InlineData("readonly-map", false, 280)]
    [InlineData("readonly-map", true, 280)]
    public void Warm_marshalling_cache_preserves_allocation_budget(string shape, bool wireValue, int bytesPerCall)
    {
        var (value, type) = MarshallerCacheFixture.Value(shape, typeof(MarshallerCacheState));
        var sandbox = KernelRpcMarshaller.ToSandboxValue(value, type);
        var wire = KernelRpcValueConverter.FromSandboxValue(sandbox);
        _ = Measure(sandbox, wire, type, wireValue);
        var allocated = Measure(sandbox, wire, type, wireValue);
        output.WriteLine($"{shape}, wire={wireValue}: {allocated / 1000D} bytes per call");
        Assert.InRange(allocated, 0, 1000L * bytesPerCall);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(SandboxValue sandbox, KernelRpcValue wire, Type type, bool wireValue)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var result = wireValue
                ? KernelRpcMarshaller.FromKernelRpcValue(wire, type)
                : KernelRpcMarshaller.FromSandboxValue(sandbox, type);
            GC.KeepAlive(result);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
