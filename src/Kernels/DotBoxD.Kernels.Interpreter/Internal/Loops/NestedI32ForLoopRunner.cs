using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal.Loops;

using DotBoxD.Kernels;

/// <summary>
/// Executes a narrow nested-for shape without redispatching the inner statement
/// for every outer iteration. The inner assignment plan must already be present
/// in the layout cache, so cold or unsupported loops retain the general path.
/// </summary>
internal static class NestedI32ForLoopRunner
{
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
        if (options.EnableDebugTrace || start >= end)
        {
            return false;
        }

        if (!TryGetInnerLoop(statement, out var inner) ||
            !TryCreatePlan(statement, inner, frame, out var plan))
        {
            return false;
        }

        Run(start, end, plan, frame, context);
        return true;
    }

    private static void Run(
        int start,
        int end,
        NestedLoopPlan plan,
        InterpreterFrame frame,
        SandboxContext context)
    {
        for (var i = start; i < end; i++)
        {
            context.ChargeLoopIteration(OuterLoopFuel);
            frame.WriteRawInt32Slot(plan.OuterLoopSlot, i);

            // Preserve the generic path's separate statement/start/end charges.
            // Cancellation and metering order require that they are not folded
            // into one bulk charge.
            context.ChargeFuel(StatementFuel);
            context.ChargeFuel(BoundFuel);
            var nestedStart = plan.InnerStart.Read(frame);
            context.ChargeFuel(BoundFuel);
            var nestedEnd = plan.InnerEnd.Read(frame);

            if (nestedStart < nestedEnd)
            {
                I32ForLoopRunner.RunValidatedCachedSingleAssignment(
                    nestedStart,
                    nestedEnd,
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
        if (!frame.IsInt32Slot(outerLoopSlot) || !frame.IsInt32Slot(innerLoopSlot))
        {
            plan = default;
            return false;
        }

        if (!TryCreateBound(inner.Start, frame, outerLoopSlot, out var innerStart) ||
            !TryCreateBound(inner.End, frame, outerLoopSlot, out var innerEnd) ||
            !frame.Layout.LoopPlans.TryGetI32ForRangePlan(
                inner,
                frame,
                innerLoopSlot,
                outerLoopSlot,
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

    private static bool TryCreateBound(
        Expression expression,
        InterpreterFrame frame,
        int slotWrittenBeforeBoundRead,
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
            if (frame.IsInt32Slot(slot) &&
                (slot == slotWrittenBeforeBoundRead || frame.IsSlotAssigned(slot)))
            {
                plan = BoundPlan.FromRawSlot(slot);
                return true;
            }
        }

        plan = default;
        return false;
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
        I32ForLoopPlan InnerPlan);
}
