using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Policy.Matching;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CapabilityRevocationAllocationTests(ITestOutputHelper output)
{
    private const int WarmupIterations = 1_000;
    private const int MeasuredIterations = 5_000;
    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Interpreted,
        SuppressSuccessfulRunSummaryAudit = true
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unrelated_revocations_do_not_add_allocation_per_entry(bool wildcard)
    {
        using var host = SandboxHost.Create(static builder => builder.UseInterpreter());
        var module = await host.ImportJsonAsync(ModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().GrantLogging().WithFuel(1_000).Build());

        host.RevokeCapability(UnrelatedCapability(0, wildcard));
        var single = MeasureSteadyState(host, plan);
        for (var i = 1; i < 512; i++)
        {
            host.RevokeCapability(UnrelatedCapability(i, wildcard));
        }

        var many = MeasureSteadyState(host, plan);
        output.WriteLine($"wildcard={wildcard}: one={single:N3} B/run, 512={many:N3} B/run.");
        Assert.True(many <= single + 64, $"Revocation count increased allocation from {single:N3} to {many:N3} B/run.");

        host.RevokeCapability(wildcard ? "log.*" : "log.write");
        var denied = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options);
        Assert.False(denied.Succeeded);
        Assert.False(denied.ExecutionDispatched);
        Assert.Equal(SandboxErrorCode.PolicyDenied, denied.Error!.Code);
    }

    private static double MeasureSteadyState(SandboxHost host, ExecutionPlan plan)
    {
        _ = Measure(host, plan, WarmupIterations);
        var allocated = long.MaxValue;
        for (var sample = 0; sample < 3; sample++)
        {
            allocated = Math.Min(allocated, Measure(host, plan, MeasuredIterations));
        }

        return allocated / (double)MeasuredIterations;
    }

    private static long Measure(SandboxHost host, ExecutionPlan plan, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(plan, "main", SandboxValue.Unit, Options);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Pure interpreted execution unexpectedly became asynchronous.");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value { Value: 1 })
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "Unexpected execution result.");
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string UnrelatedCapability(int index, bool wildcard)
        => wildcard ? $"unrelated.{index}.*" : $"unrelated.{index}";

    private const string ModuleJson = """
    {
      "id": "revocation-allocation",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "log.write", "reason": "audit messages" }],
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 1 } }]
      }]
    }
    """;
}
