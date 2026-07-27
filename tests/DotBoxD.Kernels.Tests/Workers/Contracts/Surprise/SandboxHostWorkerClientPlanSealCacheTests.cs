using DotBoxD.Hosting;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class SandboxHostWorkerClientPlanSealCacheTests
{
    [Fact]
    public async Task ExecuteInWorkerAsync_does_not_reuse_prepared_plan_for_distinct_module_with_equal_seal()
    {
        using var requestingHost = WorkerHostFactory();
        var firstPlan = await PreparePlanAsync(requestingHost, "worker-cache-first", 11);
        var secondPreparedPlan = await PreparePlanAsync(requestingHost, "worker-cache-second", 29);
        var secondPlanWithSharedSeal = WithSeal(secondPreparedPlan, firstPlan.PlanSeal);

        using var worker = new SandboxHostWorkerClient(WorkerHostFactory);

        var first = await worker.ExecuteInWorkerAsync(
            firstPlan,
            "main",
            SandboxValue.Unit,
            Options());
        var second = await worker.ExecuteInWorkerAsync(
            secondPlanWithSharedSeal,
            "main",
            SandboxValue.Unit,
            Options());

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.Equal(11, Assert.IsType<I32Value>(first.Value).Value);

        if (!second.Succeeded)
        {
            Assert.Equal(SandboxErrorCode.HostFailure, second.Error?.Code);
            return;
        }

        Assert.Equal(29, Assert.IsType<I32Value>(second.Value).Value);
    }

    private static async ValueTask<ExecutionPlan> PreparePlanAsync(
        SandboxHost host,
        string moduleId,
        int result)
    {
        var module = await host.ImportJsonAsync(ConstantModuleJson(moduleId, result));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static ExecutionPlan WithSeal(ExecutionPlan plan, ExecutionPlanSeal seal)
        => new(
            plan.ModuleHash,
            plan.PlanHash,
            seal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            plan.Module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);

    private static SandboxHost WorkerHostFactory()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            Isolation = SandboxIsolation.InProcess
        };

    private static string ConstantModuleJson(string moduleId, int result)
        => $$"""
        {
          "id": "{{moduleId}}",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "capabilityRequests": [],
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "i32": {{result}} } }]
          }]
        }
        """;
}
