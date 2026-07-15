using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Hosting;

public sealed class InterpreterFailureBoundaryTests
{
    private const string ExpectedSafeMessage = "interpreter execution failed";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_interpreter_result_is_host_failure_regardless_of_audit_observer(
        bool attachObserver)
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(
            new NullResultInterpreter(),
            attachObserver ? observed.Add : null);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        AssertHostFailure(result);
        Assert.Equal(attachObserver ? result.AuditEvents : [], observed);
    }

    [Fact]
    public async Task Throwing_interpreter_is_host_failure_without_leaking_exception_details()
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = CreateHost(new ThrowingInterpreter(), observed.Add);
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan);

        AssertHostFailure(result);
        Assert.Equal(result.AuditEvents, observed);
        Assert.DoesNotContain(
            ThrowingInterpreter.SensitiveMessage,
            result.Error!.SafeMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interpreter_cancellation_stays_cancelled_when_caller_token_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        using var host = CreateHost(new CallerCancellingInterpreter(cancellation));
        var plan = await PrepareAsync(host);

        var result = await ExecuteAsync(host, plan, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal("execution cancelled", result.Error.SafeMessage);
        AssertRunSummary(result, SandboxErrorCode.Cancelled);
    }

    private static SandboxHost CreateHost(
        ISandboxInterpreter interpreter,
        Action<SandboxAuditEvent>? observer = null)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter(interpreter);
            if (observer is not null)
            {
                builder.ForwardAuditEventsTo(observer);
            }
        });

    private static async ValueTask<ExecutionPlan> PrepareAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync(
            SandboxTestHost.PureScoreJson("interpreter-failure-boundary"));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        CancellationToken cancellationToken = default)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cancellationToken);

    private static void AssertHostFailure(SandboxExecutionResult result)
    {
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.True(result.ExecutionDispatched);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(ExpectedSafeMessage, result.Error.SafeMessage);

        AssertRunSummary(result, SandboxErrorCode.HostFailure);
    }

    private static void AssertRunSummary(
        SandboxExecutionResult result,
        SandboxErrorCode expectedErrorCode)
    {
        var summary = Assert.Single(
            result.AuditEvents,
            auditEvent => auditEvent.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(expectedErrorCode, summary.ErrorCode);
    }

    private sealed class NullResultInterpreter : ISandboxInterpreter
    {
        public ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<SandboxExecutionResult>(null!);
    }

    private sealed class ThrowingInterpreter : ISandboxInterpreter
    {
        public const string SensitiveMessage = "custom interpreter secret";

        public ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromException<SandboxExecutionResult>(new InvalidOperationException(SensitiveMessage));
    }

    private sealed class CallerCancellingInterpreter(CancellationTokenSource cancellation)
        : ISandboxInterpreter
    {
        public ValueTask<SandboxExecutionResult> ExecuteAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromException<SandboxExecutionResult>(
                new OperationCanceledException(cancellation.Token));
        }
    }
}
