using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerIndependentCancellationClassificationTests
{
    [Fact]
    public async Task Independently_cancelled_worker_is_reported_as_host_failure_not_timeout()
    {
        using var independentCancellation = new CancellationTokenSource();
        independentCancellation.Cancel();
        var worker = new IndependentlyCancelledWorker(independentCancellation.Token);
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
            new SandboxExecutionOptions { Isolation = SandboxIsolation.WorkerProcess });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("worker process execution failed", result.Error.SafeMessage);
        Assert.Contains(result.AuditEvents, audit => audit.Kind == "WorkerIsolationFailed");
        Assert.Equal(1, worker.Calls);
    }

    private sealed class IndependentlyCancelledWorker(CancellationToken independentCancellation)
        : ISandboxWorkerClient
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
            return ValueTask.FromException<SandboxExecutionResult>(
                new OperationCanceledException(independentCancellation));
        }
    }
}
