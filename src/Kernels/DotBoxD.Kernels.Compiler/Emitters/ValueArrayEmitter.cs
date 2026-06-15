namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class ValueArrayEmitter
{
    public static void Emit(
        ILGenerator il,
        IReadOnlyList<Expression> arguments,
        Action<Expression> emitExpression)
    {
        // Evaluate every argument before allocating/charging the array. An argument may be a
        // side-effecting binding call, and the interpreter evaluates all argument expressions before
        // charging. Allocating first would let a tight fuel/allocation budget throw QuotaExceeded
        // before a side-effecting argument runs, diverging from the interpreter. Materialize the
        // arguments into locals first, then create the array and store them — preserving evaluation
        // order while keeping the allocation charge after the arguments are computed.
        var locals = new LocalBuilder[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            locals[i] = il.DeclareLocal(typeof(SandboxValue));
            emitExpression(arguments[i]);
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        il.Emit(OpCodes.Ldarg_0);
        EmitInt32(il, arguments.Count);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CreateValueArray)));
        for (var i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            EmitInt32(il, i);
            il.Emit(OpCodes.Ldloc, locals[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }
}
