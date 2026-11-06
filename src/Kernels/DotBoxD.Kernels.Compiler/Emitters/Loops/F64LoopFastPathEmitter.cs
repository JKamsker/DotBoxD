using System.Reflection.Emit;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

internal static class F64LoopFastPathEmitter
{
    private const int LoopFuel = 5;

    public static bool TryEmit(
        ForRangeStatement range,
        ILGenerator il,
        LocalStackKindPlanner stackPlan,
        IBindingCatalog bindings,
        IReadOnlySet<string> nonNegativeF64Locals,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        out string? nonNegativeTarget)
    {
        nonNegativeTarget = null;
        if (stackPlan.LocalKind(range.LocalName) != StackKind.I32 ||
            !RawForRangeEmitter.CanEmitBound(range.Start, stackPlan) ||
            !RawForRangeEmitter.CanEmitBound(range.End, stackPlan) ||
            range.Body.Count != 1 ||
            range.Body[0] is not AssignmentStatement assignment ||
            stackPlan.LocalKind(assignment.Name) != StackKind.F64 ||
            !RawF64ExpressionPlan.TryCreate(assignment.Value, assignment.Name, stackPlan, bindings, nonNegativeF64Locals, out var expression))
        {
            return false;
        }

        nonNegativeTarget = expression.PreservesNonNegative ? assignment.Name : null;
        EmitLoop(range, il, declare, assignment.Name, expression);
        return true;
    }

    private static void EmitLoop(
        ForRangeStatement range,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        string target,
        RawF64ExpressionPlan expression)
    {
        var index = il.DeclareLocal(typeof(int));
        var end = il.DeclareLocal(typeof(int));
        var iterations = il.DeclareLocal(typeof(int));
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
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iterations);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, expression.BindingId);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, expression.BindingCallCount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CanBulkChargeBindingCallsScaled)));
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, expression.BindingId);
        il.Emit(OpCodes.Ldloc, iterations);
        EmitInt32(il, expression.BindingCallCount);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ChargeBindingCallsScaled)));
        EmitLoopBody(fastLoop, finish, index, end, range.LocalName, target, expression, il, declare, chargeBinding: false);

        il.MarkLabel(fallback);
        EmitLoopBody(fallbackLoop, finish, index, end, range.LocalName, target, expression, il, declare, chargeBinding: true);
        il.MarkLabel(finish);
    }

    private static void EmitLoopBody(
        Label loop,
        Label finish,
        LocalBuilder index,
        LocalBuilder end,
        string loopLocal,
        string target,
        RawF64ExpressionPlan expression,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare,
        bool chargeBinding)
    {
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, finish);
        CompiledMeterEmitter.LoopIteration(il, LoopFuel + 1 + expression.FuelCost);
        if (chargeBinding)
        {
            for (var i = 0; i < expression.BindingCallCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, expression.BindingId);
                il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
            }
        }

        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stloc, declare(loopLocal).Local);
        expression.Emit(il, declare);
        il.Emit(OpCodes.Stloc, declare(target).Local);
        il.Emit(OpCodes.Ldloc, index);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
    }
}
