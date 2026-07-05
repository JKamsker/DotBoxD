using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

public sealed class VerifierModuloAccumulatorMeterCreditTests
{
    [Fact]
    public async Task Verifier_rejects_zero_iteration_modulo_branch_meter_credit()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitZeroIterationModuloBranchDeltas(il);
            EmitUnmeteredStringLengthWork(il);
            EmitI32Return(il, 0);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains(nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw), StringComparison.Ordinal) &&
            d.Message.Contains("positive", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("iterations", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verifier_rejects_empty_range_modulo_index_meter_credit()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitEmptyRangeModuloIndexAccumulator(il);
            EmitUnmeteredStringLengthWork(il);
            EmitI32Return(il, 0);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains(nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw), StringComparison.Ordinal) &&
            d.Message.Contains("positive", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("iterations", StringComparison.OrdinalIgnoreCase));
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static MethodBuilder DefineFunction(TypeBuilder type)
        => type.DefineMethod(
            "Fn_0",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = DefineExecute(type).GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ValidateEntrypointInput)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitZeroIterationModuloBranchDeltas(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw)));
        il.Emit(OpCodes.Pop);
    }

    private static void EmitEmptyRangeModuloIndexAccumulator(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw)));
        il.Emit(OpCodes.Pop);
    }

    private static void EmitUnmeteredStringLengthWork(ILGenerator il)
    {
        for (var i = 0; i < 6; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.StringLengthRaw)));
            il.Emit(OpCodes.Pop);
        }
    }

    private static void EmitI32Return(ILGenerator il, int value)
    {
        var result = il.DeclareLocal(typeof(SandboxValue));
        il.Emit(OpCodes.Ldc_I4, value);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
        il.Emit(OpCodes.Stloc, result);
        EmitExitCall(il);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.EnterCall)));
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ChargeFuel)));
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitCall)));
    }

    private static MethodInfo RuntimeMethod(string name)
        => typeof(CompiledRuntime).GetMethod(name) ?? throw new MissingMethodException(nameof(CompiledRuntime), name);
}
