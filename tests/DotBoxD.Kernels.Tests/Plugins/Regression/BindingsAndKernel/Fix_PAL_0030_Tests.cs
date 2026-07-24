using DotBoxD.Hosting.Internal;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.BindingsAndKernel;

/// <summary>
/// Regression coverage for PAL-0030: <see cref="AutoExecutionHotness"/> must not
/// retain one <c>AutoHotnessState</c> per <c>planHash|entrypoint</c> for the lifetime
/// of the host. The table is bounded with an LRU policy so a long-lived host that
/// keeps preparing new plans stays proportional to recently active plan-entrypoints
/// rather than every plan hash ever seen. Active entries must keep accumulating
/// history exactly as before; only the least-recently-used entries are dropped.
/// </summary>
public sealed class Fix_PAL_0030_Tests
{
    [Fact]
    public async Task Hotness_table_is_bounded_when_many_unique_plans_are_seen()
    {
        const int maxEntries = 8;
        var hotness = new AutoExecutionHotness(maxEntries);
        var template = await PrepareTemplatePlanAsync();

        for (var i = 0; i < maxEntries * 100; i++)
        {
            hotness.BeginAttempt(WithPlanHash(template, $"plan-{i}"), "main");
        }

        Assert.Equal(maxEntries, hotness.Count);
    }

    [Fact]
    public async Task Hotness_table_retains_recently_used_entries_over_cold_ones()
    {
        const int maxEntries = 4;
        var hotness = new AutoExecutionHotness(maxEntries);
        var template = await PrepareTemplatePlanAsync();
        var hotPlan = WithPlanHash(template, "hot-plan");

        // Establish two attempts of accumulated history for the hot plan.
        hotness.BeginAttempt(hotPlan, "main");

        // Flood the table with cold plans, keeping the hot plan touched between each
        // batch so it stays the most-recently-used entry and survives eviction.
        for (var i = 0; i < maxEntries * 50; i++)
        {
            hotness.BeginAttempt(WithPlanHash(template, $"cold-{i}"), "main");
            hotness.BeginAttempt(hotPlan, "main");
        }

        Assert.Equal(maxEntries, hotness.Count);

        // The hot entry must still carry its accumulated run history rather than a
        // freshly recreated state, proving it was never evicted.
        var attempt = hotness.BeginAttempt(hotPlan, "main");
        Assert.Equal("hot-plan", attempt.Stats.PlanHash);
        Assert.True(
            attempt.Stats.RunCount > maxEntries * 50,
            $"expected retained run history but RunCount was {attempt.Stats.RunCount}");
    }

    [Fact]
    public async Task Distinct_entrypoints_are_tracked_separately()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var plan = WithPlanHash(await PrepareTemplatePlanAsync(), "shared-plan");

        var first = hotness.BeginAttempt(plan, "alpha");
        var second = hotness.BeginAttempt(plan, "beta");

