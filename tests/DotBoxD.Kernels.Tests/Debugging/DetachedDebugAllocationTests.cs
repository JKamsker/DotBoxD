using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Debugging;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class DetachedDebugAllocationTests
{
    private const int MeasuredExecutions = 200;

    [Fact]
    [Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
    public async Task Detached_checkpoint_branches_do_not_allocate_per_node()
    {
        using var host = SandboxTestHost.Create();
        var shortPlan = await PrepareAsync(host, statementCount: 1);
        var longPlan = await PrepareAsync(host, statementCount: 1024);

        var shortBytes = MeasurePerExecution(host, shortPlan);
        var longBytes = MeasurePerExecution(host, longPlan);

        Assert.True(
            longBytes <= shortBytes + 128,
            $"Detached execution allocation grew with checkpoint sites: {shortBytes} B/run at 1 node versus {longBytes} B/run at 1024 nodes.");
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        global::DotBoxD.Hosting.Execution.SandboxHost host,
        int statementCount)
    {
        var span = new SourceSpan(0, 0);
        var statements = Enumerable.Range(0, statementCount)
            .Select(_ => (Statement)new ExpressionStatement(new LiteralExpression(SandboxValue.Unit, span), span))
            .Append(new ReturnStatement(new LiteralExpression(SandboxValue.Unit, span), span))
            .ToArray();
        var module = new SandboxModule(
            "detached-debug-allocation-" + statementCount,
            new SemVersion(1, 0, 0),
            new SemVersion(1, 0, 0),
            [],
            [new SandboxFunction("main", true, [], SandboxType.Unit, statements)],
            new Dictionary<string, string>());
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(20_000).Build());
    }

    private static long MeasurePerExecution(global::DotBoxD.Hosting.Execution.SandboxHost host, ExecutionPlan plan)
    {
        for (var index = 0; index < 20; index++)
        {
            Execute(host, plan);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < MeasuredExecutions; index++)
        {
            Execute(host, plan);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / MeasuredExecutions;
    }

    private static void Execute(global::DotBoxD.Hosting.Execution.SandboxHost host, ExecutionPlan plan)
    {
        var result = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted })
            .GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
    }
}
