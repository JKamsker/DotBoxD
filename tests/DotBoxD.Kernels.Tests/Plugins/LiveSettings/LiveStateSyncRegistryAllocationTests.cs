using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class LiveStateSyncRegistryAllocationTests
{
    private const int WarmupIterations = 20_000;
    private const int MeasurementIterations = 100_000;
    private const double MeasurementNoiseBytesPerCall = 0.1;
    private static readonly Type[] StateTypes =
    [
        typeof(State1),
        typeof(State2),
        typeof(State3),
        typeof(State4),
        typeof(State5),
        typeof(State6),
        typeof(State7),
        typeof(State8)
    ];

    [Theory]
    [InlineData(LiveUpdateMode.Sync, 1, 32, 0)]
    [InlineData(LiveUpdateMode.Sync, 8, 88, 0)]
    [InlineData(LiveUpdateMode.AsyncSet, 1, 120, 88)]
    [InlineData(LiveUpdateMode.AsyncSet, 8, 264, 176)]
    public void Input_sync_allocations_match_the_snapshot_contract(
        LiveUpdateMode updateMode,
        int synchronizerCount,
        int historicalBytesPerCall,
        int expectedBytesPerCall)
    {
        var registry = CreateRegistry(updateMode, synchronizerCount);
        _ = Measure(registry, WarmupIterations);

        var measurement = Measure(registry, MeasurementIterations);

        var expectedDeferredUpdates = updateMode == LiveUpdateMode.AsyncSet
            ? checked((long)synchronizerCount * MeasurementIterations)
            : 0;
        var bytesPerCall = measurement.AllocatedBytes / (double)MeasurementIterations;
        var expectedBytesSaved = synchronizerCount == 1 ? 32 : 88;
        var bytesSaved = historicalBytesPerCall - bytesPerCall;
        Assert.Equal(expectedDeferredUpdates, measurement.DeferredUpdateCount);
        Assert.InRange(
            bytesPerCall,
            expectedBytesPerCall,
            expectedBytesPerCall + MeasurementNoiseBytesPerCall);
        Assert.InRange(
            bytesSaved,
            expectedBytesSaved - MeasurementNoiseBytesPerCall,
            expectedBytesSaved + MeasurementNoiseBytesPerCall);
    }

    private static LiveStateSyncRegistry CreateRegistry(LiveUpdateMode updateMode, int synchronizerCount)
    {
        var registry = new LiveStateSyncRegistry(_ => updateMode);
        for (var i = 0; i < synchronizerCount; i++)
        {
            registry.Register(StateTypes[i], NoOp);
        }

        return registry;
    }

    private static Measurement Measure(LiveStateSyncRegistry registry, int iterations)
    {
        long deferredUpdateCount = 0;
        IReadOnlyList<Action>? deferredUpdates = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            deferredUpdates = registry.SynchronizeForInput();
            deferredUpdateCount += deferredUpdates.Count;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        GC.KeepAlive(deferredUpdates);
        return new Measurement(allocated, deferredUpdateCount);
    }

    private static void NoOp()
    {
    }

    private readonly record struct Measurement(long AllocatedBytes, long DeferredUpdateCount);

    private sealed class State1;
    private sealed class State2;
    private sealed class State3;
    private sealed class State4;
    private sealed class State5;
    private sealed class State6;
    private sealed class State7;
    private sealed class State8;
}