        Assert.Equal("alpha", first.Stats.Entrypoint);
        Assert.Equal("beta", second.Stats.Entrypoint);
        Assert.Equal(2, hotness.Count);
    }

    [Fact]
    public async Task Plan_and_entrypoint_delimiters_do_not_merge_distinct_hotness_entries()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var template = await PrepareTemplatePlanAsync();

        var first = hotness.BeginAttempt(WithPlanHash(template, "a|b"), "c");
        var second = hotness.BeginAttempt(WithPlanHash(template, "a"), "b|c");

        Assert.Equal("a|b", first.Stats.PlanHash);
        Assert.Equal("c", first.Stats.Entrypoint);
        Assert.Equal("a", second.Stats.PlanHash);
        Assert.Equal("b|c", second.Stats.Entrypoint);
        Assert.Equal(1, second.Stats.RunCount);
        Assert.Equal(2, hotness.Count);
    }

    [Fact]
    public async Task Run_count_attempts_share_exact_history_with_snapshot_attempts()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 16);
        var plan = WithPlanHash(await PrepareTemplatePlanAsync(), "shared-history");

        var first = hotness.BeginRunCountAttempt(plan, "main");
        var second = hotness.BeginRunCountAttempt(plan, "main");
        var snapshot = hotness.BeginAttempt(plan, "main");

        Assert.Equal(1, first.RunCount);
        Assert.Equal(2, second.RunCount);
        Assert.Equal(3, snapshot.Stats.RunCount);
        Assert.Equal("shared-history", snapshot.Stats.PlanHash);
        Assert.Equal("main", snapshot.Stats.Entrypoint);
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public async Task Value_equal_keys_share_history_across_distinct_string_instances()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var template = await PrepareTemplatePlanAsync();
        var firstHash = DistinctString("shared-plan");
        var secondHash = DistinctString("shared-plan");
        var firstEntrypoint = DistinctString("main");
        var secondEntrypoint = DistinctString("main");
        Assert.NotSame(firstHash, secondHash);
        Assert.NotSame(firstEntrypoint, secondEntrypoint);

        var first = hotness.BeginRunCountAttempt(WithPlanHash(template, firstHash), firstEntrypoint);
        var second = hotness.BeginRunCountAttempt(WithPlanHash(template, secondHash), secondEntrypoint);

        Assert.Equal(1, first.RunCount);
        Assert.Equal(2, second.RunCount);
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public async Task Repeated_most_recent_touch_preserves_exact_eviction_order()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var template = await PrepareTemplatePlanAsync();
        var planA = WithPlanHash(template, "plan-a");
        var planB = WithPlanHash(template, "plan-b");
        var planC = WithPlanHash(template, "plan-c");

        Assert.Equal(1, hotness.BeginRunCountAttempt(planA, "main").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(planB, "main").RunCount);
        Assert.Equal(2, hotness.BeginRunCountAttempt(planB, "main").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(planC, "main").RunCount);

        Assert.Equal(3, hotness.BeginRunCountAttempt(planB, "main").RunCount);
        Assert.Equal(1, hotness.BeginRunCountAttempt(planA, "main").RunCount);
        Assert.Equal(2, hotness.Count);
    }

    [Fact]
    public async Task Concurrent_most_recent_attempts_share_one_exact_history()
    {
        const int attempts = 256;
        var hotness = new AutoExecutionHotness(maxEntries: 2);
        var plan = WithPlanHash(await PrepareTemplatePlanAsync(), "shared-plan");
        var runCounts = new int[attempts];

        Parallel.For(
            0,
            attempts,
            index => runCounts[index] = hotness.BeginRunCountAttempt(plan, "main").RunCount);

        Assert.Equal(Enumerable.Range(1, attempts), runCounts.Order());
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public async Task Completion_of_evicted_state_does_not_resurrect_its_history()
    {
        var hotness = new AutoExecutionHotness(maxEntries: 1);
        var template = await PrepareTemplatePlanAsync();
        var planA = WithPlanHash(template, "plan-a");
        var planB = WithPlanHash(template, "plan-b");
        var evictedAttempt = hotness.BeginRunCountAttempt(planA, "main");

        hotness.BeginRunCountAttempt(planB, "main");
        evictedAttempt.Complete(SuccessfulResult("plan-a"), TimeSpan.FromTicks(1));
        var recreated = hotness.BeginAttempt(planA, "main");

        Assert.Equal(1, recreated.Stats.RunCount);
        Assert.Equal(0, recreated.Stats.CompletedRunCount);
        Assert.Null(recreated.Stats.LastRunAt);
        Assert.Equal(1, hotness.Count);
    }

    [Fact]
    public void Rejects_non_positive_capacity()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AutoExecutionHotness(maxEntries: 0));

    private static async Task<ExecutionPlan> PrepareTemplatePlanAsync()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static string DistinctString(string value) => new(value.ToCharArray());

    private static SandboxExecutionResult SuccessfulResult(string planHash)
        => new()
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new SandboxResourceUsage(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditEvents = [],
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = planHash,
            PolicyHash = "policy",
        };

    // A fresh ExecutionPlan that mirrors the prepared template but uses a distinct
    // plan hash, so each call produces a new logical plan-entrypoint key without
    // re-running the full prepare pipeline for thousands of variants.
    private static ExecutionPlan WithPlanHash(ExecutionPlan template, string planHash)
        => new(
            template.ModuleHash,
            planHash,
            template.PlanSeal,
            template.PolicyHash,
            template.BindingManifestHash,
            template.Module,
            template.Policy,
            template.Bindings,
            template.Budget,
            template.FunctionAnalysis,
            template.BindingReferences);
}
