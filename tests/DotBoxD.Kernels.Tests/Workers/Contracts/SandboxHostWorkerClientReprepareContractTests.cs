using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerClientReprepareContractTests
{
    [Fact]
    public async Task ExecuteInWorkerAsync_returns_host_failure_when_worker_reprepare_rejects_plan()
    {
        using var requestingHost = RequestingHost();
        var module = await requestingHost.ImportJsonAsync(LogModuleJson());
        var plan = await requestingHost.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().GrantLogging().Build());
        using var worker = new SandboxHostWorkerClient(WorkerHostWithoutLogBindings);

        var result = await worker.ExecuteInWorkerAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                Isolation = SandboxIsolation.InProcess
            });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.False(result.ExecutionDispatched);
    }

    private static SandboxHost RequestingHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

    private static SandboxHost WorkerHostWithoutLogBindings()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static string LogModuleJson()
        => """
        {
          "id": "worker-client-reprepare-contract",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "worker ok" }] } }
              ]
            }
          ]
        }
        """;
}
