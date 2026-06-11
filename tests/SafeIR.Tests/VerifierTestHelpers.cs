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
}
