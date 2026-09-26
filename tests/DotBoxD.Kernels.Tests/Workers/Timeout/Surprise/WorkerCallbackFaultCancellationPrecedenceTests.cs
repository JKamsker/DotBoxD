using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerCallbackFaultCancellationPrecedenceTests
{
    [Fact]
    public async Task Worker_callback_fault_after_cancelling_caller_is_reported_as_cancelled()
    {
        using var caller = new CancellationTokenSource();
        var worker = new CallerCancellingFaultingWorker(caller);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromSeconds(30))
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]),
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess },
            caller.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.Equal(1, worker.Calls);
        Assert.DoesNotContain("callback fault", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Contains(
            result.AuditEvents,
            audit => audit.Kind == "RunSummary" &&
                     !audit.Success &&
                     audit.ErrorCode == SandboxErrorCode.Cancelled);
    }

    private sealed class CallerCancellingFaultingWorker(CancellationTokenSource caller) : ISandboxWorkerClient
    {
        public int Calls { get; private set; }

        public ValueTask<SandboxExecutionResult> ExecuteInWorkerAsync(
            ExecutionPlan plan,
            string entrypoint,
            SandboxValue input,
            SandboxExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            caller.Cancel();
            return ValueTask.FromException<SandboxExecutionResult>(
                new InvalidOperationException("callback fault"));
        }
    }
}
