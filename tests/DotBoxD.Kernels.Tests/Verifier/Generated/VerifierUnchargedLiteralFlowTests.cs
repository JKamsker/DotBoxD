using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

// An uncharged literal helper result stored in a local must be charged before any other read of
// that local can let it escape. A check that only looks at a `ldloc; ret` pair misses the shapes
// the compiler really emits (a copy into the return local, or a branch to a shared `ret`).
public sealed class VerifierUnchargedLiteralFlowTests
{
    [Fact]
    public async Task Verifier_rejects_uncharged_literal_copied_into_validated_return_local()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var literal = il.DeclareLocal(typeof(SandboxValue));
            var returned = il.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitStringLiteral(il, literal);
            il.Emit(OpCodes.Ldloc, literal);
            il.Emit(OpCodes.Stloc, returned);
            EmitValidatedReturn(il, returned);
            EmitExecuteCalling(type, fn);
        }));

        AssertRejectsStringLiteral(result);
    }

    [Fact]
    public async Task Verifier_rejects_uncharged_literal_returned_through_branch_to_shared_ret()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var literal = il.DeclareLocal(typeof(SandboxValue));
            var ret = il.DefineLabel();
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitStringLiteral(il, literal);
            EmitExitCall(il);
            il.Emit(OpCodes.Ldloc, literal);
            il.Emit(OpCodes.Br_S, ret);
            il.Emit(OpCodes.Ldnull);
            il.MarkLabel(ret);
            il.Emit(OpCodes.Ret);
            EmitExecuteCalling(type, fn);
        }));

        AssertRejectsStringLiteral(result);
    }

    [Fact]
    public async Task Verifier_rejects_uncharged_literal_copied_before_its_charge()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var literal = il.DeclareLocal(typeof(SandboxValue));
            var returned = il.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitStringLiteral(il, literal);
            il.Emit(OpCodes.Ldloc, literal);
            il.Emit(OpCodes.Stloc, returned);
            EmitChargeLocal(il, literal);
            EmitValidatedReturn(il, returned);
            EmitExecuteCalling(type, fn);
        }));

        AssertRejectsStringLiteral(result);
    }

    [Fact]
    public async Task Verifier_accepts_charged_literal_copied_into_validated_return_local()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = DefineFunction(type);
            var il = fn.GetILGenerator();
            var literal = il.DeclareLocal(typeof(SandboxValue));
            var returned = il.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(il);
            EmitChargeFuel(il);
            EmitStringLiteral(il, literal);
            EmitChargeLocal(il, literal);
            il.Emit(OpCodes.Ldloc, literal);
            il.Emit(OpCodes.Stloc, returned);
            EmitValidatedReturn(il, returned);
            EmitExecuteCalling(type, fn);
        }));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
    }

    private static void AssertRejectsStringLiteral(VerificationResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal));
    }

    private static void EmitStringLiteral(ILGenerator il, LocalBuilder literal)
    {
        il.Emit(OpCodes.Ldstr, "uncharged");
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
        il.Emit(OpCodes.Stloc, literal);
    }

    private static void EmitChargeLocal(ILGenerator il, LocalBuilder local)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(
            OpCodes.Call,
            typeof(CompiledRuntime).GetMethod(
                nameof(CompiledRuntime.ChargeSandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)])!);
    }

    // Mirrors CompiledReturnEmitter: ldloc value; <metered type>; RequireValueType; ExitCall; ret.
    private static void EmitValidatedReturn(ILGenerator il, LocalBuilder value)
    {
        il.Emit(OpCodes.Ldloc, value);
        EmitChargeFuel(il);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeScalar)));
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.RequireValueType)));
        EmitExitCall(il);
        il.Emit(OpCodes.Ret);
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
