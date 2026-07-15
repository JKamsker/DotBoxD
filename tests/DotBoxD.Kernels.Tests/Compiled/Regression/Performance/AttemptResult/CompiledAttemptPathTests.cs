using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.AttemptResult;

public sealed class CompiledAttemptPathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_compiled_success_preserves_audit_suppression(bool suppressSuccessfulSummary)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            CompiledAttemptPathTestSupport.PureInput(),
            CompiledAttemptPathTestSupport.CompiledOptions(
                runId,
                suppressSuccessfulSummary: suppressSuccessfulSummary));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
        Assert.True(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        if (suppressSuccessfulSummary)
        {
            Assert.Empty(result.AuditEvents);
            return;
        }

        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.True(summary.Success);
        Assert.Equal(runId, summary.RunId);
    }

    [Fact]
    public async Task Public_compiled_runtime_failure_remains_a_compiled_result()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await CompiledAttemptPathTestSupport.PrepareArithmeticFailurePlanAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            CompiledAttemptPathTestSupport.CompiledOptions(
                runId,
                allowFallback: true,
                suppressSuccessfulSummary: true));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        AssertFailedSummary(result, runId, SandboxErrorCode.InvalidInput);
    }

    [Theory]
    [InlineData(SandboxErrorCode.VerifierFailure, true)]
    [InlineData(SandboxErrorCode.ValidationError, false)]
    public async Task Compiler_rejection_fallback_preserves_the_reason(
        SandboxErrorCode errorCode,
        bool expectsVerifierAudit)
    {
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(new SandboxErrorCompiler(errorCode));
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            CompiledAttemptPathTestSupport.PureInput(),
            CompiledAttemptPathTestSupport.CompiledOptions(runId, allowFallback: true));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(35, Assert.IsType<I32Value>(result.Value).Value);
        var fallback = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        Assert.Equal(errorCode, fallback.ErrorCode);
        Assert.Equal(runId, fallback.RunId);
        Assert.Equal(
            expectsVerifierAudit,
            result.AuditEvents.Any(auditEvent => auditEvent.Kind == "VerifierFailure"));
        Assert.All(result.AuditEvents, auditEvent => Assert.Equal(runId, auditEvent.RunId));
    }

    [Fact]
    public async Task Compiler_sandbox_failure_without_fallback_preserves_the_compiled_failure_result()
    {
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(
            new SandboxErrorCompiler(SandboxErrorCode.VerifierFailure));
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            CompiledAttemptPathTestSupport.PureInput(),
            CompiledAttemptPathTestSupport.CompiledOptions(runId));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.VerifierFailure, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        Assert.Contains(result.AuditEvents, auditEvent => auditEvent.Kind == "VerifierFailure");
        AssertFailedSummary(result, runId, SandboxErrorCode.VerifierFailure);
    }

    [Theory]
    [InlineData("cancelled", SandboxErrorCode.Cancelled)]
    [InlineData("host", SandboxErrorCode.HostFailure)]
    public async Task Unexpected_compiler_exits_preserve_failure_union_arm(
        string failureKind,
        SandboxErrorCode expectedErrorCode)
    {
        var compiler = failureKind == "cancelled"
            ? new CancelledCompiler()
            : (DotBoxD.Kernels.Compiler.ISandboxCompiler)new HostFailureCompiler();
        using var host = CompiledAttemptPathTestSupport.HostWithCompiler(compiler);
        var plan = await CompiledAttemptPathTestSupport.PreparePurePlanAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            CompiledAttemptPathTestSupport.PureInput(),
            CompiledAttemptPathTestSupport.CompiledOptions(runId, allowFallback: true));

        Assert.False(result.Succeeded);
        Assert.Equal(expectedErrorCode, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.DoesNotContain(result.AuditEvents, auditEvent => auditEvent.Kind == "ExecutionFallback");
        AssertFailedSummary(result, runId, expectedErrorCode);
    }

    private static void AssertFailedSummary(
        SandboxExecutionResult result,
        SandboxRunId runId,
        SandboxErrorCode errorCode)
    {
        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(errorCode, summary.ErrorCode);
        Assert.Equal(runId, summary.RunId);
    }
}
