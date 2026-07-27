using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

internal static class CompiledArtifactTestFactory
{
    public static CompiledArtifact LoadedAssembly(
        ExecutionPlan plan,
        byte[] assemblyBytes,
        SandboxCompiledEntrypoint? suppliedEntrypoint = null)
    {
        var artifactHash = Convert.ToHexString(SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        return new CompiledArtifact(
            assemblyBytes,
            artifactHash,
            CurrentManifest(plan, artifactHash),
            SuccessfulVerification(artifactHash),
            suppliedEntrypoint ?? ((_, _) => SandboxValue.Unit),
            CompiledRuntimeFormKind.LoadedAssembly);
    }

    public static CompiledArtifact DynamicMethod(
        ExecutionPlan plan,
        SandboxCompiledEntrypoint suppliedEntrypoint,
        string artifactHash = "dynamic-artifact")
        => new(
            [],
            artifactHash,
            CurrentManifest(plan, artifactHash),
            SuccessfulVerification(artifactHash),
            suppliedEntrypoint,
            CompiledRuntimeFormKind.DynamicMethod);

    public static ArtifactManifest CurrentManifest(ExecutionPlan plan, string artifactHash)
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        return new ArtifactManifest(
            1,
            CacheKeyBuilder.Build(plan, "main", policy, optimize: false),
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            CacheKeyBuilder.RuntimeFacadeHash,
            CacheKeyBuilder.CompilerVersion,
            CacheKeyBuilder.TypeSystemVersion,
            CacheKeyBuilder.EffectAnalysisVersion,
            policy.VerifierVersion,
            CacheKeyBuilder.LanguageVersion,
            CacheKeyBuilder.TargetFramework,
            ["boxed-values"],
            artifactHash,
            DateTimeOffset.UtcNow);
    }

    public static VerificationResult SuccessfulVerification(string artifactHash)
        => new(true, [], artifactHash, VerificationPolicy.BoxedValueDefaults().VerifierVersion, DateTimeOffset.UtcNow);

    public static byte[] BuildI32Assembly(int parameterCount, int value)
        => BuildExecuteAssembly(parameterCount, il =>
        {
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.I32)));
        });

    public static byte[] BuildBoolAssembly(int parameterCount, bool value)
        => BuildExecuteAssembly(parameterCount, il =>
        {
            il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.Bool)));
        });

    public static byte[] BuildMalformedI32ListAssembly(int parameterCount)
        => BuildExecuteAssembly(parameterCount, il =>
        {
            var values = il.DeclareLocal(typeof(SandboxValue[]));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray)));
            il.Emit(OpCodes.Stloc, values);

            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.I32)));
            il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldstr, "wrong");
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.StringLiteralValue)));
            il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ListOf)));
        });

    public static byte[] BuildForgedI32ListReturnValidationProofAssembly(int parameterCount)
        => BuildExecuteAssembly(parameterCount, il =>
        {
            var values = il.DeclareLocal(typeof(SandboxValue[]));
            var list = il.DeclareLocal(typeof(SandboxValue));

            EmitFuel(il, amount: 8);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray)));
            il.Emit(OpCodes.Stloc, values);

            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.I32)));
            il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.I32)));
            il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ListOf)));
            il.Emit(OpCodes.Stloc, list);

            EmitFuel(il, amount: 8);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, list);
            il.Emit(OpCodes.Ldstr, "I32");
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.TypeScalar)));
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.TypeList)));
            il.Emit(
                OpCodes.Call,
                RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.RequireValueTypeAndRecordValidation)));
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldstr, "wrong");
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.StringLiteralValue)));
            il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Ldloc, list);
        });

    public static byte[] BuildBindingCallAssembly(int parameterCount, string bindingId)
        => BuildExecuteAssembly(parameterCount, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, bindingId);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray)));
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));
        });

    private static byte[] BuildExecuteAssembly(int parameterCount, Action<ILGenerator> emitBody)
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var parameterTypes = new Type[parameterCount + 1];
            parameterTypes[0] = typeof(SandboxContext);
            Array.Fill(parameterTypes, typeof(SandboxValue), 1, parameterCount);
            var function = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                parameterTypes);
            var fnIl = function.GetILGenerator();
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.EnterCall)));
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Ldc_I4_1);
            fnIl.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel)));
            emitBody(fnIl);
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            fnIl.Emit(OpCodes.Stloc, value);
            fnIl.Emit(OpCodes.Ldarg_0);
            fnIl.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ExitCall)));
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, parameterCount);
            il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ValidateEntrypointInput)));
            il.Emit(OpCodes.Ldarg_0);
            for (var i = 0; i < parameterCount; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4, parameterCount);
                il.Emit(OpCodes.Ldstr, "I32");
                il.Emit(OpCodes.Call, RuntimeMethod(nameof(CompiledRuntime.TypeScalar)));
                il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.GetInputArgument)));
            }

            il.Emit(OpCodes.Call, function);
            il.Emit(OpCodes.Ret);
        });

    private static void EmitFuel(ILGenerator il, int amount)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, amount);
        il.Emit(OpCodes.Call, RuntimeMethod(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel)));
    }

    private static MethodInfo RuntimeMethod(string name)
        => typeof(CompiledRuntime).GetMethod(name) ?? throw new MissingMethodException(nameof(CompiledRuntime), name);
}
