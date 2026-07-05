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
            ArtifactVersion: 1,
            CacheKey: "cache",
            ModuleHash: "module",
            PlanHash: "plan",
            PolicyHash: "policy",
            BindingManifestHash: "bindings",
            RuntimeFacadeHash: policy.RuntimeFacadeHash,
            CompilerVersion: "compiler",
            TypeSystemVersion: "type-system",
            EffectAnalysisVersion: "effect-analysis",
            VerifierVersion: policy.VerifierVersion,
            LanguageVersion: "1.0.0",
            TargetFramework: "net10.0",
            OptimizationFlags: ["boxed-values"],
            AssemblyHash: hash,
            CreatedAt: DateTimeOffset.UtcNow);
    }
}
