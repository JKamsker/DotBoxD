using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledExecutionCancellationClassificationTests
{
    [Fact]
    public async Task Compiled_delegate_operation_canceled_exception_is_host_failure_when_caller_token_is_not_canceled()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePurePlanAsync(host);
        var executable = ThrowingExecutable(plan);

        var result = await CompiledExecutionRunner.ExecuteAsync(
            executable,
            plan,
            "main",
            Input(),
            CompiledOptions(),
            CancellationToken.None);

        AssertHostFailure(result);
    }

    [Fact]
    public async Task Compiled_no_audit_value_operation_canceled_exception_is_host_failure_when_caller_token_is_not_canceled()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePurePlanAsync(host);
        var executable = ThrowingExecutable(plan);
        var allowedBindings = AssertNoAuditBindings(plan);

        var result = CompiledNoAuditValueRunner.Execute(
            executable,
            plan,
            "main",
            Input(),
            CompiledOptions(suppressSuccessfulSummary: true),
            allowedBindings,
            CancellationToken.None,
            reusableState: null);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.NotNull(result.FullResult);
        AssertHostFailure(result.FullResult);
    }

    [Fact]
    public async Task Compiled_delegate_operation_canceled_exception_stays_cancelled_when_caller_token_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        using var host = SandboxTestHost.Create();
        var plan = await PreparePurePlanAsync(host);
        var executable = ThrowingExecutable(plan, cancellation.Cancel);

        var result = await CompiledExecutionRunner.ExecuteAsync(
            executable,
            plan,
            "main",
            Input(),
            CompiledOptions(),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        AssertRunSummary(result, SandboxErrorCode.Cancelled);
    }

    private static async Task<ExecutionPlan> PreparePurePlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxExecutionOptions CompiledOptions(bool suppressSuccessfulSummary = false)
        => new()
        {
            Mode = ExecutionMode.Compiled,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = suppressSuccessfulSummary
        };

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private static CompiledExecutable ThrowingExecutable(ExecutionPlan plan, Action? beforeThrow = null)
    {
        var artifact = CompiledArtifactTestFactory.DynamicMethod(
            plan,
            (_, _) =>
            {
                beforeThrow?.Invoke();
                throw new OperationCanceledException();
            },
            "dynamic-oce-artifact");
        return new CompiledExecutable(artifact, "Miss");
    }

    private static IReadOnlySet<string> AssertNoAuditBindings(ExecutionPlan plan)
    {
        var found = plan.BindingReferences.TryGetValue("main", out var allowedBindings);
        Assert.True(found);
        Assert.NotNull(allowedBindings);
        Assert.Empty(allowedBindings);
        return allowedBindings;
    }

    private static void AssertHostFailure(SandboxExecutionResult? result)
    {
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        AssertRunSummary(result, SandboxErrorCode.HostFailure);
    }

    private static void AssertRunSummary(SandboxExecutionResult result, SandboxErrorCode expectedCode)
    {
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(expectedCode, summary.ErrorCode);
    }
}
