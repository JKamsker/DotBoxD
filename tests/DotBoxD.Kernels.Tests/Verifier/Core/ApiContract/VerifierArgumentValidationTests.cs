using System.Security.Cryptography;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core;

public sealed class VerifierArgumentValidationTests
{
    [Fact]
    public async Task Direct_verifier_rejects_null_manifest_at_public_boundary()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            _ = await new GeneratedAssemblyVerifier().VerifyAsync(
                ReadOnlyMemory<byte>.Empty,
                null!,
                policy,
                CancellationToken.None);
        });

        Assert.Equal("manifest", exception.ParamName);
    }

    [Fact]
    public async Task Direct_verifier_rejects_null_policy_at_public_boundary()
    {
        var manifest = CurrentManifest(ReadOnlyMemory<byte>.Empty);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            _ = await new GeneratedAssemblyVerifier().VerifyAsync(
                ReadOnlyMemory<byte>.Empty,
                manifest,
                null!,
                CancellationToken.None);
        });

        Assert.Equal("policy", exception.ParamName);
    }

    private static ArtifactManifest CurrentManifest(ReadOnlyMemory<byte> bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        var policy = VerificationPolicy.BoxedValueDefaults();
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
