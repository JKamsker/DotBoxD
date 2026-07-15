using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.AttemptResult;

public sealed class CompiledAttemptAwaitBoundaryTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Delayed_compiler_success_preserves_result_after_first_incomplete_await()
    {
        var compiler = new GatedSuccessCompiler();
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(compiler);
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var execution = host.ExecuteAsync(
                plan,
                "main",
                CompiledAttemptPathTestSupport.PureInput(),
                CompiledAttemptPathTestSupport.CompiledOptions(runId))
            .AsTask();

        await compiler.Gate.WaitUntilEnteredAsync();
        var completedBeforeRelease = execution.IsCompleted;
        compiler.Gate.Release();
        var result = await execution.WaitAsync(CompletionTimeout);

        Assert.False(completedBeforeRelease);
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.True(summary.Success);
        Assert.Equal(runId, summary.RunId);
    }

    [Fact]
    public async Task Delayed_verifier_failure_preserves_fallback_reason_after_first_incomplete_await()
    {
        var compiler = new GatedSandboxErrorCompiler(SandboxErrorCode.VerifierFailure);
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(compiler);
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var execution = host.ExecuteAsync(
                plan,
                "main",
                CompiledAttemptPathTestSupport.PureInput(),
                CompiledAttemptPathTestSupport.CompiledOptions(runId, allowFallback: true))
            .AsTask();

        await compiler.Gate.WaitUntilEnteredAsync();
        var completedBeforeRelease = execution.IsCompleted;
        compiler.Gate.Release();
        var result = await execution.WaitAsync(CompletionTimeout);

        Assert.False(completedBeforeRelease);
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(
            ["VerifierFailure", "ExecutionFallback", "RunSummary"],
            result.AuditEvents.Select(auditEvent => auditEvent.Kind));
        var verifier = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "VerifierFailure");
        Assert.Equal(SandboxErrorCode.VerifierFailure, verifier.ErrorCode);
        var fallback = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        Assert.Equal(SandboxErrorCode.VerifierFailure, fallback.ErrorCode);
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.True(summary.Success);
        Assert.All(result.AuditEvents, auditEvent => Assert.Equal(runId, auditEvent.RunId));
    }

    [Fact]
    public async Task Delayed_compiler_cancellation_preserves_terminal_result_after_first_incomplete_await()
    {
        using var cancellation = new CancellationTokenSource();
        var compiler = new GatedSuccessCompiler();
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(compiler);
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var execution = host.ExecuteAsync(
                plan,
                "main",
                CompiledAttemptPathTestSupport.PureInput(),
                CompiledAttemptPathTestSupport.CompiledOptions(runId, allowFallback: true),
                cancellation.Token)
            .AsTask();

        await compiler.Gate.WaitUntilEnteredAsync();
        var completedBeforeCancellation = execution.IsCompleted;
        cancellation.Cancel();
        SandboxExecutionResult result;
        try
        {
            result = await execution.WaitAsync(CompletionTimeout);
        }
        finally
        {
            compiler.Gate.Release();
        }

        Assert.False(completedBeforeCancellation);
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.Equal(runId, summary.RunId);
    }
}
