using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Verifier;

namespace SafeIR.Tests;

internal static class VerifierTestHelpers
{
    public static async ValueTask<VerificationResult> VerifyAsync(byte[] bytes)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var manifest = new ArtifactManifest(
            1,
            "test",
            "module",
            "plan",
            "policy",
            "bindings",
            "runtime",
            "compiler",
            "verifier",
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);

        return await new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None);
    }

    public static byte[] BuildGeneratedAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Generated" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GeneratedModule");
        var type = module.DefineType(
            "SafeIR.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    public static MethodBuilder DefineValidExecute(TypeBuilder type)
    {
        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
