using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class CompiledNoAuditRunStateSecondaryLaneTests
{
    [Fact]
    public async Task Secondary_is_lazy_bounded_and_generation_safe()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "secondary-lazy");
        using var pool = new CompiledNoAuditRunStatePool();

        using (var sequential = pool.TryAcquire(plan))
        {
            Assert.True(sequential.IsAcquired);
        }

        Assert.False(pool.HasSecondaryStateFor(plan));

        var primary = pool.TryAcquire(plan);
        var primaryState = Assert.IsType<CompiledNoAuditRunState>(primary.State);
        var secondary = pool.TryAcquire(plan);
        var secondaryState = Assert.IsType<CompiledNoAuditRunState>(secondary.State);
        var staleSecondary = secondary;
        Assert.NotSame(primaryState, secondaryState);
        Assert.True(pool.HasSecondaryStateFor(plan));
        using (var overflow = pool.TryAcquire(plan))
        {
            Assert.False(overflow.IsAcquired);
        }

        secondary.Dispose();
        var nextSecondary = pool.TryAcquire(plan);
        Assert.Same(secondaryState, nextSecondary.State);
        staleSecondary.Dispose();
        Assert.True(nextSecondary.IsAcquired);
        using (var overflow = pool.TryAcquire(plan))
        {
            Assert.False(overflow.IsAcquired);
        }

        nextSecondary.Dispose();
        primary.Dispose();
    }

    [Fact]
    public async Task Disposal_defers_both_active_lane_states_to_their_matching_releases()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "secondary-disposal");
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        var pool = new CompiledNoAuditRunStatePool();
        var primary = pool.TryAcquire(plan);
        var secondary = pool.TryAcquire(plan);
        var primaryState = Assert.IsType<CompiledNoAuditRunState>(primary.State);
        var secondaryState = Assert.IsType<CompiledNoAuditRunState>(secondary.State);
        _ = primaryState.ContextFor(allowedBindings, CancellationToken.None);
        _ = secondaryState.ContextFor(allowedBindings, CancellationToken.None);

        pool.Dispose();

        Assert.True(primary.IsAcquired);
        Assert.True(secondary.IsAcquired);
        Assert.False(pool.HasStateFor(plan));
        using (var late = pool.TryAcquire(plan))
        {
            Assert.False(late.IsAcquired);
        }

        primary.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => primaryState.ContextFor(allowedBindings, CancellationToken.None));
        _ = secondaryState.ContextFor(allowedBindings, CancellationToken.None);
        secondary.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => secondaryState.ContextFor(allowedBindings, CancellationToken.None));
    }

    [Fact]
    public async Task Eviction_rotates_past_a_secondary_only_active_plan()
    {
        using var host = SandboxTestHost.Create();
        var plans = await PrepareManyAsync(host, CompiledNoAuditRunStatePool.Capacity + 1, "secondary-active");
        using var pool = new CompiledNoAuditRunStatePool();
        var primary = pool.TryAcquire(plans[0]);
        var secondary = pool.TryAcquire(plans[0]);
        primary.Dispose();

        for (var i = 1; i < CompiledNoAuditRunStatePool.Capacity; i++)
        {
            using var admitted = pool.TryAcquire(plans[i]);
            Assert.True(admitted.IsAcquired);
        }

        using var replacement = pool.TryAcquire(plans[^1]);

        Assert.True(replacement.IsAcquired);
        Assert.True(pool.HasStateFor(plans[0]));
        Assert.False(pool.HasStateFor(plans[1]));
        Assert.True(secondary.IsAcquired);
        secondary.Dispose();
    }

    [Fact]
    public async Task Idle_two_lane_eviction_disposes_both_states()
    {
        using var host = SandboxTestHost.Create();
        var plans = await PrepareManyAsync(host, CompiledNoAuditRunStatePool.Capacity + 1, "secondary-eviction");
        Assert.True(plans[0].BindingReferences.TryGetValue("main", out var allowedBindings));
        using var pool = new CompiledNoAuditRunStatePool();
        var primary = pool.TryAcquire(plans[0]);
        var secondary = pool.TryAcquire(plans[0]);
        var primaryState = Assert.IsType<CompiledNoAuditRunState>(primary.State);
        var secondaryState = Assert.IsType<CompiledNoAuditRunState>(secondary.State);
        _ = primaryState.ContextFor(allowedBindings, CancellationToken.None);
        _ = secondaryState.ContextFor(allowedBindings, CancellationToken.None);
        primary.Dispose();
        secondary.Dispose();

        for (var i = 1; i < CompiledNoAuditRunStatePool.Capacity; i++)
        {
            using var admitted = pool.TryAcquire(plans[i]);
            Assert.True(admitted.IsAcquired);
        }

        using var replacement = pool.TryAcquire(plans[^1]);

        Assert.True(replacement.IsAcquired);
        Assert.False(pool.HasStateFor(plans[0]));
        Assert.Throws<ObjectDisposedException>(
            () => primaryState.ContextFor(allowedBindings, CancellationToken.None));
        Assert.Throws<ObjectDisposedException>(
            () => secondaryState.ContextFor(allowedBindings, CancellationToken.None));
    }

    [Fact]
    public async Task Secondary_admission_racing_disposal_never_leaves_a_live_state()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "secondary-dispose-race");
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var pool = new CompiledNoAuditRunStatePool();
            var primary = pool.TryAcquire(plan);
            var primaryState = Assert.IsType<CompiledNoAuditRunState>(primary.State);
            _ = primaryState.ContextFor(allowedBindings, CancellationToken.None);
            CompiledNoAuditRunStatePool.Lease secondary = default;
            using var start = new ManualResetEventSlim();
            var acquire = Task.Run(() =>
            {
                start.Wait();
                secondary = pool.TryAcquire(plan);
            });
            var dispose = Task.Run(() =>
            {
                start.Wait();
                pool.Dispose();
            });

            start.Set();
            await Task.WhenAll(acquire, dispose);

            Assert.False(pool.HasStateFor(plan));
            var secondaryState = secondary.State;
            secondary.Dispose();
            if (secondaryState is not null)
            {
                Assert.Throws<ObjectDisposedException>(
                    () => secondaryState.ContextFor(allowedBindings, CancellationToken.None));
            }

            primary.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => primaryState.ContextFor(allowedBindings, CancellationToken.None));
            pool.Dispose();
        }
    }

    private static async Task<ExecutionPlan[]> PrepareManyAsync(SandboxHost host, int count, string prefix)
    {
        var plans = new ExecutionPlan[count];
        for (var i = 0; i < plans.Length; i++)
        {
            plans[i] = await PrepareAsync(host, $"{prefix}-{i}");
        }

        return plans;
    }

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string id)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson(id));
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
    }
}
