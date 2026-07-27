using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.ScalarReturns;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ScalarReturnAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Theory]
    [InlineData("I64", "i64", "3", false)]
    [InlineData("I64", "i64", "3", true)]
    [InlineData("F64", "f64", "0.25", false)]
    [InlineData("F64", "f64", "0.25", true)]
    public async Task X1_and_x8_return_trees_match_the_x0_allocation_floor(
        string type,
        string literalName,
        string increment,
        bool useRawStep)
    {
        using var host = SandboxTestHost.Create();
        var plans = new Dictionary<int, ExecutionPlan>();
        var lane = useRawStep ? "raw" : "literal";
        foreach (var operationCount in new[] { 0, 1, 8 })
        {
            plans.Add(
                operationCount,
                await ScalarReturnTestSupport.PrepareAsync(
                    host,
                    ScalarReturnTestModules.Recurrences(
                        $"scalar-return-{type.ToLowerInvariant()}-{lane}-{operationCount}",
                        type,
                        literalName,
                        increment,
                        operationCount,
                        useRawStep)));
        }

        var scenario = Scenario.Create(type, useRawStep);
        var interpreter = new SandboxInterpreter();
        var options = ScalarReturnTestSupport.Options();
        var zero = MeasureCase(interpreter, plans[0], scenario, options, operationCount: 0);
        var one = MeasureCase(interpreter, plans[1], scenario, options, operationCount: 1);
        var eight = MeasureCase(interpreter, plans[8], scenario, options, operationCount: 8);

        AssertUsage(zero, operationCount: 0, scenario.CollectionElements);
        AssertUsage(one, operationCount: 1, scenario.CollectionElements);
        AssertUsage(eight, operationCount: 8, scenario.CollectionElements);
        AssertAllocationDelta(zero, one, type, useRawStep, operationCount: 1);
        AssertAllocationDelta(zero, eight, type, useRawStep, operationCount: 8);
    }

    [Theory]
    [InlineData("I64", "i64", "42")]
    [InlineData("F64", "f64", "42.5")]
    public async Task Literal_return_keeps_the_prepared_value_instance(
        string type,
        string literalName,
        string literal)
    {
        using var host = SandboxTestHost.Create();
        var plan = await ScalarReturnTestSupport.PrepareAsync(
            host,
            ScalarReturnTestModules.Literal(
                $"scalar-return-{literalName}-literal-identity",
                type,
                literalName,
                literal));

        var first = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            ScalarReturnTestSupport.Scalar(type, 0));
        var second = await ScalarReturnTestSupport.ExecuteAsync(
            host,
            plan,
            ScalarReturnTestSupport.Scalar(type, 0));

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Same(first.Value, second.Value);
        ScalarReturnTestSupport.AssertUsage(first.ResourceUsage, fuel: 3);
        ScalarReturnTestSupport.AssertUsage(second.ResourceUsage, fuel: 3);
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        Scenario scenario,
        SandboxExecutionOptions options,
        int operationCount)
    {
        _ = Measure(interpreter, plan, scenario, options, operationCount, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, scenario, options, operationCount, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        Scenario scenario,
        SandboxExecutionOptions options,
        int operationCount,
        int iterations)
    {
        var expectedValue = scenario.InitialValue + (operationCount * scenario.Step);
        SandboxResourceUsage? expectedUsage = null;
        double checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                scenario.Input,
                options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("scalar-return allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var value = ScalarReturnTestSupport.NumericValue(result.Value);
            if (value != expectedValue)
            {
                throw new Xunit.Sdk.XunitException($"expected {expectedValue}, got {value}");
            }

            checksum += value;
            expectedUsage ??= result.ResourceUsage;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new Xunit.Sdk.XunitException("resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage!);
    }

    private static void AssertUsage(Measurement measurement, int operationCount, long collectionElements)
        => ScalarReturnTestSupport.AssertUsage(
            measurement.Usage,
            fuel: 3 + (2L * operationCount),
            collectionElements: collectionElements);

    private static void AssertAllocationDelta(
        Measurement zero,
        Measurement candidate,
        string type,
        bool useRawStep,
        int operationCount)
    {
        var bytesPerExecution = (candidate.AllocatedBytes - zero.AllocatedBytes) /
                                (double)MeasurementIterations;
        Assert.True(
            Math.Abs(bytesPerExecution) <= 1,
            $"{type} {(useRawStep ? "raw-variable" : "literal")} x{operationCount} return tree " +
            $"added {bytesPerExecution:F3} B/execution " +
            $"(candidate={candidate.AllocatedBytes}, x0={zero.AllocatedBytes}).");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Scenario(
        SandboxValue Input,
        double InitialValue,
        double Step,
        long CollectionElements)
    {
        public static Scenario Create(string type, bool useRawStep)
        {
            var initial = type == "I64" ? 2D : 0.5D;
            var step = type == "I64" ? 3D : 0.25D;
            var initialValue = ScalarReturnTestSupport.Scalar(type, initial);
            return useRawStep
                ? new Scenario(
                    SandboxValue.FromList(
                        [initialValue, ScalarReturnTestSupport.Scalar(type, step)],
                        type == "I64" ? SandboxType.I64 : SandboxType.F64),
                    initial,
                    step,
                    CollectionElements: 2)
                : new Scenario(initialValue, initial, step, CollectionElements: 0);
        }
    }

    private readonly record struct Measurement(
        long AllocatedBytes,
        double Checksum,
        SandboxResourceUsage Usage);
}
