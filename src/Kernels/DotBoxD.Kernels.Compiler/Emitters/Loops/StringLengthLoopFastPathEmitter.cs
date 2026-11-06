using System.Reflection.Emit;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal static class StringLengthLoopFastPathEmitter
{
    private const int LoopFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !RawForRangeEmitter.CanEmitBound(range.Start, stackPlan) ||
            !RawForRangeEmitter.CanEmitBound(range.End, stackPlan) ||
            !TryCreatePlan(range, stackPlan, bindings, out var plan))
        {
            return false;
        }

        EmitLoop(range, il, declare, plan);
        return true;
    }

    private static bool TryCreatePlan(
        ForRangeStatement range,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        out LoopPlan plan)
    {
        plan = default;
        if (range.Body.Count != 1 ||
            range.Body[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.I32 ||
            !CanUseDirectStringLength(bindings))
        {
            return false;
        }

        if (TryGetStringLengthCall(assignment.Value, stackPlan, out var source))
        {
            plan = new LoopPlan(assignment.Name, source, AddToTarget: false, FuelPerIteration: LoopFuel + 2);
            return true;
        }

        if (assignment.Value is BinaryExpression { Operator: "+" } add &&
            TryGetAccumulatingLength(add, assignment.Name, stackPlan, out source))
        {
            plan = new LoopPlan(assignment.Name, source, AddToTarget: true, FuelPerIteration: LoopFuel + 4);
            return true;
        }

        return false;
    }

    private static void EmitLoop(
        ForRangeStatement range,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        LoopPlan plan)
    {
        var index = il.DeclareLocal(typeof(int));
        var end = il.DeclareLocal(typeof(int));
        var iterations = il.DeclareLocal(typeof(int));
        var length = il.DeclareLocal(typeof(int));
        RawForRangeEmitter.EmitBound(range.Start, il, declare);
        il.Emit(OpCodes.Stloc, index);
        RawForRangeEmitter.EmitBound(range.End, il, declare);
        il.Emit(OpCodes.Stloc, end);

        var fallback = il.DefineLabel();
        var fastLoop = il.DefineLabel();
        var fallbackLoop = il.DefineLabel();
        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);

        il.Emit(OpCodes.Ldloc, declare(plan.Source).Local);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.StringLengthRaw)));
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "string.length");
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeBindingCalls)));
        il.Emit(OpCodes.Brfalse, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "string.length");
        il.Emit(OpCodes.Ldloc, iterations);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCalls)));
        if (plan.AddToTarget && RawForRangeEmitter.CanAccumulateInClosedForm(range, plan.Target))
        {
            // EXPERIMENT (exp/closed-form-accumulation): the hoisted length is loop-invariant, so the whole
            // accumulation loop "target += length" collapses to "target += length * iterations" computed in
            // O(1). AccumulateLinearI32 charges the identical per-iteration loop fuel and reproduces the
            // checked-overflow throw point exactly, so the only observable change is wall-time.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldloc, iterations);
            EmitInt32(il, plan.FuelPerIteration);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.AccumulateLinearI32)));
            il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
            RawForRangeEmitter.EmitStoreLastIndex(range.LocalName, end, il, declare);
            il.Emit(OpCodes.Br, finish);
        }
        else
        {
            EmitLoopBody(fastLoop, finish, index, end, length, range.LocalName, plan, il, declare, chargeBinding: false);
        }

        il.MarkLabel(fallback);
        EmitLoopBody(fallbackLoop, finish, index, end, length, range.LocalName, plan, il, declare, chargeBinding: true);
        il.MarkLabel(finish);
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        LocalBuilder length,
        string loopLocal,
        LoopPlan plan,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        bool chargeBinding)
    {
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(il, plan.FuelPerIteration);
        if (chargeBinding)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "string.length");
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
        }

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stloc, declare(loopLocal).Local);
        if (plan.AddToTarget)
        {
            il.Emit(OpCodes.Ldloc, declare(plan.Target).Local);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.AddI32Raw)));
        }
        else
        {
            il.Emit(OpCodes.Ldloc, length);
        }

        il.Emit(OpCodes.Stloc, declare(plan.Target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }

    private static bool TryGetAccumulatingLength(
        BinaryExpression expression,
        string target,
        LocalStackKindPlanner stackPlan,
        out string source)
    {
        source = "";
        return (IsTargetVariable(expression.Left, target) &&
                TryGetStringLengthCall(expression.Right, stackPlan, out source)) ||
               (IsTargetVariable(expression.Right, target) &&
                TryGetStringLengthCall(expression.Left, stackPlan, out source));
    }

    private static bool TryGetStringLengthCall(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        out string source)
    {
        source = "";
        if (expression is not CallExpression { Name: "string.length", Arguments.Count: 1 } call ||
            call.Arguments[0] is not VariableExpression variable ||
            stackPlan.LocalKind(variable.Name) != StackKind.Boxed)
        {
            return false;
        }

        source = variable.Name;
        return true;
    }

    private static bool IsTargetVariable(Expression expression, string target)
        => expression is VariableExpression variable &&
           string.Equals(variable.Name, target, StringComparison.Ordinal);

    private static bool CanUseDirectStringLength(IBindingCatalog bindings)
        => bindings.TryGet("string.length", out var binding) &&
           CompiledIntrinsicBindingMatcher.IsPureRuntimeStub(
               binding,
               nameof(Kernels.Runtime.CompiledRuntime.StringLength),
               SandboxType.I32,
               [SandboxType.String],
               requireUnboundedCost: true);

    private readonly record struct LoopPlan(
        string Target,
        string Source,
        bool AddToTarget,
        int FuelPerIteration);
}
