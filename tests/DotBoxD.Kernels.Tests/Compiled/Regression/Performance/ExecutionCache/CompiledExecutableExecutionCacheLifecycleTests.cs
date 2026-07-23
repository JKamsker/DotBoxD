using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.ExecutionCache;

public sealed class CompiledExecutableExecutionCacheLifecycleTests
{
    [Fact]
    public async Task Failed_materialization_is_removed_before_a_completed_entry_can_publish()
    {
        var (plan, executable) = await CreateFixtureAsync();
        using var cache = new CompiledExecutableExecutionCache();
        var hotEntry = cache.GetOrCreateHotEntry();
        var calls = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await cache.GetAsync(
                plan,
                "main",
                _ =>
                {
                    calls++;
                    return ValueTask.FromException<CompiledExecutable>(
                        new InvalidOperationException("materialization failed"));
                },
                CancellationToken.None));

        Assert.Equal("materialization failed", failure.Message);
        Assert.False(cache.TryPublishMostRecentCompletedExact(plan, "main", hotEntry));
        var recovered = await cache.GetAsync(
            plan,
            "main",
            _ =>
            {
                calls++;
                return ValueTask.FromResult(executable);
            },
            CancellationToken.None);
        Assert.Equal("Miss", recovered.MaterializationStatus);
        Assert.Equal(2, calls);
        Assert.True(cache.TryPublishMostRecentCompletedExact(plan, "main", hotEntry));
        Assert.True(hotEntry.TryGet(plan, "main", out _));
    }

    [Fact]
    public async Task Evicting_the_primary_publication_promotes_the_secondary()
    {
        var (plan, executable) = await CreateFixtureAsync();
        using var cache = new CompiledExecutableExecutionCache();
        var hotEntry = cache.GetOrCreateHotEntry();
        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(executable),
            CancellationToken.None);
        Assert.True(cache.TryPublishMostRecentCompletedExact(plan, "main", hotEntry));
        _ = await cache.GetAsync(
            plan,
            "secondary",
            _ => ValueTask.FromResult(executable),
            CancellationToken.None);
        Assert.True(cache.TryPublishMostRecentCompletedExact(plan, "secondary", hotEntry));
        Assert.True(hotEntry.TryGet(plan, "main", out _));
        Assert.True(hotEntry.TryGet(plan, "secondary", out _));

        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(executable),
            CancellationToken.None);
        for (var i = 0; i < 63; i++)
        {
            _ = await cache.GetAsync(
                plan,
                $"other-{i}",
                _ => ValueTask.FromResult(executable),
                CancellationToken.None);
        }

        Assert.True(hotEntry.TryGet(plan, "main", out _));
        Assert.False(hotEntry.TryGet(plan, "secondary", out _));
    }

    [Fact]
    public async Task Lock_free_completed_read_is_atomic_while_entry_is_evicted()
    {
        var (plan, executable) = await CreateFixtureAsync();
        using var cache = new CompiledExecutableExecutionCache();
        var hotEntry = cache.GetOrCreateHotEntry();
        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(executable),
            CancellationToken.None);
        Assert.True(cache.TryPublishMostRecentCompletedExact(plan, "main", hotEntry));
        using var start = new ManualResetEventSlim();
        var reader = Task.Run(() => ReadUntilInvalidated(hotEntry, plan, start));

        start.Set();
        for (var i = 0; i < 64; i++)
        {
            _ = await cache.GetAsync(
                plan,
                $"other-{i}",
                _ => ValueTask.FromResult(executable),
                CancellationToken.None);
        }

        await reader;
        Assert.False(hotEntry.TryGet(plan, "main", out _));
    }

    [Fact]
    public async Task Lock_free_completed_read_is_atomic_while_cache_is_disposed()
    {
        var (plan, executable) = await CreateFixtureAsync();
        var cache = new CompiledExecutableExecutionCache();
        var hotEntry = cache.GetOrCreateHotEntry();
        _ = await cache.GetAsync(
            plan,
            "main",
            _ => ValueTask.FromResult(executable),
            CancellationToken.None);
        Assert.True(cache.TryPublishMostRecentCompletedExact(plan, "main", hotEntry));
        using var start = new ManualResetEventSlim();
        var reader = Task.Run(() => ReadUntilInvalidated(hotEntry, plan, start));

        start.Set();
        cache.Dispose();

        await reader;
        Assert.False(hotEntry.TryGet(plan, "main", out _));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await cache.GetAsync(
                plan,
                "main",
                _ => ValueTask.FromResult(executable),
                CancellationToken.None));
    }

    private static void ReadUntilInvalidated(
        CompiledExecutableHotEntry hotEntry,
        ExecutionPlan plan,
        ManualResetEventSlim start)
    {
        start.Wait();
        for (var i = 0; i < 100_000; i++)
        {
            if (!hotEntry.TryGet(plan, "main", out var executable))
            {
                return;
            }

            if (executable.Artifact is null ||
                !StringComparer.Ordinal.Equals(executable.MaterializationStatus, "Hit"))
            {
                throw new InvalidOperationException("Completed executable was observed partially published.");
            }
        }
    }

    private static async Task<(ExecutionPlan Plan, CompiledExecutable Executable)> CreateFixtureAsync()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson("completed-entry-lifecycle"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var artifact = CompiledArtifactTestFactory.DynamicMethod(
            plan,
            static (_, _) => SandboxValue.FromInt32(35));
        return (plan, new CompiledExecutable(artifact, "Miss"));
    }
}
