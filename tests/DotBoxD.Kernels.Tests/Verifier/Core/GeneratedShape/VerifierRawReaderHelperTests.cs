using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.GeneratedShape;

public sealed class VerifierRawReaderHelperTests
{
    [Fact]
    public async Task Verifier_rejects_unmetered_raw_list_reader_allocation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(RawListReaderAllocationAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            d.Message.Contains(nameof(CompiledRuntime.ListI32ReaderRaw), StringComparison.Ordinal));
    }

    private static byte[] RawListReaderAllocationAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));

            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ListI32ReaderRaw))!);
            fnIl.Emit(OpCodes.Pop);
            fnIl.Emit(OpCodes.Ldc_I4_0);
            fnIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var executeIl = DefineExecute(type).GetILGenerator();
            executeIl.Emit(OpCodes.Ldarg_1);
            executeIl.Emit(OpCodes.Ldc_I4_1);
            executeIl.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            executeIl.Emit(OpCodes.Ldarg_0);
            executeIl.Emit(OpCodes.Ldarg_1);
            executeIl.Emit(OpCodes.Call, fn);
            executeIl.Emit(OpCodes.Ret);
        });

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
    }
}
