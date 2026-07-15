using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerClientLifecycleContractTests
{
    [Fact]
    public async Task Dispose_during_worker_host_factory_disposes_created_host_and_blocks_execution()
    {
        var plan = await PreparePlanAsync();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SandboxHost? createdHost = null;

        using var worker = new SandboxHostWorkerClient(() =>
        {
            factoryEntered.SetResult();
            releaseFactory.Task.GetAwaiter().GetResult();
            createdHost = WorkerHostFactory();
            return createdHost;
        });

        try
        {
            var execution = Task.Run(
                async () => await worker.ExecuteInWorkerAsync(
                    plan,
                    "main",
                    Input(),
                    Options()));

            await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposeTask = Task.Run(worker.Dispose);
            releaseFactory.SetResult();
            await disposeTask;

            var executionException = await Record.ExceptionAsync(
                async () => await execution.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.IsType<ObjectDisposedException>(executionException);
            var workerHost = Assert.IsType<SandboxHost>(createdHost);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await workerHost.ImportJsonAsync(SandboxTestHost.PureScoreJson()));
        }
        finally
        {
            releaseFactory.TrySetResult();
            createdHost?.Dispose();
        }
    }

    private static async ValueTask<ExecutionPlan> PreparePlanAsync()
    {
        using var host = WorkerHostFactory();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxHost WorkerHostFactory()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static SandboxValue Input()
        => SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = SandboxIsolation.InProcess
        };
}
