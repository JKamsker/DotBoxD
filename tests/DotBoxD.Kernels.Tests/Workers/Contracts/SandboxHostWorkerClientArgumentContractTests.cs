using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerClientArgumentContractTests
{
    [Theory]
    [InlineData("entrypoint")]
    [InlineData("input")]
    public async Task ExecuteInWorkerAsync_rejects_required_arguments_before_worker_host_factory(
        string parameterName)
    {
        var plan = await PreparePlanAsync();
        var factoryCalls = 0;
        using var worker = new SandboxHostWorkerClient(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return WorkerHostFactory();
        });

        var exception = parameterName == "entrypoint"
            ? await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await worker.ExecuteInWorkerAsync(
                    plan,
                    null!,
                    Input(),
                    Options()))
            : await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await worker.ExecuteInWorkerAsync(
                    plan,
                    "main",
                    null!,
                    Options()));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, Volatile.Read(ref factoryCalls));
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
