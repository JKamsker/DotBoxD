using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class InterpreterPostCancellationBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_custom_interpreter_result_is_cancelled_when_caller_token_was_cancelled(
        bool suppressSuccessfulSummary)
    {
        using var cancellation = new CancellationTokenSource();
        var interpreter = new CancellingSuccessfulInterpreter(cancellation);
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(interpreter, observed.Add);
        var plan = await PrepareAsync(host);
        var runId = SandboxRunId.New();

        var result = await host.ExecuteAsync(
            plan,
            "main",
            Input(),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                RunId = runId,
                SuppressSuccessfulRunSummaryAudit = suppressSuccessfulSummary
            },
            cancellation.Token);

        var successfulResult = Assert.IsType<SandboxExecutionResult>(interpreter.SuccessfulResult);
        Assert.True(successfulResult.Succeeded);
        Assert.True(cancellation.IsCancellationRequested);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal("execution cancelled", result.Error.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(successfulResult.ResourceUsage, result.ResourceUsage);
        Assert.Equal(successfulResult.ModuleHash, result.ModuleHash);
        Assert.Equal(successfulResult.PlanHash, result.PlanHash);
        Assert.Equal(successfulResult.PolicyHash, result.PolicyHash);

        var summary = Assert.Single(result.AuditEvents, auditEvent => auditEvent.Kind == "RunSummary");
        Assert.Equal(runId, summary.RunId);
        Assert.False(summary.Success);
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
        Assert.Equal("True", summary.Fields!["executionDispatched"]);
        Assert.Equal(
            result.ResourceUsage.FuelUsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            summary.Fields["fuelUsed"]);
        Assert.Equal(result.AuditEvents, observed);
        Assert.DoesNotContain(observed, auditEvent => auditEvent.Kind == "RunSummary" && auditEvent.Success);
    }

    private static SandboxHost CreateHost(
        ISandboxInterpreter interpreter,
        Action<SandboxAuditEvent> observer)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter(interpreter);
            builder.ForwardAuditEventsTo(observer);
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(
            SandboxTestHost.PureScoreJson("interpreter-post-cancellation-boundary"));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private sealed class CancellingSuccessfulInterpreter(CancellationTokenSource cancellation)
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
            Assert.False(cancellationToken.IsCancellationRequested);

            SuccessfulResult = await _inner.ExecuteAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.True(SuccessfulResult.Succeeded, SuccessfulResult.Error?.SafeMessage);

            cancellation.Cancel();
            return SuccessfulResult;
        }
    }
}
