using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops.F64.NestedF64ForLoopTestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops.F64;

public sealed class NestedF64ForLoopRunnerTests
{
    [Fact]
    public void Cached_fixed_bound_plan_preserves_value_and_resource_order()
    {
        var setup = CreateSetup();
        using var context = CreateContext();

        var handled = TryRun(setup, context, start: 0, end: 3);

        Assert.True(handled);
        Assert.True(setup.Frame.TryReadDouble("total", out var total));
        Assert.Equal(BitConverter.DoubleToInt64Bits(4.5), BitConverter.DoubleToInt64Bits(total));
        Assert.Equal(51, context.Budget.FuelUsed);
        Assert.Equal(6, context.Budget.LoopIterations);
        Assert.Equal(2, setup.Frame.ReadInt32(setup.Outer.LocalName));
        Assert.Equal(0, setup.Frame.ReadInt32(setup.Inner.LocalName));
    }

    [Theory]
    [InlineData(50L, 100L)]
    [InlineData(1_000L, 5L)]
    public void Insufficient_aggregate_budget_falls_back_before_mutation(
        long maxFuel,
        long maxLoopIterations)
    {
        var setup = CreateSetup();
        using var context = CreateContext(maxFuel, maxLoopIterations);

        var handled = TryRun(setup, context, start: 0, end: 3);

        Assert.False(handled);
        AssertUnchanged(setup, context);
    }

    [Fact]
    public void Overflowing_aggregate_work_falls_back_before_mutation()
    {
        var setup = CreateSetup(BoundKind.OverflowingLiterals);
        using var context = CreateContext(long.MaxValue, long.MaxValue);

        var handled = TryRun(setup, context, int.MinValue, int.MaxValue);

        Assert.False(handled);
        AssertUnchanged(setup, context);
    }

    [Theory]
    [InlineData((int)BoundKind.Arithmetic, false)]
    [InlineData((int)BoundKind.OuterLoopSlot, true)]
    [InlineData((int)BoundKind.InnerLoopSlot, true)]
    public void Unstable_or_non_leaf_bounds_fail_closed(
        int boundKind,
        bool assignLoopSlots)
    {
        var setup = CreateSetup((BoundKind)boundKind, assignLoopSlots: assignLoopSlots);
        using var context = CreateContext();

        var handled = TryRun(setup, context, start: 0, end: 1);

        Assert.False(handled);
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
    }

    [Fact]
    public void Cold_or_unassigned_cached_plans_fail_closed()
    {
        var cold = CreateSetup(cachePlan: false);
        var unassigned = CreateSetup(assignTarget: false);
        using var coldContext = CreateContext();
        using var unassignedContext = CreateContext();

        Assert.False(TryRun(cold, coldContext, start: 0, end: 1));
        Assert.False(TryRun(unassigned, unassignedContext, start: 0, end: 1));
        Assert.Equal(0, coldContext.Budget.FuelUsed);
        Assert.Equal(0, unassignedContext.Budget.FuelUsed);
    }

    [Fact]
    public void Debug_trace_option_retains_generic_dispatch()
    {
        var setup = CreateSetup();
        using var context = CreateContext();

        var handled = NestedF64ForLoopRunner.TryRun(
            setup.Outer,
            start: 0,
            end: 1,
            setup.Frame,
            context,
            new SandboxExecutionOptions { EnableDebugTrace = true });

        Assert.False(handled);
        AssertUnchanged(setup, context);
    }

    [Fact]
    public void Precancellation_is_observed_before_outer_mutation()
    {
        var setup = CreateSetup();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = CreateContext(cancellationToken: cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            TryRun(setup, context, start: 0, end: 1));
        AssertUnchanged(setup, context);
    }

    private static bool TryRun(
        RunnerSetup setup,
        SandboxContext context,
        int start,
        int end)
        => NestedF64ForLoopRunner.TryRun(
            setup.Outer,
            start,
            end,
            setup.Frame,
            context,
            new SandboxExecutionOptions());

    private static void AssertUnchanged(RunnerSetup setup, SandboxContext context)
    {
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);
        Assert.False(setup.Frame.IsSlotAssigned(setup.Frame.GetSlot(setup.Outer.LocalName)));
        Assert.True(setup.Frame.TryReadDouble("total", out var total));
        Assert.Equal(0, total);
    }
}
