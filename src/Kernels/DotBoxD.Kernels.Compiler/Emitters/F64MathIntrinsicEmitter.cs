using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Runtime;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class F64MathIntrinsicEmitter
{
    public static bool TryEmit(
        Expression expression,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call ||
            call is not { Name: "math.sqrt", Arguments.Count: 1 } ||
            !CanEmitRawSqrt(call, bindings))
        {
            return false;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        var operand = il.DeclareLocal(typeof(double));
        emitAs(call.Arguments[0], StackKind.F64);
        il.Emit(OpCodes.Stloc, operand);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
        il.Emit(OpCodes.Ldloc, operand);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.SqrtF64Raw)));
        return true;
    }

    private static bool CanEmitRawSqrt(CallExpression call, IBindingCatalog bindings)
        => bindings.TryGet(call.Name, out var binding) &&
           binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(Runtime.CompiledRuntime).FullName &&
           binding.Compiled.Method == nameof(Kernels.Runtime.CompiledRuntime.SqrtF64) &&
           binding.Parameters.Count == 1 &&
           binding.Parameters[0].Equals(SandboxType.F64) &&
           binding.ReturnType.Equals(SandboxType.F64) &&
           binding.RequiredCapability is null &&
           binding.Safety == BindingSafety.PureIntrinsic &&
           binding.AuditLevel == AuditLevel.None &&
           (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
}
