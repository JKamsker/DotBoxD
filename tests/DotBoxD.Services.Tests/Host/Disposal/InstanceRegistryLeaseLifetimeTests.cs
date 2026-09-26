using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryLeaseLifetimeTests
{
    [Theory]
    [InlineData("Object", false)]
    [InlineData("Object", true)]
    [InlineData("Sync", false)]
    [InlineData("Sync", true)]
    [InlineData("Async", false)]
    [InlineData("Async", true)]
    public async Task Retained_disposed_leases_release_instances(string kind, bool releaseRegistrationFirst)
    {
        var fixture = CreateCompleted(kind, releaseRegistrationFirst, retainLease: true);

        await AssertCollected(fixture.Instance);

        Assert.Equal(kind == "Object" ? 0 : 1, fixture.State.Calls);
        await fixture.Lease!.DisposeAsync();
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Discarded_disposed_leases_release_instances()
    {
        var fixture = CreateCompleted("Async", releaseRegistrationFirst: true, retainLease: false);
        await AssertCollected(fixture.Instance);
        Assert.Equal(1, fixture.State.Calls);
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Outstanding_leases_preserve_instances_after_registration_release()
    {
        var fixture = CreateAcquired("Sync");
        fixture.Registry.Release("service", fixture.Id);
        Collect();

        Assert.True(fixture.Instance.IsAlive);
        Assert.Equal(0, fixture.State.Calls);
        await fixture.Lease!.DisposeAsync();
        Assert.Equal(1, fixture.State.Calls);
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_cleanup_preserves_instance_until_completion_then_releases_it(bool fail)
    {
        var fixture = CreateAcquired("Async", pause: true, fail);
        fixture.Registry.Release("service", fixture.Id);
        fixture.Completion = fixture.Lease!.DisposeAsync().AsTask();
        Assert.False(fixture.Completion.IsCompleted);
        Collect();
        Assert.True(fixture.Instance.IsAlive);
        Assert.Equal(1, fixture.State.Calls);
        await fixture.Lease.DisposeAsync();

        fixture.State.Proceed.SetResult(true);
        if (fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Completion);
        }
        else
        {
            await fixture.Completion;
        }

        fixture.Completion = null;
        await AssertCollected(fixture.Instance);
        await fixture.Lease.DisposeAsync();
        Assert.Equal(1, fixture.State.Calls);
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData("Sync")]
    [InlineData("Async")]
    public async Task Concurrent_lease_returns_dispose_once(string kind)
    {
        var fixture = CreateAcquired(kind);
        fixture.Registry.Release("service", fixture.Id);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returns = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await fixture.Lease!.DisposeAsync();
        })).ToArray();

        start.SetResult(true);
        await Task.WhenAll(returns);
        await fixture.Lease!.DisposeAsync();

        Assert.Equal(1, fixture.State.Calls);
        Assert.False(fixture.Registry.TryGet("service", fixture.Id, out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Fixture CreateCompleted(string kind, bool releaseRegistrationFirst, bool retainLease)
    {
        var fixture = CreateAcquired(kind);
        if (releaseRegistrationFirst)
        {
            fixture.Registry.Release("service", fixture.Id);
        }

        fixture.Lease!.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (!releaseRegistrationFirst)
        {
            fixture.Registry.ReleaseAsync("service", fixture.Id).AsTask().GetAwaiter().GetResult();
        }

        if (!retainLease)
        {
            fixture.Lease = null;
        }

        return fixture;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Fixture CreateAcquired(string kind, bool pause = false, bool fail = false)
    {
        var registry = new InstanceRegistry();
        var state = new DisposalState { Fail = fail };
        if (!pause)
        {
            state.Proceed.SetResult(true);
        }

        object instance = kind switch
        {
            "Object" => new object(),
            "Sync" => new SyncInstance(state),
            "Async" => new AsyncInstance(state),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var id = registry.Register("service", instance);
        Assert.True(registry.TryAcquire("service", id, out var acquired, out var lease));
        Assert.Same(instance, acquired);
        return new Fixture(registry, id, new WeakReference(instance), lease, state);
    }

    private static async Task AssertCollected(WeakReference reference)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            Collect();
            if (!reference.IsAlive)
            {
                return;
            }

            await Task.Delay(10);
        }
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5));

        Assert.False(reference.IsAlive, "A disposed lease must not retain a released instance after cleanup completes.");
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class Fixture(InstanceRegistry registry, string id, WeakReference instance, IAsyncDisposable lease, DisposalState state)
    {
        public InstanceRegistry Registry { get; } = registry;
        public string Id { get; } = id;
        public WeakReference Instance { get; } = instance;
        public IAsyncDisposable? Lease { get; set; } = lease;
        public DisposalState State { get; } = state;
        public Task? Completion { get; set; }
    }

    private sealed class DisposalState
    {
        public int Calls;
        public bool Fail { get; init; }
        public TaskCompletionSource<bool> Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SyncInstance(DisposalState state) : IDisposable
    {
        public void Dispose() => Interlocked.Increment(ref state.Calls);
    }

    private sealed class AsyncInstance(DisposalState state) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref state.Calls);
            await state.Proceed.Task;
            GC.KeepAlive(this);
            if (state.Fail)
            {
                throw new InvalidOperationException("Disposal failed.");
            }
        }
    }
}
