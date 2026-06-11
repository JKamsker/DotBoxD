using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class VerifierCompiledShapeTests
{
    [Fact]
    public async Task Verifier_rejects_execute_without_entrypoint_validation()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    [Fact]
    public async Task Verifier_rejects_generated_function_without_meters()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var fnIl = fn.GetILGenerator();
            fnIl.Emit(OpCodes.Ldarg_1);
            fnIl.Emit(OpCodes.Ret);

            var execute = DefineExecute(type);
            var il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-COMPILED-SHAPE");
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
}
