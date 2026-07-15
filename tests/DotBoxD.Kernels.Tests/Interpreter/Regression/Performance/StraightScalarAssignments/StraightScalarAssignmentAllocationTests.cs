using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class StraightScalarAssignmentAllocationTests
{
    private const int RecurrenceCount = 8;
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Theory]
    [InlineData("I64", "i64", "3", false)]
    [InlineData("F64", "f64", "0.25", false)]
    [InlineData("I64", "i64", "3", true)]
    [InlineData("F64", "f64", "0.25", true)]
    public async Task Eight_recurrences_match_same_shape_zero_recurrence_allocation(
        string type,
        string literalName,
        string increment,
        bool useRawStep)
    {
        var lane = useRawStep ? "raw-step" : "literal-step";
        using var host = SandboxTestHost.Create();
        var zeroPlan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Recurrences(
                $"straight-{type.ToLowerInvariant()}-{lane}-assignment-x0",
                type,
                literalName,
                increment,
                count: 0,
                useRawStep));
        var eightPlan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Recurrences(
                $"straight-{type.ToLowerInvariant()}-{lane}-assignment-x8",
                type,
                literalName,
                increment,
                RecurrenceCount,
                useRawStep));
        var scenario = Scenario.For(type, useRawStep);
        var interpreter = new SandboxInterpreter();
        var options = StraightScalarAssignmentTestSupport.Options();

        var zero = MeasureCase(interpreter, zeroPlan, scenario, options, scenario.InitialValue);
        var eight = MeasureCase(interpreter, eightPlan, scenario, options, scenario.ExpectedValue);

        StraightScalarAssignmentTestSupport.AssertUsage(
            zero.Usage,
            fuel: 3,
            collectionElements: scenario.CollectionElements);
        StraightScalarAssignmentTestSupport.AssertUsage(
            eight.Usage,
            fuel: 35,
            collectionElements: scenario.CollectionElements);
        var bytesPerExecution = (eight.AllocatedBytes - zero.AllocatedBytes) /
                                (double)MeasurementIterations;
        Assert.True(
            Math.Abs(bytesPerExecution) <= 1,
            $"Eight straight {type} {lane} recurrences added {bytesPerExecution:F3} B/execution " +
            $"(x8={eight.AllocatedBytes}, x0={zero.AllocatedBytes}). " +
            "The raw assignment evaluator must not box operands or results.");
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        Scenario scenario,
        SandboxExecutionOptions options,
        double expectedValue)
    {
        _ = Measure(interpreter, plan, scenario, options, expectedValue, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, scenario, options, expectedValue, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        Scenario scenario,
        SandboxExecutionOptions options,
        double expectedValue,
        int iterations)
    {
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
                throw new InvalidOperationException(
                    "straight scalar assignment allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var value = NumericValue(result.Value!);
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

    private static double NumericValue(SandboxValue value)
        => value switch
        {
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected straight assignment value")
        };

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Scenario(
        SandboxValue Input,
        double InitialValue,
        double ExpectedValue,
        long CollectionElements)
    {
        public static Scenario For(string type, bool useRawStep)
        {
            var (initial, step, expected) = type switch
            {
                "I64" => (2.0, 3.0, 26.0),
                "F64" => (0.5, 0.25, 2.5),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
            };

            if (!useRawStep)
            {
                return new Scenario(Scalar(type, initial), initial, expected, CollectionElements: 0);
            }

            return new Scenario(
                SandboxValue.FromList(
                    [Scalar(type, initial), Scalar(type, step)],
                    type == "I64" ? SandboxType.I64 : SandboxType.F64),
                initial,
                expected,
                CollectionElements: 2);
        }

        private static SandboxValue Scalar(string type, double value)
            => type == "I64"
                ? SandboxValue.FromInt64((long)value)
                : SandboxValue.FromDouble(value);
    }

    private readonly record struct Measurement(
        long AllocatedBytes,
        double Checksum,
        SandboxResourceUsage Usage);
}
