using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

// Shape checks that read the textually previous instruction as a call operand must prove that
// instruction is the call's sole control-flow predecessor; a branch can otherwise jump over it.
public sealed class VerifierPredecessorShapeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verifier_rejects_type_scalar_name_that_a_branch_skips(bool conditional)
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var resultValue = il.DeclareLocal(typeof(SandboxValue));
            var allowedName = il.DefineLabel();
            var call = il.DefineLabel();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            if (conditional)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Brtrue_S, allowedName);
            }

            il.Emit(OpCodes.Ldstr, "System.String");
            il.Emit(OpCodes.Br_S, call);
            il.MarkLabel(allowedName);
            il.Emit(OpCodes.Ldstr, "I32");
            il.MarkLabel(call);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeScalar)));
            EmitChargeFuel(il);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeList)));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
            il.Emit(OpCodes.Stloc, resultValue);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldloc, resultValue);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("SandboxType scalars from literal names", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verifier_rejects_literal_charge_whose_operand_load_a_branch_skips()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var uncharged = il.DeclareLocal(typeof(SandboxValue));
            var charged = il.DeclareLocal(typeof(SandboxValue));
            var charge = il.DefineLabel();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            il.Emit(OpCodes.Ldstr, "uncharged");
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
            il.Emit(OpCodes.Stloc, uncharged);
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
            il.Emit(OpCodes.Stloc, charged);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, charged);
            il.Emit(OpCodes.Br_S, charge);
            il.Emit(OpCodes.Ldloc, uncharged);
            il.MarkLabel(charge);
            il.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(
                    nameof(CompiledRuntime.ChargeSandboxValue),
                    [typeof(SandboxContext), typeof(SandboxValue)])!);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldloc, uncharged);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal));
    }

    private static MethodBuilder DefineFunction(TypeBuilder type)
        => type.DefineMethod(
            "Fn_0",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void EmitExecuteCalling(TypeBuilder type, MethodInfo function)
    {
        var il = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]).GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ValidateEntrypointInput)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, function);
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
