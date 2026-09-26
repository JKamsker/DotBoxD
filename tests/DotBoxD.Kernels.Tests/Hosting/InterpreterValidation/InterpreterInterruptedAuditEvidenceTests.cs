using DotBoxD.Hosting;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Hosting;

using static InterpreterSecurityValidationTestSupport;

public sealed class InterpreterInterruptedAuditEvidenceTests
{
    private const string BindingId = "test.audited.interruption";

    [Theory]
    [InlineData(SandboxErrorCode.Timeout, true, false)]
    [InlineData(SandboxErrorCode.Timeout, false, false)]
    [InlineData(SandboxErrorCode.Cancelled, true, false)]
    [InlineData(SandboxErrorCode.Cancelled, false, false)]
    [InlineData(SandboxErrorCode.Timeout, true, true)]
    [InlineData(SandboxErrorCode.Timeout, false, true)]
    [InlineData(SandboxErrorCode.Cancelled, true, true)]
    [InlineData(SandboxErrorCode.Cancelled, false, true)]
    public async Task Interruption_preserves_error_and_single_call_accounting(
        SandboxErrorCode errorCode,
        bool writeSuccessFirst,
        bool throughWorker)
    {
        var audited = AuditedBinding(BindingId, maxCallsPerRun: 1);
        var binding = audited with
        {
            Invoke = (context, arguments, cancellationToken) =>
            {
                if (writeSuccessFirst)
                {
                    _ = audited.Invoke(context, arguments, cancellationToken).GetAwaiter().GetResult();
                }

                throw new SandboxRuntimeException(new SandboxError(errorCode, "binding interrupted"));
            }
        };

        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            PolicyWithWallTime(TimeSpan.FromSeconds(10)),
            configure: builder =>
            {
                builder.AddBinding(binding);
                builder.UseWorkerClient(new InterpreterWorker(), SandboxWorkerProfile.HardenedOutOfProcess);
            },
            options: Options(throughWorker));

        AssertInterruption(outcome, errorCode, writeSuccessFirst);
    }

    [Fact]
    public async Task Deadline_expiring_after_success_audit_preserves_timeout()
    {
        var audited = AuditedBinding(BindingId, maxCallsPerRun: 1);
        var binding = audited with
        {
            Invoke = (context, arguments, cancellationToken) =>
            {
                var result = audited.Invoke(context, arguments, cancellationToken).GetAwaiter().GetResult();
                Assert.True(SpinWait.SpinUntil(() => DeadlineExpired(context), TimeSpan.FromSeconds(15)));
                return ValueTask.FromResult(result);
            }
        };
        var outcome = await ExecuteAsync(
            SingleBindingModule(BindingId),
            PolicyWithWallTime(TimeSpan.FromSeconds(5)),
            configure: builder => builder.AddBinding(binding));

        AssertInterruption(outcome, SandboxErrorCode.Timeout, writeSuccessFirst: true);
    }

    private static SandboxPolicy PolicyWithWallTime(TimeSpan wallTime)
    {
        var policy = Policy();
        return policy with { ResourceLimits = policy.ResourceLimits with { MaxWallTime = wallTime } };
    }

    private static bool DeadlineExpired(SandboxContext context)
    {
        try
        {
            context.Budget.CheckDeadline();
            return false;
        }
        catch (SandboxRuntimeException ex) when (ex.Error.Code == SandboxErrorCode.Timeout)
        {
            return true;
        }
    }

    private static void AssertInterruption(
        InterpreterValidationOutcome outcome,
        SandboxErrorCode errorCode,
        bool writeSuccessFirst)
    {
        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(errorCode, outcome.Result.Error!.Code);
        Assert.Equal(1, outcome.Result.ResourceUsage.HostCalls);
        Assert.Equal(outcome.Result.AuditEvents, outcome.Observed);
        var bindingEvents = outcome.Result.AuditEvents.Where(e => e.BindingId == BindingId).ToArray();
        Assert.Equal(writeSuccessFirst ? 2 : 1, bindingEvents.Length);
        Assert.Equal(errorCode, bindingEvents[^1].ErrorCode);
        Assert.False(bindingEvents[^1].Success);
        if (writeSuccessFirst)
        {
            Assert.True(bindingEvents[0].Success);
        }
    }

    private static SandboxExecutionOptions Options(bool throughWorker)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = throughWorker ? SandboxIsolation.WorkerProcess : SandboxIsolation.InProcess
        };

    private sealed class InterpreterWorker : ISandboxWorkerClient
    {
        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
            => new SandboxInterpreter().ExecuteAsync(plan, entrypoint, input, options, cancellationToken);
    }
}
