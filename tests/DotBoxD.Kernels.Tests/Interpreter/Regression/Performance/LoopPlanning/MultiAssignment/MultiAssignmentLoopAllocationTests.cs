using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class MultiAssignmentLoopAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Warmed_for_range_plan_removes_per_entry_multi_assignment_planning()
    {
        using var host = SandboxTestHost.Create();
        var plan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            host,
            MultiAssignmentAllocationModules.ForRange);
        var interpreter = new SandboxInterpreter();
        var options = MultiAssignmentLoopTestRuntime.Options();

        var entered = MeasureCase(interpreter, plan, options, input: 1, expectedValue: 6);
        var zero = MeasureCase(interpreter, plan, options, input: 0, expectedValue: 0);

        Assert.Equal(25, entered.Usage.FuelUsed);
        Assert.Equal(1, entered.Usage.LoopIterations);
        Assert.Equal(10, zero.Usage.FuelUsed);
        Assert.Equal(0, zero.Usage.LoopIterations);
        Assert.Equal(0, entered.Usage.AllocatedBytes);
        Assert.Equal(0, zero.Usage.AllocatedBytes);
        Assert.Equal(0, entered.Usage.HostCalls);
        Assert.Equal(0, zero.Usage.HostCalls);
        Assert.Equal(6L * MeasurementIterations, entered.Checksum);
        Assert.Equal(0, zero.Checksum);

        var enteredLoopBytes = (entered.AllocatedBytes - zero.AllocatedBytes) /
                               (double)MeasurementIterations;
        AssertNearZero(
            enteredLoopBytes,
            "one warmed for-range entry",
            "the uncached multi-assignment planner adds about 280 B");
    }

    [Fact]
    public async Task Warmed_while_plan_matches_the_two_local_no_loop_control()
    {
        using var host = SandboxTestHost.Create();
        var whilePlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            host,
            MultiAssignmentAllocationModules.While);
        var noLoopPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            host,
            MultiAssignmentAllocationModules.WhileNoLoop);
        var interpreter = new SandboxInterpreter();
        var options = MultiAssignmentLoopTestRuntime.Options();

        var zero = MeasureCase(interpreter, whilePlan, options, input: 0, expectedValue: 0);
        var noLoop = MeasureCase(interpreter, noLoopPlan, options, input: 0, expectedValue: 0);
        var entered = MeasureCase(interpreter, whilePlan, options, input: 1, expectedValue: 2);

        Assert.Equal(11, zero.Usage.FuelUsed);
        Assert.Equal(0, zero.Usage.LoopIterations);
        Assert.Equal(7, noLoop.Usage.FuelUsed);
        Assert.Equal(0, noLoop.Usage.LoopIterations);
        Assert.Equal(27, entered.Usage.FuelUsed);
        Assert.Equal(1, entered.Usage.LoopIterations);
        Assert.Equal(0, zero.Usage.AllocatedBytes);
        Assert.Equal(0, noLoop.Usage.AllocatedBytes);
        Assert.Equal(0, entered.Usage.AllocatedBytes);
        Assert.Equal(0, zero.Usage.HostCalls);
        Assert.Equal(0, noLoop.Usage.HostCalls);
        Assert.Equal(0, entered.Usage.HostCalls);
        Assert.Equal(2L * MeasurementIterations, entered.Checksum);

        var plannedLoopBytes = (zero.AllocatedBytes - noLoop.AllocatedBytes) /
                               (double)MeasurementIterations;
        var enteredBodyBytes = (entered.AllocatedBytes - zero.AllocatedBytes) /
                               (double)MeasurementIterations;
        AssertNearZero(
            plannedLoopBytes,
            "one warmed zero-iteration while",
            "the uncached condition and body planners add about 504 B");
        AssertNearZero(
            enteredBodyBytes,
            "one body iteration through the retained while plan",
            "cached assignment arrays must not be rebuilt on entry");
    }

    private static MultiAssignmentLoopTestRuntime.Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int input,
        int expectedValue)
    {
        var value = MultiAssignmentLoopTestRuntime.Input(input);
        _ = MultiAssignmentLoopTestRuntime.MeasureSuccessful(
            interpreter,
            plan,
            value,
            options,
            expectedValue,
            WarmupIterations);
        MultiAssignmentLoopTestRuntime.ForceGc();
        return MultiAssignmentLoopTestRuntime.MeasureSuccessful(
            interpreter,
            plan,
            value,
            options,
            expectedValue,
            MeasurementIterations);
    }

    private static void AssertNearZero(
        double allocatedBytesPerExecution,
        string scenario,
        string legacyCost)
    {
        Assert.True(
            Math.Abs(allocatedBytesPerExecution) <= 8,
            $"{scenario} differed from its control by {allocatedBytesPerExecution:F1} B/execution; " +
            $"{legacyCost}.");
    }
}
