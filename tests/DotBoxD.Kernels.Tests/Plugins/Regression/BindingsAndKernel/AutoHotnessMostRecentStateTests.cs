using System.Runtime.CompilerServices;
using DotBoxD.Hosting.Internal;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

public sealed class AutoHotnessMostRecentStateTests
{
    [Fact]
    public async Task Concurrent_exact_reference_hits_share_complete_history()
    {
        const int attempts = 512;
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var plan = await PreparePlanAsync();
        var result = SuccessfulResult(plan.PlanHash);
        var runCounts = new int[attempts];

        Parallel.For(
            0,
            attempts,
            index =>
            {
                var attempt = hotness.BeginRunCountAttempt(plan, "main");
                runCounts[index] = attempt.RunCount;
                attempt.Complete(result, TimeSpan.FromTicks(2));
            });

        var snapshot = hotness.BeginAttempt(plan, "main").Stats;
        Assert.Equal(Enumerable.Range(1, attempts), runCounts.Order());
        Assert.Equal(attempts + 1, snapshot.RunCount);
        Assert.Equal(attempts, snapshot.CompletedRunCount);
        Assert.Equal(7, snapshot.AverageFuelUsed);
        Assert.Equal(TimeSpan.FromTicks(2), snapshot.AverageInterpretedDuration);
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public async Task Exact_most_recent_hits_preserve_lru_eviction_order()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var plan = await PreparePlanAsync();

        Assert.Equal(1, hotness.BeginRunCountAttempt(plan, "a").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(plan, "b").RunCount);
        Assert.Equal(2, hotness.BeginRunCountAttempt(plan, "b").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(plan, "c").RunCount);

        Assert.Equal(3, hotness.BeginRunCountAttempt(plan, "b").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(plan, "a").RunCount);
        Assert.Equal(2, hotness.Count);
    }

    [Fact]
    public async Task Evicted_completion_does_not_replace_the_published_most_recent_state()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 1);
        var plan = await PreparePlanAsync();
        var evicted = hotness.BeginRunCountAttempt(plan, "a");

        _ = hotness.BeginRunCountAttempt(plan, "b");
        evicted.Complete(SuccessfulResult(plan.PlanHash), TimeSpan.FromTicks(1));
        var recreated = hotness.BeginAttempt(plan, "a").Stats;

        Assert.Equal(1, recreated.RunCount);
        Assert.Equal(0, recreated.CompletedRunCount);
        Assert.Null(recreated.LastRunAt);
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public async Task Published_most_recent_state_does_not_retain_an_evicted_key()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 1);
        var plan = await PreparePlanAsync();
        var evictedEntrypoint = AddAndEvictUniqueEntrypoint(hotness, plan);

        CollectFully();

        Assert.False(evictedEntrypoint.TryGetTarget(out _));
        Assert.Equal(1, hotness.Count);
        GC.KeepAlive(hotness);
        GC.KeepAlive(plan);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<string> AddAndEvictUniqueEntrypoint(
        AutoExecutionHotness hotness,
        ExecutionPlan plan)
    {
        var uniqueEntrypoint = new string("evicted-entrypoint".ToCharArray());
        var weak = new WeakReference<string>(uniqueEntrypoint);
        _ = hotness.BeginRunCountAttempt(plan, uniqueEntrypoint);
        _ = hotness.BeginRunCountAttempt(plan, "replacement-entrypoint");
        return weak;
    }

    private static async Task<ExecutionPlan> PreparePlanAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson("auto-hot-state"));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxExecutionResult SuccessfulResult(string planHash)
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new SandboxResourceUsage(7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditEvents = [],
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = planHash,
            PolicyHash = "policy"
        };

    private static void CollectFully()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
