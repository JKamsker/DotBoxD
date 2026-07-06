using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Compiler.Internal;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Verifier;

internal static class PersistentCompiledArtifactCacheValidator
{
    public static void ValidateCacheKey(string cacheKey)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        if (cacheKey.Length != 64 || !cacheKey.All(Uri.IsHexDigit))
        {
            throw CacheInvalid("cache key is not path safe");
        }
    }

    public static void ValidateManifest(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationPolicy policy)
    {
        ValidateManifestIdentity(cacheKey, plan, manifest, policy);
        if (manifest.OptimizationFlags is null)
        {
            throw CacheInvalid("cached artifact optimization flags are missing");
        }

        var expectedFlags = ExpectedOptimizationFlags(cacheKey, plan, entrypoint, policy);
        if (!manifest.OptimizationFlags.SequenceEqual(expectedFlags, StringComparer.Ordinal))
        {
            throw CacheInvalid("cached artifact optimization flags do not match cache key");
        }
    }

    public static void ValidateVerification(
        ArtifactManifest manifest,
        VerificationResult verification,
        VerificationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(verification.AssemblyHash) ||
            string.IsNullOrWhiteSpace(verification.VerifierVersion))
        {
            throw CacheInvalid("cached artifact verification metadata is incomplete");
        }

        if (!verification.Succeeded ||
            verification.VerifierVersion != policy.VerifierVersion ||
            verification.AssemblyHash != manifest.AssemblyHash)
        {
            throw CacheInvalid("cached artifact verification does not match current verifier");
        }
    }

    private static void ValidateManifestIdentity(
        string cacheKey,
        ExecutionPlan plan,
        ArtifactManifest manifest,
        VerificationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(manifest.AssemblyHash))
        {
            throw CacheInvalid("cached artifact assembly hash is missing");
        }

        if (!ManifestIdentityMatches(cacheKey, plan, manifest, policy))
        {
            throw CacheInvalid("cached artifact manifest does not match current plan");
        }
    }

    private static bool ManifestIdentityMatches(
        string cacheKey,
        ExecutionPlan plan,
        ArtifactManifest manifest,
        VerificationPolicy policy)
    {
        if (manifest.ArtifactVersion != 1)
        {
            return false;
        }

        foreach (var field in ManifestIdentityFields(cacheKey, plan, manifest, policy))
        {
            if (!string.Equals(field.Actual, field.Expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static ManifestIdentityField[] ManifestIdentityFields(
        string cacheKey,
        ExecutionPlan plan,
        ArtifactManifest manifest,
        VerificationPolicy policy) =>
        [
            new(manifest.CacheKey, cacheKey),
            new(manifest.ModuleHash, plan.ModuleHash),
            new(manifest.PlanHash, plan.PlanHash),
            new(manifest.PolicyHash, plan.PolicyHash),
            new(manifest.BindingManifestHash, plan.BindingManifestHash),
            new(manifest.CompilerVersion, CacheKeyBuilder.CompilerVersion),
            new(manifest.TypeSystemVersion, CacheKeyBuilder.TypeSystemVersion),
            new(manifest.EffectAnalysisVersion, CacheKeyBuilder.EffectAnalysisVersion),
            new(manifest.VerifierVersion, policy.VerifierVersion),
            new(manifest.RuntimeFacadeHash, policy.RuntimeFacadeHash),
            new(manifest.LanguageVersion, CacheKeyBuilder.LanguageVersion),
            new(manifest.TargetFramework, CacheKeyBuilder.TargetFramework)
        ];

    private static string[] ExpectedOptimizationFlags(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy)
    {
        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: false))
        {
            return ["boxed-values"];
        }

        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: true))
        {
            return ["opt"];
        }

        throw CacheInvalid("cache key does not match current compile options");
    }

    private static SandboxRuntimeException CacheInvalid(string message)
        => new(new SandboxError(SandboxErrorCode.CacheInvalid, message));

    private readonly record struct ManifestIdentityField(string? Actual, string? Expected);
}
