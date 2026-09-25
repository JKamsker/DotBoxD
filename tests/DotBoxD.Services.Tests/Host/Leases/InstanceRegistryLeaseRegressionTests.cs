using DotBoxD.Services.Server;
using Xunit;
using Xunit.Abstractions;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryLeaseRegressionTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "AllocationMeasurement")]
    public async Task ReturningLease_AllocationsDoNotScaleWithRegistrySize()
    {
        const int iterations = 1_000;
        var small = await MeasureLeaseAllocationsAsync(1, iterations);
        var large = await MeasureLeaseAllocationsAsync(512, iterations);
        output.WriteLine($"Bytes per acquire/release: 1 registration = {small / iterations}; 512 registrations = {large / iterations}.");

        Assert.True(large <= small + 16 * iterations,
            $"Lease allocation grew with registry size: {small} bytes versus {large} bytes.");
    }

    [Fact]
    public async Task ReturningLease_KeepsRemainingRegistrationAlive()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingDisposable();
        var first = registry.Register("first", instance);
        var second = registry.Register("second", instance);
        Assert.True(registry.TryAcquire("first", first, out _, out var lease));

        registry.Release("first", first);
        await lease.DisposeAsync();

        Assert.Equal(0, instance.DisposeCalls);
        Assert.True(registry.TryGet("second", second, out var remaining));
        Assert.Same(instance, remaining);
        registry.ReleaseAll();
        Assert.Equal(1, instance.DisposeCalls);
    }

    [Fact]
    public async Task ReturningLastLease_DisposesPendingInstanceOnce()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingDisposable();
        var id = registry.Register("svc", instance);
        Assert.True(registry.TryAcquire("svc", id, out _, out var first));
        Assert.True(registry.TryAcquire("svc", id, out _, out var second));

        registry.Release("svc", id);
        await first.DisposeAsync();
        Assert.Equal(0, instance.DisposeCalls);

        await second.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal(1, instance.DisposeCalls);
    }

    [Fact]
    public async Task PendingLeaseDisposal_RejectsReregistration()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingDisposable();
        var id = registry.Register("svc", instance);
        Assert.True(registry.TryAcquire("svc", id, out _, out var lease));

        registry.Release("svc", id);

        Assert.Throws<InvalidOperationException>(() => registry.Register("other", instance));
        await lease.DisposeAsync();
        Assert.Equal(1, instance.DisposeCalls);
    }

    private static async Task<long> MeasureLeaseAllocationsAsync(int registrations, int iterations)
    {
        var registry = new InstanceRegistry();
        var id = registry.Register("svc", new object());
        for (var i = 1; i < registrations; i++)
        {
            registry.Register("svc", new object());
        }

        try
        {
            for (var i = 0; i < 100; i++)
            {
                await AcquireAndReturnAsync(registry, id);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
            {
                await AcquireAndReturnAsync(registry, id);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally
        {
            registry.ReleaseAll();
        }
    }

    private static ValueTask AcquireAndReturnAsync(InstanceRegistry registry, string id)
    {
        if (!registry.TryAcquire("svc", id, out _, out var lease))
        {
            throw new InvalidOperationException("Registered instance was lost.");
        }

        var completion = lease.DisposeAsync();
        if (!completion.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Returning a live instance lease should complete synchronously.");
        }

        return completion;
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }
}
