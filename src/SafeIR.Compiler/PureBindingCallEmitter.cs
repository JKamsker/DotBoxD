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
            case "list.of":
                il.Emit(OpCodes.Ldarg_0);
                EmitValueArray(call, il, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListOf)));
                return true;
            case "list.count":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.ListCount));
                return true;
            case "list.get":
                EmitCall(il, emitExpression, call, nameof(CompiledRuntime.ListGet));
                return true;
            case "list.add":
                il.Emit(OpCodes.Ldarg_0);
                EmitArguments(call, emitExpression);
                il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.ListAdd)));
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

    private static void EmitValueArray(CallExpression call, ILGenerator il, Action<Expression> emitExpression)
    {
        EmitInt32(il, call.Arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(SandboxValue));
        for (var i = 0; i < call.Arguments.Count; i++) {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            emitExpression(call.Arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
