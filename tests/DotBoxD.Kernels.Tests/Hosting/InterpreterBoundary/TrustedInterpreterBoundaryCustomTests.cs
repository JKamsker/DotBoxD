using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class TrustedInterpreterBoundaryCustomTests
{
    [Fact]
    public async Task Forwarding_custom_interpreter_preserves_valid_suppressed_success()
    {
        var interpreter = new TransformingInterpreter((_, result) => result);
        var runId = SandboxRunId.New();

        var outcome = await ExecuteAsync(
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule,
            Policy(),
            interpreter,
            options: TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId));

        Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(outcome.Result.Value).Value);
        Assert.Empty(outcome.Result.AuditEvents);
        Assert.Empty(outcome.Observed);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(outcome.Result, outcome.Plan);
    }

    [Fact]
    public async Task Forwarding_custom_interpreter_rejects_wrong_typed_fast_path_shaped_success()
    {
        var interpreter = new WrongTypedForwardingInterpreter();
        var runId = SandboxRunId.New();

        var outcome = await ExecuteAsync(
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule,
            Policy(),
            interpreter,
            options: TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId));

        AssertFastPathShapedMalformedResult(interpreter.ReturnedResult!, outcome.Plan);
        AssertRejectedWithoutPublication(outcome);
        var summary = Assert.Single(outcome.Result.AuditEvents);
        Assert.Equal("RunSummary", summary.Kind);
        Assert.Equal(runId, summary.RunId);
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.HostFailure, summary.ErrorCode);
    }

    [Fact]
    public async Task Post_dispatch_cancellation_still_replaces_a_valid_suppressed_success()
    {
        using var cancellation = new CancellationTokenSource();
        var interpreter = new CancellingForwardingInterpreter(cancellation);
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.UseInterpreter(interpreter);
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var plan = await TrustedInterpreterBoundaryTestSupport.PrepareAsync(
            host,
            TrustedInterpreterBoundaryTestSupport.PureSuccessModule);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            TrustedInterpreterBoundaryTestSupport.SuppressedOptions(runId: runId),
            cancellation.Token);

        var successful = Assert.IsType<SandboxExecutionResult>(interpreter.SuccessfulResult);
        Assert.True(successful.Succeeded, successful.Error?.SafeMessage);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(successful.ResourceUsage, result.ResourceUsage);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(result, plan);
        var summary = Assert.Single(result.AuditEvents);
        Assert.Equal(runId, summary.RunId);
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.Equal(result.AuditEvents, observed);
    }

    private static void AssertFastPathShapedMalformedResult(
        SandboxExecutionResult result,
        ExecutionPlan plan)
    {
        Assert.True(result.Succeeded);
        Assert.Equal("wrong", Assert.IsType<StringValue>(result.Value).Value);
        Assert.Null(result.Error);
        Assert.Same(InMemoryAuditSink.EmptyEventSnapshot, result.AuditEvents);
        TrustedInterpreterBoundaryTestSupport.AssertEnvelope(result, plan);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
        Assert.Equal(0, result.ResourceUsage.FileBytesRead);
        Assert.Equal(0, result.ResourceUsage.FileBytesWritten);
        Assert.Equal(0, result.ResourceUsage.NetworkBytesRead);
        Assert.Equal(0, result.ResourceUsage.NetworkBytesWritten);
        Assert.Equal(0, result.ResourceUsage.LogEvents);
    }

    private sealed class WrongTypedForwardingInterpreter : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();

        public SandboxExecutionResult? ReturnedResult { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            var result = await _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            ReturnedResult = result with { Value = SandboxValue.FromString("wrong") };
            return ReturnedResult;
        }
    }

    private sealed class CancellingForwardingInterpreter(CancellationTokenSource cancellation)
        : ISandboxInterpreter
    {
        private readonly SandboxInterpreter _inner = new();

        public SandboxExecutionResult? SuccessfulResult { get; private set; }

        public async ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            Assert.Equal(cancellation.Token, cancellationToken);
            SuccessfulResult = await _inner.ExecuteAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    CancellationToken.None)
                .ConfigureAwait(false);
            cancellation.Cancel();
            return SuccessfulResult;
        }
    }
}
