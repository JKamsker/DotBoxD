using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

using System.Reflection.Emit;
using DotBoxD.Kernels;
using static DotBoxD.Kernels.Compiler.IlEmitterPrimitives;

internal static class F64MathIntrinsicEmitter
{
    private static readonly Dictionary<string, F64Intrinsic> Intrinsics = new(StringComparer.Ordinal)
    {
        ["math.sqrt"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.SqrtF64Raw),
            nameof(Kernels.Runtime.CompiledRuntime.SqrtF64)),
        ["math.floor"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.FloorF64Raw),
            nameof(Kernels.Runtime.CompiledRuntime.FloorF64)),
        ["math.ceil"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.CeilF64Raw),
            nameof(Kernels.Runtime.CompiledRuntime.CeilF64)),
        ["math.round"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.RoundF64Raw),
            nameof(Kernels.Runtime.CompiledRuntime.RoundF64)),
    };

    public static bool TryEmit(
        Expression expression,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call ||
            call.Arguments.Count != 1 ||
            !TryGetRawIntrinsic(call, bindings, out var rawMethod))
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
        il.Emit(OpCodes.Call, Runtime(rawMethod));
        return true;
    }

    private static bool TryGetRawIntrinsic(CallExpression call, IBindingCatalog bindings, out string rawMethod)
    {
        rawMethod = string.Empty;
        if (!Intrinsics.TryGetValue(call.Name, out var intrinsic))
        {
            return false;
        }

        rawMethod = intrinsic.RawMethod;
        return bindings.TryGet(call.Name, out var binding) &&
               CompiledIntrinsicBindingMatcher.IsPureRuntimeStub(
                   binding,
                   intrinsic.BoxedMethod,
                   SandboxType.F64,
                   [SandboxType.F64]);
    }

    private readonly record struct F64Intrinsic(string RawMethod, string BoxedMethod);
}

internal static class I32MathIntrinsicEmitter
{
    private static readonly Dictionary<string, I32Intrinsic> Intrinsics = new(StringComparer.Ordinal)
    {
        ["math.abs"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.AbsI32Raw),
            nameof(Kernels.Runtime.CompiledRuntime.AbsI32),
            1),
        ["math.min"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.MinI32Raw),
            nameof(Kernels.Runtime.CompiledRuntime.MinI32),
            2),
        ["math.max"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.MaxI32Raw),
            nameof(Kernels.Runtime.CompiledRuntime.MaxI32),
            2),
        ["math.clamp"] = new(
            nameof(Kernels.Runtime.CompiledRuntime.ClampI32Raw),
            nameof(Kernels.Runtime.CompiledRuntime.ClampI32),
            3),
    };

    public static bool TryEmit(
        Expression expression,
        IBindingCatalog bindings,
        ILGenerator il,
        Action<Expression, StackKind> emitAs)
    {
        if (expression is not CallExpression call ||
            !TryGetRawIntrinsic(call, bindings, out var rawMethod, out var argumentCount) ||
            call.Arguments.Count != argumentCount)
        {
            return false;
        }

        CompiledMeterEmitter.Fuel(il, 1);
        var locals = new LocalBuilder[argumentCount];
        for (var i = 0; i < argumentCount; i++)
        {
            locals[i] = il.DeclareLocal(typeof(int));
            emitAs(call.Arguments[i], StackKind.I32);
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, call.Name);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeBindingCall)));
        for (var i = 0; i < argumentCount; i++)
        {
            il.Emit(OpCodes.Ldloc, locals[i]);
        }

        il.Emit(OpCodes.Call, Runtime(rawMethod));
        return true;
    }

    private static bool TryGetRawIntrinsic(
        CallExpression call,
        IBindingCatalog bindings,
        out string rawMethod,
        out int argumentCount)
    {
        rawMethod = string.Empty;
        argumentCount = 0;
        if (!Intrinsics.TryGetValue(call.Name, out var intrinsic))
        {
            return false;
        }

        rawMethod = intrinsic.RawMethod;
        argumentCount = intrinsic.ArgumentCount;
        return bindings.TryGet(call.Name, out var binding) &&
               CompiledIntrinsicBindingMatcher.IsPureRuntimeStub(
                   binding,
                   intrinsic.BoxedMethod,
                   SandboxType.I32,
                   I32Parameters(argumentCount));
    }

    private static SandboxType[] I32Parameters(int count)
        => count switch
        {
            1 => [SandboxType.I32],
            2 => [SandboxType.I32, SandboxType.I32],
            3 => [SandboxType.I32, SandboxType.I32, SandboxType.I32],
            _ => []
        };

    private readonly record struct I32Intrinsic(string RawMethod, string BoxedMethod, int ArgumentCount);
}
