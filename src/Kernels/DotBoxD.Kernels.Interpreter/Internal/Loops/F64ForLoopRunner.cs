using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Internal;

using DotBoxD.Kernels;

internal static class F64ForLoopRunner
{
    private const long LoopFuel = 5;

    public static bool TryRun(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context,
        SandboxExecutionOptions options)
    {
        if (!CanUseFastPath(options, start, end))
        {
            return false;
        }

        if (frame.Layout.LoopPlans.TryGetF64ForRangePlan(statement, frame, out var cached))
        {
            return TryRun(
                statement,
                cached.TargetSlot,
                cached.Expression,
                cached.FuelPerIteration,
                binding: null,
                start,
                end,
                frame,
                context);
        }

        if (!TryCreateBodyPlan(statement, frame, context.Bindings, out var body, out var fuelPerIteration, out var binding))
        {
            return false;
        }

        ref var loopPlans = ref frame.Layout.LoopPlans;
        if (body.Expression.IsReusableForLoopPlan &&
            loopPlans.ShouldCacheF64ForRangePlan(statement))
        {
            loopPlans.CacheF64ForRangePlan(new F64ForLoopPlan(
                statement,
                body.TargetSlot,
                body.Expression,
                fuelPerIteration));
        }

        return TryRun(
            statement,
            body.TargetSlot,
            body.Expression,
            fuelPerIteration,
            binding,
            start,
            end,
            frame,
            context);
    }

    private static bool TryRun(
        ForRangeStatement statement,
        int targetSlot,
        F64ExpressionPlan expression,
        long fuelPerIteration,
        BindingDescriptor? binding,
        int start,
        int end,
        InterpreterFrame frame,
        SandboxContext context)
    {
        var iterations = (long)end - start;
        var bindingCalls = BindingCalls(iterations, expression.BindingCallCount);
        if (bindingCalls < 0 ||
            !context.CanBulkChargeLoopIterations(iterations, fuelPerIteration) ||
            (binding is not null && !context.CanBulkChargeBindingCalls(binding, bindingCalls)))
        {
            return false;
        }

        context.ChargeLoopIterations(iterations, fuelPerIteration);
        if (binding is not null)
        {
            context.ChargeBindingCalls(binding, bindingCalls);
        }
        var loopSlot = frame.GetSlot(statement.LocalName);
        var checkpoint = 4096;
        for (var i = start; i < end; i++)
        {
            frame.WriteRawInt32Slot(loopSlot, i);
            frame.WriteRawDoubleSlot(targetSlot, expression.Evaluate(frame));
            if (--checkpoint == 0)
            {
                context.Checkpoint();
                checkpoint = 4096;
            }
        }

        return true;
    }

    private static bool CanUseFastPath(SandboxExecutionOptions options, int start, int end)
        => !options.EnableDebugTrace && start < end;

    private static bool TryCreateBodyPlan(
        ForRangeStatement statement,
        InterpreterFrame frame,
        IBindingCatalog bindings,
        out AssignmentPlan body,
        out long fuelPerIteration,
        out BindingDescriptor binding)
    {
        body = default;
        fuelPerIteration = LoopFuel;
        binding = null!;
        if (statement.Body.Count != 1 ||
            statement.Body[0] is not AssignmentStatement assignment ||
            !F64ExpressionPlan.TryCreate(assignment.Value, frame, assignment.Name, bindings, out var expression, out binding))
        {
            return false;
        }

        var targetSlot = frame.GetSlot(assignment.Name);
        if (!frame.IsF64Slot(targetSlot))
        {
            return false;
        }

        body = new AssignmentPlan(targetSlot, expression);
        fuelPerIteration += 1 + expression.FuelCost;
        return true;
    }

    private static long BindingCalls(long iterations, int callsPerIteration)
    {
        try
        {
            return checked(iterations * callsPerIteration);
        }
        catch (OverflowException)
        {
            return -1;
        }
    }

    private readonly record struct AssignmentPlan(int TargetSlot, F64ExpressionPlan Expression);
}
