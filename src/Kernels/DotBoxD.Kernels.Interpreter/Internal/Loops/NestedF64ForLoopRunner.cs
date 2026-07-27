using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

using DotBoxD.Kernels;

/// <summary>
/// Executes one fixed-bound inner F64 loop without redispatching its statement for
/// every outer iteration. The inner plan remains owned and validated by the frame
/// layout cache; unsupported or budget-sensitive shapes fail closed before mutation.
/// </summary>
internal static class NestedF64ForLoopRunner
{
    private const long OuterFuelPerIteration = 8;
    private const long OuterLoopFuel = 5;
    private const long StatementFuel = 1;
    private const long BoundFuel = 1;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (options.EnableDebugTrace || start >= end ||
            !TryGetInnerLoop(statement, out var inner) ||
            !TryCreatePlan(statement, inner, frame, out var plan))
        {
            return false;
        }

        var innerStart = plan.InnerStart.Read(frame);
        var innerEnd = plan.InnerEnd.Read(frame);
        if (!TryCalculateRequiredWork(
                start,
                end,
                innerStart,
                innerEnd,
                plan.InnerPlan.FuelPerIteration,
                out var requiredLoops,
                out var requiredFuel) ||
            !context.CanBulkChargeLoopWork(requiredLoops, requiredFuel))
        {
            return false;
        }

        Run(start, end, plan, frame, context);
        return true;
    }

    private static void Run(
        int start,
        int end,
        in NestedLoopPlan plan,
        InterpreterFrame frame,
        SandboxContext context)
    {
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(OuterLoopFuel);
            frame.WriteRawInt32Slot(plan.OuterLoopSlot, i);

            context.ChargeFuel(StatementFuel);
            context.ChargeFuel(BoundFuel);
            var innerStart = plan.InnerStart.Read(frame);
            context.ChargeFuel(BoundFuel);
            var innerEnd = plan.InnerEnd.Read(frame);

            if (innerStart < innerEnd)
            {
                F64ForLoopRunner.RunValidatedCachedSingleAssignment(
                    innerStart,
                    innerEnd,
                    frame,
                    context,
                    plan.InnerLoopSlot,
                    plan.InnerPlan);
            }
        }
    }

    private static bool TryGetInnerLoop(
        ForRangeStatement statement,
        out ForRangeStatement inner)
    {
        if (statement.Body.Count == 1 &&
            statement.Body[0] is ForRangeStatement candidate &&
            candidate.Body.Count == 1)
        {
            inner = candidate;
            return true;
        }

        inner = null!;
        return false;
    }

    private static bool TryCreatePlan(
        ForRangeStatement outer,
        ForRangeStatement inner,
        InterpreterFrame frame,
        out NestedLoopPlan plan)
    {
        var outerLoopSlot = frame.GetSlot(outer.LocalName);
        var innerLoopSlot = frame.GetSlot(inner.LocalName);
        if (!frame.IsInt32Slot(outerLoopSlot) ||
            !frame.IsInt32Slot(innerLoopSlot) ||
            !TryCreateStableBound(
                inner.Start,
                frame,
                outerLoopSlot,
                innerLoopSlot,
                out var innerStart) ||
            !TryCreateStableBound(
                inner.End,
                frame,
                outerLoopSlot,
                innerLoopSlot,
                out var innerEnd) ||
            !frame.Layout.LoopPlans.TryGetF64ForRangePlan(
                inner,
                frame,
                out var innerPlan))
        {
            plan = default;
            return false;
        }

        plan = new NestedLoopPlan(
            outerLoopSlot,
            innerLoopSlot,
            innerStart,
            innerEnd,
            innerPlan);
        return true;
    }

    private static bool TryCreateStableBound(
        Expression expression,
        InterpreterFrame frame,
        int outerLoopSlot,
        int innerLoopSlot,
        out BoundPlan plan)
    {
        if (expression is LiteralExpression { Value: I32Value literal })
        {
            plan = BoundPlan.FromLiteral(literal.Value);
            return true;
        }

        if (expression is VariableExpression variable)
        {
            var slot = frame.GetSlot(variable.Name);
            if (slot != outerLoopSlot &&
                slot != innerLoopSlot &&
                frame.IsInt32Slot(slot) &&
                frame.IsSlotAssigned(slot))
            {
                plan = BoundPlan.FromRawSlot(slot);
                return true;
            }
        }

        plan = default;
        return false;
    }

    private static bool TryCalculateRequiredWork(
        int outerStart,
        int outerEnd,
        int innerStart,
        int innerEnd,
        long innerFuelPerIteration,
        out long requiredLoops,
        out long requiredFuel)
    {
        try
        {
            var outerIterations = (long)outerEnd - outerStart;
            var innerIterations = Math.Max(0, (long)innerEnd - innerStart);
            var innerExecutions = checked(outerIterations * innerIterations);
            requiredLoops = checked(outerIterations + innerExecutions);
            requiredFuel = checked(
                (outerIterations * OuterFuelPerIteration) +
                (innerExecutions * innerFuelPerIteration));
            return true;
        }
        catch (OverflowException)
        {
            requiredLoops = 0;
            requiredFuel = 0;
            return false;
        }
    }

    private readonly struct BoundPlan
    {
        private readonly int _value;
        private readonly bool _isRawSlot;

        private BoundPlan(int value, bool isRawSlot)
        {
            _value = value;
            _isRawSlot = isRawSlot;
        }

        public static BoundPlan FromLiteral(int value) => new(value, isRawSlot: false);

        public static BoundPlan FromRawSlot(int slot) => new(slot, isRawSlot: true);

        public int Read(InterpreterFrame frame)
            => _isRawSlot ? frame.ReadRawInt32Slot(_value) : _value;
    }

    private readonly record struct NestedLoopPlan(
        int OuterLoopSlot,
        int InnerLoopSlot,
        BoundPlan InnerStart,
        BoundPlan InnerEnd,
        F64ForLoopPlan InnerPlan);
}
