namespace SafeIR.Compiler;

using System.Reflection.Emit;
using SafeIR;
using SafeIR.Runtime;
using static IlEmitterPrimitives;

internal static class BindingCallEmitter
{
    public static bool TryEmit(
        CallExpression call,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression> emitExpression)
    {
        if (!bindings.TryGet(call.Name, out var binding) || !IsCallBindingStub(binding)) {
            return false;
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        ValueArrayEmitter.Emit(il, call.Arguments, emitExpression);
        il.Emit(OpCodes.Call, Runtime(nameof(CompiledRuntime.CallBinding)));
        return true;
    }

    private static bool IsCallBindingStub(BindingSignature binding)
        => binding.Compiled.Kind == "RuntimeStub" &&
           binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
           binding.Compiled.Method == nameof(CompiledRuntime.CallBinding);
}
