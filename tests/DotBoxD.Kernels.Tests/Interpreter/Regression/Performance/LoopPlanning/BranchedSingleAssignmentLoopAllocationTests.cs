using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class BranchedSingleAssignmentLoopAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Theory]
    [InlineData("i32", 560, 392, 1_008)]
    [InlineData("f64", 640, 448, 1_200)]
    public async Task Branch_plan_shapes_preserve_expected_managed_allocation(
        string type,
        double expectedSingleOverhead,
        double expectedEmptyOverhead,
        double expectedManyOverhead)
    {
        using var host = SandboxTestHost.Create();
        var singlePlan = await PrepareAsync(host, BranchedLoopAllocationModules.OneAssignment(type));
        var emptyPlan = await PrepareAsync(host, BranchedLoopAllocationModules.EmptyBranch(type));
        var manyPlan = await PrepareAsync(host, BranchedLoopAllocationModules.TwoAssignments(type));
        var controlPlan = await PrepareAsync(host, BranchedLoopAllocationModules.NoBranch(type));
        var interpreter = new SandboxInterpreter();
        var options = CreateOptions();

        var single = MeasureCase(interpreter, singlePlan, options, expectedValue: 3, expectedFuel: 23);
        var empty = MeasureCase(interpreter, emptyPlan, options, expectedValue: 1, expectedFuel: 19);
        var many = MeasureCase(interpreter, manyPlan, options, expectedValue: 6, expectedFuel: 29);
        var control = MeasureCase(interpreter, controlPlan, options, expectedValue: 3, expectedFuel: 17);

        var singleOverhead = BytesPerExecution(single.AllocatedBytes - control.AllocatedBytes);
        var emptyOverhead = BytesPerExecution(empty.AllocatedBytes - control.AllocatedBytes);
        var manyOverhead = BytesPerExecution(many.AllocatedBytes - control.AllocatedBytes);
        Assert.InRange(singleOverhead, expectedSingleOverhead - 12, expectedSingleOverhead + 12);
        Assert.InRange(emptyOverhead, expectedEmptyOverhead - 12, expectedEmptyOverhead + 12);
        Assert.InRange(manyOverhead, expectedManyOverhead - 12, expectedManyOverhead + 12);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("f64")]
    public async Task Empty_then_branch_and_single_else_branch_preserve_accounting(string type)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.EmptyBranch(type));

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(2),
            CreateOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(5, ReadIntegralValue(result.Value!));
        Assert.Equal(34, result.ResourceUsage.FuelUsed);
        Assert.Equal(2, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("f64")]
    public async Task Multiple_assignments_remain_sequential_on_both_branches(string type)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.TwoAssignments(type));

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(2),
            CreateOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(14, ReadIntegralValue(result.Value!));
        Assert.Equal(48, result.ResourceUsage.FuelUsed);
        Assert.Equal(2, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("f64")]
    public async Task Unsupported_branch_statement_falls_back_without_partial_execution(string type)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.UnsupportedElse(type));

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(2),
            CreateOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(3, ReadIntegralValue(result.Value!));
        Assert.Equal(2, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("f64")]
    public async Task Debug_trace_keeps_branch_and_assignment_statement_events(string type)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.OneAssignment(type));

        var result = await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(1),
            CreateOptions(enableDebugTrace: true),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(3, ReadIntegralValue(result.Value!));
        Assert.Contains(result.AuditEvents, IsDebugTraceFor<IfStatement>);
        Assert.Contains(result.AuditEvents, IsDebugTraceFor<AssignmentStatement>);
    }

    private static SandboxExecutionOptions CreateOptions(bool enableDebugTrace = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = enableDebugTrace
        };

    private static bool IsDebugTraceFor<TNode>(SandboxAuditEvent auditEvent)
        => auditEvent.Kind == "DebugTrace" &&
           auditEvent.Message?.Contains(
               $"node=statement:{typeof(TNode).Name}",
               StringComparison.Ordinal) == true;

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        long expectedValue,
        long expectedFuel)
    {
        var input = SandboxValue.FromInt32(1);
        _ = Measure(
            interpreter,
            plan,
            input,
            options,
            expectedValue,
            expectedFuel,
            WarmupIterations);
        ForceGc();
        return Measure(
            interpreter,
            plan,
            input,
            options,
            expectedValue,
            expectedFuel,
            MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        long expectedValue,
        long expectedFuel,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("branched allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ReadIntegralValue(result.Value!);
            if (actual != expectedValue)
            {
                throw new Xunit.Sdk.XunitException($"expected {expectedValue}, got {actual}");
            }

            if (result.ResourceUsage is not
                {
                    FuelUsed: var fuel,
                    LoopIterations: 1,
                    AllocatedBytes: 0,
                    HostCalls: 0
                } || fuel != expectedFuel)
            {
                throw new Xunit.Sdk.XunitException("branched allocation resource usage changed");
            }

        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build());
    }

    private static long ReadIntegralValue(SandboxValue value)
        => value switch
        {
            I32Value i32 => i32.Value,
            F64Value f64 when f64.Value == Math.Truncate(f64.Value) => checked((long)f64.Value),
            _ => throw new Xunit.Sdk.XunitException("expected an integral I32 or F64 value")
        };

    private static double BytesPerExecution(long allocatedBytes)
        => allocatedBytes / (double)MeasurementIterations;

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(long AllocatedBytes);
}
