using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class CompiledNoAuditRunStateRecoveryTests
{
    [Fact]
    public async Task Reusable_state_recovers_after_a_run_with_a_different_cancelled_token()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(10).Build());
        var executable = new CompiledExecutable(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                static (context, _) =>
                {
                    context.ChargeFuel(1);
                    return SandboxValue.FromInt32(35);
                }),
            "Miss");
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var allowedBindings = AssertNoAuditBindings(plan);
        var state = new CompiledNoAuditRunState(plan);

        var first = await ExecuteAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var failed = await ExecuteAsync(cancelled.Token);
        var recovered = await ExecuteAsync(CancellationToken.None);

        AssertSuccess(first);
        Assert.False(failed.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, failed.Error!.Code);
        Assert.Contains(failed.AuditEvents, e =>
            e.Kind == "RunSummary" && e.ErrorCode == SandboxErrorCode.Cancelled);
        AssertSuccess(recovered);
        Assert.Equal(first.ResourceUsage, recovered.ResourceUsage);

        ValueTask<SandboxExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
            => CompiledNoAuditResultRunner.Execute(
                executable,
                plan,
                "main",
                input,
                Options(),
                allowedBindings,
                cancellationToken,
                state);
    }

    private static void AssertSuccess(SandboxExecutionResult result)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(1, result.ResourceUsage.FuelUsed);
        Assert.Empty(result.AuditEvents);
    }

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Auto,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static IReadOnlySet<string> AssertNoAuditBindings(ExecutionPlan plan)
    {
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        Assert.NotNull(allowedBindings);
        Assert.Empty(allowedBindings);
        return allowedBindings;
    }
}
