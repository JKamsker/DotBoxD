using System.Reflection.Emit;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Returns;

using static IlEmitterPrimitives;

internal static class CompiledReturnEmitter
{
    public static void Emit(
        ILGenerator il,
        SandboxType returnType,
        bool recordValidation)
    {
        var value = il.DeclareLocal(typeof(SandboxValue));
        il.Emit(OpCodes.Stloc, value);
        if (recordValidation)
        {
            il.Emit(OpCodes.Ldarg_0);
        }

        il.Emit(OpCodes.Ldloc, value);
        CompiledTypeEmitter.EmitMetered(il, returnType);
        var validator = recordValidation
            ? nameof(Kernels.Runtime.CompiledRuntime.RequireValueTypeAndRecordValidation)
            : nameof(Kernels.Runtime.CompiledRuntime.RequireValueType);
        il.Emit(OpCodes.Call, Runtime(validator));
        // Keep the validated value on the stack. For the proof-producing helper this makes publication
        // mechanically terminal: the verifier requires only ExitCall and ret to follow.
        CompiledMeterEmitter.ExitCall(il);
        il.Emit(OpCodes.Ret);
    }
}
