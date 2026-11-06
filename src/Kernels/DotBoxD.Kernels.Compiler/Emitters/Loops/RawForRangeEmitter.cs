using System.Reflection.Emit;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

using static Compiler.IlEmitterPrimitives;

/// <summary>
/// Shared forRange plumbing for the raw single-assignment loop fast paths (literal or I32-local bounds).
/// </summary>
internal static class RawForRangeEmitter
{
    // The interpreter evaluates both bounds through I32ExpressionEvaluator (one fuel unit per literal or
    // variable bound) before it decides on any loop fast path, so the compiled fast paths charge the same
    // unit per bound whether or not the range turns out to be empty.
    private const int BoundFuel = 1;

    public static bool CanEmitBound(Expression expression, LocalStackKindPlanner stackPlan)
        => expression is LiteralExpression { Value: I32Value } ||
           expression is VariableExpression variable && stackPlan.LocalKind(variable.Name) == StackKind.I32;

    public static void EmitBound(
        Expression expression,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        CompiledMeterEmitter.Fuel(il, BoundFuel);
        switch (expression)
        {
            case LiteralExpression { Value: I32Value value }:
                EmitInt32(il, value.Value);
                break;
            case VariableExpression variable:
                il.Emit(OpCodes.Ldloc, declare(variable.Name).Local);
                break;
            default:
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.ValidationError,
                    "unsupported forRange bound"));
        }
    }

    /// <summary>
    /// A closed-form accumulation skips the per-iteration loop-variable writes, so it is only sound when the
    /// target is not the loop variable (the interpreter re-reads the freshly written index every iteration).
    /// </summary>
    public static bool CanAccumulateInClosedForm(ForRangeStatement range, string target)
        => !string.Equals(target, range.LocalName, StringComparison.Ordinal);

    /// <summary>
    /// After a closed-form non-empty range, leaves the loop variable at <c>end - 1</c> like the interpreter.
    /// </summary>
    public static void EmitStoreLastIndex(
        string loopLocal,
        LocalBuilder end,
        ILGenerator il,
        Func<string, (LocalBuilder Local, StackKind Kind)> declare)
    {
        il.Emit(OpCodes.Ldloc, end);
        EmitInt32(il, 1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, declare(loopLocal).Local);
    }
}
