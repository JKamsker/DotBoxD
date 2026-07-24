using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class GenericPrimitiveExpressionAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Deep_left_and_right_trees_keep_the_shallow_allocation_floor()
    {
        using var host = SandboxTestHost.Create();
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var shallowLeft = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 8, leftDeep: true));
        var shallowRight = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 8, leftDeep: false));
        var deepLeft = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 96, leftDeep: true));
        var deepRight = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.F64Comparison(depth: 96, leftDeep: false));

        var shallowLeftBytes = MeasureBytes(interpreter, shallowLeft, options, depth: 8);
        var shallowRightBytes = MeasureBytes(interpreter, shallowRight, options, depth: 8);
        var deepLeftBytes = MeasureBytes(interpreter, deepLeft, options, depth: 96);
        var deepRightBytes = MeasureBytes(interpreter, deepRight, options, depth: 96);

        AssertAllocationFloor(shallowLeftBytes, deepLeftBytes, "left-deep");
        AssertAllocationFloor(shallowRightBytes, deepRightBytes, "right-deep");
        Assert.InRange(Math.Abs(deepLeftBytes - deepRightBytes), 0, 1);
    }

    [Fact]
    public async Task Deep_i64_tree_keeps_the_shallow_allocation_floor()
    {
        using var host = SandboxTestHost.Create();
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var shallow = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.I64Comparison(depth: 8, leftDeep: true));
        var deep = await PrepareAsync(
            host,
            GenericPrimitiveExpressionModules.I64Comparison(depth: 96, leftDeep: true));

        var shallowBytes = MeasureBytes(interpreter, shallow, options, depth: 8);
        var deepBytes = MeasureBytes(interpreter, deep, options, depth: 96);

        AssertAllocationFloor(shallowBytes, deepBytes, "I64 left-deep");
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        SandboxModule module)
        => await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowPureComputation()
                .WithFuel(1_000)
                .WithMaxAllocatedBytes(long.MaxValue)
                .Build());

    private static double MeasureBytes(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int depth)
    {
        _ = Measure(interpreter, plan, options, depth, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, options, depth, MeasurementIterations) /
               (double)MeasurementIterations;
    }

    private static long Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int depth,
        int iterations)
    {
        var expectedFuel = GenericPrimitiveExpressionModules.ExpectedComparisonFuel(depth);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new Xunit.Sdk.XunitException(
                    "generic expression allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded ||
                result.Value is not BoolValue { Value: true } ||
                result.ResourceUsage.FuelUsed != expectedFuel)
            {
                throw new Xunit.Sdk.XunitException(
                    result.Error?.SafeMessage ?? "generic expression measurement changed semantics");
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static void AssertAllocationFloor(double shallow, double deep, string lane)
        => Assert.True(
            Math.Abs(deep - shallow) <= 1,
            $"{lane} depth 96 added {deep - shallow:F1} B/execution " +
            $"(shallow={shallow:F1}, deep={deep:F1}).");

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
