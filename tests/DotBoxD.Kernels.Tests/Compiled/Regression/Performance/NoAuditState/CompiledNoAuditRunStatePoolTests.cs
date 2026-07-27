using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class CompiledNoAuditRunStatePoolTests
{
    [Fact]
    public async Task Same_plan_reuses_two_exclusive_states_and_bounds_the_third_overlap()
    {
        using var host = SandboxTestHost.Create();
        var firstPlan = await PrepareAsync(host, "no-audit-pool-first");
        var secondPlan = await PrepareAsync(host, "no-audit-pool-second");
        using var pool = new CompiledNoAuditRunStatePool();
        var first = pool.TryAcquire(firstPlan);
        Assert.True(first.IsAcquired);
        var firstState = Assert.IsType<CompiledNoAuditRunState>(first.State);
        CompiledNoAuditRunState secondState;
        try
        {
            using var second = pool.TryAcquire(firstPlan);
            Assert.True(second.IsAcquired);
            secondState = Assert.IsType<CompiledNoAuditRunState>(second.State);
            Assert.NotSame(firstState, secondState);
            using var reentrant = pool.TryAcquire(firstPlan);
            Assert.False(reentrant.IsAcquired);
            Assert.Null(reentrant.State);
            var concurrentAcquired = await Task.Run(() =>
            {
                using var concurrent = pool.TryAcquire(firstPlan);
                return concurrent.IsAcquired;
            });
            Assert.False(concurrentAcquired);
            using var independent = pool.TryAcquire(secondPlan);
            Assert.True(independent.IsAcquired);
            Assert.NotSame(firstState, independent.State);
        }
        finally
        {
            first.Dispose();
        }

        using var reacquired = pool.TryAcquire(firstPlan);
        Assert.True(reacquired.IsAcquired);
        Assert.Same(firstState, reacquired.State);
        using var secondaryReacquired = pool.TryAcquire(firstPlan);
        Assert.True(secondaryReacquired.IsAcquired);
        Assert.Same(secondState, secondaryReacquired.State);
    }

    [Fact]
    public async Task Disposal_defers_active_state_cleanup_until_its_lease_releases()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "no-audit-pool-disposal");
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        var pool = new CompiledNoAuditRunStatePool();
        var active = pool.TryAcquire(plan);
        var state = Assert.IsType<CompiledNoAuditRunState>(active.State);
        _ = state.ContextFor(allowedBindings, CancellationToken.None);

        pool.Dispose();

        Assert.False(pool.HasStateFor(plan));
        Assert.True(active.IsAcquired);
        Assert.Same(state, active.State);
        using var late = pool.TryAcquire(plan);
        Assert.False(late.IsAcquired);
        Assert.Null(late.State);

        active.Dispose();
        Assert.False(active.IsAcquired);
        Assert.Throws<ObjectDisposedException>(
            () => state.ContextFor(allowedBindings, CancellationToken.None));
        pool.Dispose();
    }

    [Fact]
    public async Task Stale_lease_copy_cannot_release_a_later_generation()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "no-audit-pool-generation");
        using var pool = new CompiledNoAuditRunStatePool();
        var first = pool.TryAcquire(plan);
        var staleCopy = first;
        first.Dispose();

        var second = pool.TryAcquire(plan);
        Assert.True(second.IsAcquired);
        staleCopy.Dispose();
        Assert.False(staleCopy.IsAcquired);
        Assert.Null(staleCopy.State);
        using var secondary = pool.TryAcquire(plan);
        Assert.True(secondary.IsAcquired);
        using var stillBusy = pool.TryAcquire(plan);
        Assert.False(stillBusy.IsAcquired);

        secondary.Dispose();
        second.Dispose();
        using var available = pool.TryAcquire(plan);
        Assert.True(available.IsAcquired);
    }

    [Fact]
    public async Task Eviction_marker_and_active_rental_are_mutually_exclusive()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "no-audit-pool-eviction-sentinel");
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        var evictedFirst = new CompiledNoAuditRunStateSlot(plan);
        var evictedState = evictedFirst.State;
        _ = evictedState.ContextFor(allowedBindings, CancellationToken.None);
        evictedState.StoreExecutable("main", new CompiledExecutable(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                static (_, _) => SandboxValue.FromInt32(7)),
            "Miss"));
        Assert.True(evictedState.TryGetExecutable("main", out _));
        Assert.True(evictedFirst.TryMarkEvicted());
        Assert.False(evictedFirst.TryTake(out _));
        Assert.False(evictedState.TryGetExecutable("main", out _));
        Assert.Throws<ObjectDisposedException>(
            () => evictedState.ContextFor(allowedBindings, CancellationToken.None));

        var rentedFirst = new CompiledNoAuditRunStateSlot(plan);
        Assert.True(rentedFirst.TryTake(out var generation));
        Assert.False(rentedFirst.TryMarkEvicted());
        rentedFirst.Release(generation);
        Assert.True(rentedFirst.TryMarkEvicted());
        rentedFirst.Release(generation);
        Assert.False(rentedFirst.TryTake(out _));
    }

    [Fact]
    public async Task Stale_generation_cannot_finish_a_newer_retiring_lease()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, "no-audit-pool-retiring-generation");
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        var slot = new CompiledNoAuditRunStateSlot(plan);
        Assert.True(slot.TryTake(out var firstGeneration));
        slot.Release(firstGeneration);
        Assert.True(slot.TryTake(out var secondGeneration));

        slot.Retire();
        slot.Release(firstGeneration);

        Assert.True(slot.IsHeldBy(secondGeneration));
        _ = slot.State.ContextFor(allowedBindings, CancellationToken.None);
        slot.Release(secondGeneration);
        Assert.False(slot.IsHeldBy(secondGeneration));
        Assert.Throws<ObjectDisposedException>(
            () => slot.State.ContextFor(allowedBindings, CancellationToken.None));
    }

    [Fact]
    public async Task Capacity_evicts_idle_slots_in_fifo_order_without_aliasing_states()
    {
        using var host = SandboxTestHost.Create();
        var plans = await PrepareManyAsync(host, CompiledNoAuditRunStatePool.Capacity + 2, "no-audit-pool-fifo");
        using var pool = new CompiledNoAuditRunStatePool();
        var states = new HashSet<CompiledNoAuditRunState>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < CompiledNoAuditRunStatePool.Capacity; i++)
        {
            using var admitted = pool.TryAcquire(plans[i]);
            Assert.True(admitted.IsAcquired);
            Assert.True(states.Add(Assert.IsType<CompiledNoAuditRunState>(admitted.State)));
        }

        using var firstReplacement = pool.TryAcquire(plans[^2]);
        Assert.True(firstReplacement.IsAcquired);
        Assert.True(states.Add(Assert.IsType<CompiledNoAuditRunState>(firstReplacement.State)));
        Assert.False(pool.HasStateFor(plans[0]));
        Assert.True(pool.HasStateFor(plans[1]));
        using var secondReplacement = pool.TryAcquire(plans[^1]);
        Assert.True(secondReplacement.IsAcquired);
        Assert.True(states.Add(Assert.IsType<CompiledNoAuditRunState>(secondReplacement.State)));
        Assert.False(pool.HasStateFor(plans[1]));
        Assert.True(pool.HasStateFor(plans[2]));
    }

    [Fact]
    public async Task Capacity_rotates_past_an_active_oldest_slot_without_evicting_it()
    {
        using var host = SandboxTestHost.Create();
        var plans = await PrepareManyAsync(host, CompiledNoAuditRunStatePool.Capacity + 1, "no-audit-pool-active");
        using var pool = new CompiledNoAuditRunStatePool();
        var active = pool.TryAcquire(plans[0]);
        var activeState = Assert.IsType<CompiledNoAuditRunState>(active.State);
        for (var i = 1; i < CompiledNoAuditRunStatePool.Capacity; i++)
        {
            using var admitted = pool.TryAcquire(plans[i]);
            Assert.True(admitted.IsAcquired);
        }

        using var replacement = pool.TryAcquire(plans[^1]);
        Assert.True(replacement.IsAcquired);
        Assert.NotSame(activeState, replacement.State);
        Assert.True(pool.HasStateFor(plans[0]));
        Assert.False(pool.HasStateFor(plans[1]));
        using var secondary = pool.TryAcquire(plans[0]);
        Assert.True(secondary.IsAcquired);
        Assert.NotSame(activeState, secondary.State);
        using var activeStillExclusive = pool.TryAcquire(plans[0]);
        Assert.False(activeStillExclusive.IsAcquired);

        secondary.Dispose();
        active.Dispose();
        using var reacquired = pool.TryAcquire(plans[0]);
        Assert.True(reacquired.IsAcquired);
        Assert.Same(activeState, reacquired.State);
    }

    [Fact]
    public async Task Capacity_falls_back_when_all_slots_are_active_then_recovers()
    {
        using var host = SandboxTestHost.Create();
        var plans = await PrepareManyAsync(host, CompiledNoAuditRunStatePool.Capacity + 1, "no-audit-pool-full");
        using var pool = new CompiledNoAuditRunStatePool();
        var leases = new CompiledNoAuditRunStatePool.Lease[CompiledNoAuditRunStatePool.Capacity];
        try
        {
            for (var i = 0; i < leases.Length; i++)
            {
                leases[i] = pool.TryAcquire(plans[i]);
                Assert.True(leases[i].IsAcquired);
            }

            using var overflow = pool.TryAcquire(plans[^1]);
            Assert.False(overflow.IsAcquired);
            Assert.False(pool.HasStateFor(plans[^1]));
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }

        using var recovered = pool.TryAcquire(plans[^1]);
        Assert.True(recovered.IsAcquired);
        Assert.False(pool.HasStateFor(plans[0]));
    }

    private static async Task<ExecutionPlan[]> PrepareManyAsync(SandboxHost host, int count, string idPrefix)
    {
        var plans = new ExecutionPlan[count];
        for (var i = 0; i < plans.Length; i++)
        {
            plans[i] = await PrepareAsync(host, $"{idPrefix}-{i}");
        }

        return plans;
    }

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string id)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson(id));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
    }
}
