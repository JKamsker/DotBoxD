using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerClientDirectIsolationContractTests
{
    [Fact]
    public async Task ExecuteInWorkerAsync_rejects_worker_process_options_before_worker_host_creation()
    {
        var plan = await PreparePlanAsync();
        var nestedWorker = new CapturingWorker();
        var factoryCalls = 0;
        using var worker = new SandboxHostWorkerClient(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return WorkerHostFactory(nestedWorker);
        });

        var exception = await Record.ExceptionAsync(
            async () => await worker.ExecuteInWorkerAsync(
                plan,
                "main",
                Input(),
                WorkerProcessOptions()));

        Assert.True(
            exception is ArgumentException { ParamName: "options" },
            "Expected ArgumentException for options before worker-host creation. " +
            $"Factory calls: {Volatile.Read(ref factoryCalls)}. " +
            $"Nested worker calls: {nestedWorker.Calls}. " +
            $"Actual exception: {exception?.GetType().FullName ?? "<none>"}.");
        Assert.Equal(0, Volatile.Read(ref factoryCalls));
        Assert.Equal(0, nestedWorker.Calls);
    }

    private static async ValueTask<ExecutionPlan> PreparePlanAsync()
    {
        using var host = WorkerHostFactory(new CapturingWorker());
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxHost WorkerHostFactory(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private static SandboxExecutionOptions WorkerProcessOptions()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = SandboxIsolation.WorkerProcess
        };
}
