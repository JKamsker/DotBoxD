namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class PureBindingCallEmitter
{
    public static bool TryEmit(CallExpression call, ILGenerator il, Action<Expression> emitExpression)
    {
        switch (call.Name) {
            case "string.length":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.StringLength));
                return true;
            case "string.concatBudgeted":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ConcatString)));
                return true;
            case "math.abs":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.AbsI32));
                return true;
            case "math.min":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.MinI32));
                return true;
            case "math.max":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.MaxI32));
                return true;
            case "math.clamp":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.ClampI32));
                return true;
            case "math.sqrt":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.SqrtF64));
                return true;
            case "math.floor":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.FloorF64));
                return true;
            case "math.ceil":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.CeilF64));
                return true;
            case "math.round":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.RoundF64));
                return true;
            default:
                return false;
        }
    }

    private static void EmitCall(
        ILGenerator il,
        Action<Expression> emitExpression,
        CallExpression call,
        string runtimeMethod)
    {
        EmitArguments(call, emitExpression);
        il.Emit(OpCodes.Call, Runtime(runtimeMethod));
    }

    private static void EmitArguments(CallExpression call, Action<Expression> emitExpression)
    {
        foreach (var argument in call.Arguments) {
            emitExpression(argument);
        }
    }
}
