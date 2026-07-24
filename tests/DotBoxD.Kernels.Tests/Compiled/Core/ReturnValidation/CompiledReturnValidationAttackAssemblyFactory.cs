using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ReturnValidation;

internal static class CompiledReturnValidationAttackAssemblyFactory
{
    public static byte[] BuildNestedPublisherAssembly()
        => BuildNestedPublisherAssembly(restoreInlineDepth: true);

    public static byte[] BuildUnbalancedInlineDepthAssembly()
        => BuildNestedPublisherAssembly(restoreInlineDepth: false);

    public static byte[] BuildDuplicateEntrypointNameAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            VerifierTestHelpers.DefineValidExecute(type);
            var duplicate = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = duplicate.GetILGenerator();
            var result = il.DeclareLocal(typeof(SandboxValue));
            EmitPrologue(il);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
            il.Emit(OpCodes.Stloc, result);
            EmitRawReturn(il, result);
        });

    private static byte[] BuildNestedPublisherAssembly(bool restoreInlineDepth)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var publisher = DefinePublisher(type);
            var entrypoint = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            EmitNestedPublisherAttack(entrypoint.GetILGenerator(), publisher, restoreInlineDepth);
            EmitExecute(type, entrypoint, parameterTypes: []);
        });

    public static byte[] BuildRecursivePublisherAssembly()
        => BuildRecursivePublisherAssembly(branchIntoUnreachableSuffix: false);

    public static byte[] BuildBranchIntoUnreachablePublicationSuffixAssembly()
        => BuildRecursivePublisherAssembly(branchIntoUnreachableSuffix: true);

    private static byte[] BuildRecursivePublisherAssembly(bool branchIntoUnreachableSuffix)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var entrypoint = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue), typeof(SandboxValue)]);
            EmitRecursivePublisherAttack(
                entrypoint.GetILGenerator(),
                entrypoint,
                branchIntoUnreachableSuffix);
            EmitExecute(type, entrypoint, [SandboxType.Bool, SandboxType.List(SandboxType.I32)]);
        });

    private static MethodBuilder DefinePublisher(TypeBuilder type)
    {
        var method = type.DefineMethod(
            "Fn_1",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        EmitPrologue(il);
        EmitValidatedReturn(il, argumentIndex: 1);
        return method;
    }

    private static void EmitNestedPublisherAttack(
        ILGenerator il,
        MethodInfo publisher,
        bool restoreInlineDepth)
    {
        var (values, list) = DeclareAttackLocals(il);
        EmitPrologue(il);
        EmitOwnedList(il, values, list);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitInlineCall)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Call, publisher);
        il.Emit(OpCodes.Pop);
        if (restoreInlineDepth)
        {
            EmitEnterInlineCall(il);
        }

        EmitMalformedMutation(il, values);
        EmitRawReturn(il, list);
    }

    private static void EmitRecursivePublisherAttack(
        ILGenerator il,
        MethodInfo entrypoint,
        bool branchIntoUnreachableSuffix)
    {
        var (values, list) = DeclareAttackLocals(il);
        var publish = il.DefineLabel();
        EmitPrologue(il);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.AsBool)));
        il.Emit(OpCodes.Brtrue, publish);

        EmitOwnedList(il, values, list);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitInlineCall)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.Bool)));
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Call, entrypoint);
        il.Emit(OpCodes.Pop);
        EmitEnterInlineCall(il);
        EmitMalformedMutation(il, values);
        if (branchIntoUnreachableSuffix)
        {
            EmitBranchIntoUnreachablePublicationSuffix(il, list);
        }
        else
        {
            EmitRawReturn(il, list);
        }

        il.MarkLabel(publish);
        EmitValidatedReturn(il, argumentIndex: 2);
    }

    private static void EmitBranchIntoUnreachablePublicationSuffix(
        ILGenerator il,
        LocalBuilder list)
    {
        var bypassPublication = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Br, bypassPublication);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, list);
        EmitI32ListType(il);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.RequireValueTypeAndRecordValidation)));
        il.MarkLabel(bypassPublication);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitCall)));
        il.Emit(OpCodes.Ret);
    }

    private static (LocalBuilder Values, LocalBuilder List) DeclareAttackLocals(ILGenerator il)
        => (il.DeclareLocal(typeof(SandboxValue[])), il.DeclareLocal(typeof(SandboxValue)));

    private static void EmitOwnedList(ILGenerator il, LocalBuilder values, LocalBuilder list)
    {
        EmitFuel(il, amount: 8);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.CreateValueArray)));
        il.Emit(OpCodes.Stloc, values);
        EmitI32Element(il, values, index: 0, value: 1);
        EmitI32Element(il, values, index: 1, value: 2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ListOf)));
        il.Emit(OpCodes.Stloc, list);
    }

    private static void EmitI32Element(ILGenerator il, LocalBuilder values, int index, int value)
    {
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldc_I4, value);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.I32)));
        il.Emit(OpCodes.Stelem_Ref);
    }

    private static void EmitMalformedMutation(ILGenerator il, LocalBuilder values)
    {
        EmitFuel(il, amount: 8);
        il.Emit(OpCodes.Ldloc, values);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldstr, "wrong");
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.StringLiteralValue)));
        il.Emit(OpCodes.Stelem_Ref);
    }

    private static void EmitValidatedReturn(ILGenerator il, int argumentIndex)
    {
        EmitFuel(il, amount: 8);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, argumentIndex);
        EmitI32ListType(il);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.RequireValueTypeAndRecordValidation)));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitCall)));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRawReturn(ILGenerator il, LocalBuilder value)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ExitCall)));
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitExecute(
        TypeBuilder type,
        MethodInfo entrypoint,
        IReadOnlyList<SandboxType> parameterTypes)
    {
        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, parameterTypes.Count);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ValidateEntrypointInput)));
        il.Emit(OpCodes.Ldarg_0);
        for (var i = 0; i < parameterTypes.Count; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldc_I4, parameterTypes.Count);
            EmitType(il, parameterTypes[i]);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.GetInputArgument)));
        }

        il.Emit(OpCodes.Call, entrypoint);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitType(ILGenerator il, SandboxType type)
    {
        if (type.Name == "List")
        {
            EmitType(il, type.Arguments[0]);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeList)));
            return;
        }

        il.Emit(OpCodes.Ldstr, type.Name);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeScalar)));
    }

    private static void EmitI32ListType(ILGenerator il)
        => EmitType(il, SandboxType.List(SandboxType.I32));

    private static void EmitPrologue(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.EnterCall)));
        EmitFuel(il, amount: 1);
    }

    private static void EmitEnterInlineCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.EnterInlineCall)));
    }

    private static void EmitFuel(ILGenerator il, int amount)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, amount);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.ChargeFuel)));
    }

    private static MethodInfo RuntimeMethod(string name)
        => typeof(CompiledRuntime).GetMethod(name) ?? throw new MissingMethodException(nameof(CompiledRuntime), name);
}
