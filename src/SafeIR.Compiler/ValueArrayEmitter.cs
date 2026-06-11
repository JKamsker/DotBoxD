namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using static IlEmitterPrimitives;

internal static class ValueArrayEmitter
{
    public static void Emit(
        ILGenerator il,
        IReadOnlyList<Expression> arguments,
        Action<Expression> emitExpression)
    {
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(SandboxValue));
        for (var i = 0; i < arguments.Count; i++) {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            emitExpression(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
