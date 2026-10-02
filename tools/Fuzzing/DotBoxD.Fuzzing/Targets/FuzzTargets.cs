using System.Security.Cryptography;
using System.Text;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Fuzzing.Targets;

internal static class FuzzTargets
{
    public static bool IsKnown(string target)
        => target is "json" or "verifier" or "framing" or "messagepack-request" or "messagepack-response";

    public static void Run(string target, byte[] bytes)
    {
        switch (target)
        {
            case "json":
                Json(bytes);
                break;
            case "verifier":
                Verify(bytes);
                break;
            case "framing":
                ProtocolTargets.Frame(bytes);
                break;
            case "messagepack-request":
                ProtocolTargets.Request(bytes);
                break;
            case "messagepack-response":
                ProtocolTargets.Response(bytes);
                break;
            default:
                throw new ArgumentException("Unknown fuzz target.", nameof(target));
        }
    }

    private static void Json(byte[] bytes)
    {
        SandboxModule module;
        try
        {
            module = JsonImporter.Import(Encoding.UTF8.GetString(bytes));
        }
        catch (SandboxValidationException exception)
        {
            if (exception.Diagnostics.Count == 0)
            {
                throw new InvalidOperationException("JSON rejection has no diagnostics.", exception);
            }

            return;
        }

        var exported = JsonExporter.Export(module);
        var reparsed = JsonImporter.Import(exported);
        if (CanonicalModuleHasher.Hash(module) != CanonicalModuleHasher.Hash(reparsed)
            || exported != JsonExporter.Export(reparsed))
        {
            throw new InvalidOperationException("JSON export changed the module's canonical identity.");
        }
    }

    private static void Verify(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var manifest = new ArtifactManifest(
            1, "fuzz", "module", "plan", "policy", "bindings", "runtime", "compiler",
            "types", "effects", "verifier", "1.0.0", "net10.0", [], hash, DateTimeOffset.UnixEpoch);
        var result = new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        if (!result.Succeeded && result.Diagnostics.Count == 0)
        {
            throw new InvalidOperationException("Verifier rejection has no diagnostics.");
        }
    }
}
