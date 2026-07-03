using System.Security.Cryptography;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core;

public sealed class VerifierCancellationTests
{
    [Fact]
    public async Task Direct_verifier_observes_pre_canceled_token_before_malformed_bytes()
    {
        byte[] bytes = [0x4d, 0x5a, 0x00, 0x00, 0x50, 0x45];
        var policy = VerificationPolicy.BoxedValueDefaults();
        var manifest = CurrentManifest(bytes, policy);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await new GeneratedAssemblyVerifier().VerifyAsync(
                bytes,
                manifest,
                policy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(manifest)),
                cts.Token);
        });
    }

    private static ArtifactManifest CurrentManifest(byte[] bytes, VerificationPolicy policy)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(
            1,
            "cache",
            "module",
            "plan",
            "policy",
            "bindings",
            policy.RuntimeFacadeHash,
            "compiler",
            "type-system",
            "effect-analysis",
            policy.VerifierVersion,
            "1.0.0",
            "net10.0",
            ["boxed-values"],
            hash,
            DateTimeOffset.UtcNow);
    }
}
