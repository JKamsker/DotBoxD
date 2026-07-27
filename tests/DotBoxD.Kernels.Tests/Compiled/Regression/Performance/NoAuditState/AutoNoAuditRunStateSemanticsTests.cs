using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class AutoNoAuditRunStateSemanticsTests
{
    [Fact]
    public async Task Reusable_state_does_not_stick_auto_mode_or_bypass_provider_and_hotness()
    {
        var selector = new SequencedSelector(
            ExecutionModeDecision.Compiled,
            ExecutionModeDecision.Interpreted,
            ExecutionModeDecision.Compiled);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
            builder.UseExecutionModeSelector(selector);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var state = new CompiledNoAuditRunState(plan);
        state.StoreExecutable(
            "main",
            new CompiledExecutable(
                CompiledArtifactTestFactory.DynamicMethod(
                    plan,
                    static (_, _) => SandboxValue.FromInt32(999),
                    "must-not-run"),
                "PoisonedState"));

        var results = new PreparedExecutionResult[4];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = await host.ExecutePreparedValueInProcessAsync(
                plan,
                "main",
                input,
                Options(),
                CancellationToken.None,
                state);
        }

        Assert.Equal(
            [
                ExecutionMode.Interpreted,
                ExecutionMode.Compiled,
                ExecutionMode.Interpreted,
                ExecutionMode.Compiled
            ],
            results.Select(result => result.ActualMode));
        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
            Assert.NotNull(result.FullResult);
        });
        Assert.All(
            results.Where(result => result.ActualMode == ExecutionMode.Compiled),
            result =>
            {
                Assert.NotEqual("must-not-run", result.ArtifactHash);
                Assert.Empty(result.FullResult!.AuditEvents);
            });

        Assert.Equal(3, selector.Hotness.Count);
        Assert.Equal(2, selector.Hotness[0].RunCount);
        Assert.Equal(1, selector.Hotness[0].CompletedRunCount);
        Assert.Null(selector.Hotness[0].LastCompiledArtifactHash);
        Assert.Equal(3, selector.Hotness[1].RunCount);
        Assert.Equal(2, selector.Hotness[1].CompletedRunCount);
        Assert.Equal(results[1].ArtifactHash, selector.Hotness[1].LastCompiledArtifactHash);
        Assert.Equal(4, selector.Hotness[2].RunCount);
        Assert.Equal(3, selector.Hotness[2].CompletedRunCount);
        Assert.Equal(results[1].ArtifactHash, selector.Hotness[2].LastCompiledArtifactHash);
        var expectedAverageFuel = results.Take(3).Sum(result => result.FullResult!.ResourceUsage.FuelUsed) / 3;
        Assert.Equal(expectedAverageFuel, selector.Hotness[2].AverageFuelUsed);
        Assert.Equal(results[1].FullResult!.ResourceUsage, results[3].FullResult!.ResourceUsage);
    }

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = 1,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private sealed class SequencedSelector(params ExecutionModeDecision[] decisions) : IExecutionModeSelector
    {
        private int _next;

        public List<ModuleHotnessStats> Hotness { get; } = [];

        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
        {
            Assert.Equal(CompiledCacheStatus.None, cacheStatus);
            Hotness.Add(hotness);
            return decisions[_next++];
        }
    }
}
