using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierTests
{
    [Fact]
    public async Task Verifier_rejects_direct_system_io_reference()
    {
        var bytes = BuildMaliciousFileReadAssembly();
        var verifier = new GeneratedAssemblyVerifier();
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

        var result = await verifier.VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code is "V-TYPE-FORBIDDEN" or "V-MEMBER" or "V-ASM-REF");
    }

    private static byte[] BuildMaliciousFileReadAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("Malicious"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MaliciousModule");
        var type = module.DefineType("Malicious.Module", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(string), []);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "secret.txt");
        il.Emit(OpCodes.Call, typeof(File).GetMethod(nameof(File.ReadAllText), [typeof(string)])!);
        il.Emit(OpCodes.Ret);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
