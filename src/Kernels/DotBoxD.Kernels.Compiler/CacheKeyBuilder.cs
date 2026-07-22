using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Compiler;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Verifier;

public static class CacheKeyBuilder
{
    public const string CompilerVersion = "dotboxd-compiler-13";
    public const string TypeSystemVersion = "dotboxd-type-system-2";
    public const string EffectAnalysisVersion = "dotboxd-effect-analysis-3";
    public const string CanonicalizerVersion = CanonicalModuleHasher.CanonicalizerVersion;
    public const string TargetFramework = "net10.0";

    public static string LanguageVersion => SandboxLanguage.CurrentVersionText;

    public static string RuntimeFacadeHash => VerificationPolicy.BoxedValueDefaults().RuntimeFacadeHash;

    public static string Build(ExecutionPlan plan, string entrypoint, VerificationPolicy policy, bool optimize)
    {
        ValidateInputs(plan, entrypoint, policy);

        return BuildCore(plan, entrypoint, policy, optimize);
    }

    public static VerificationManifestIdentity BuildManifestIdentity(
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy,
        bool optimize)
    {
        ValidateInputs(plan, entrypoint, policy);

        return new(
            1,
            BuildCore(plan, entrypoint, policy, optimize),
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            policy.RuntimeFacadeHash,
            CompilerVersion,
            TypeSystemVersion,
            EffectAnalysisVersion,
            policy.VerifierVersion,
            LanguageVersion,
            TargetFramework,
            [optimize ? "opt" : "boxed-values"]);
    }

    private static void ValidateInputs(ExecutionPlan plan, string entrypoint, VerificationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entrypoint);
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(entrypoint))
        {
            throw new ArgumentException("Entrypoint must not be blank.", nameof(entrypoint));
        }
    }

    private static string BuildCore(ExecutionPlan plan, string entrypoint, VerificationPolicy policy, bool optimize)
        => HashParts(
            "dotboxd-cache-v2",
            plan.ModuleHash,
            CanonicalizerVersion,
            entrypoint,
            LanguageVersion,
            CompilerVersion,
            TypeSystemVersion,
            EffectAnalysisVersion,
            policy.VerifierVersion,
            policy.AllowlistHash,
            policy.RuntimeFacadeHash,
            plan.BindingManifestHash,
            plan.PolicyHash,
            TargetFramework,
            optimize ? "opt" : "boxed-values",
            plan.Policy.Deterministic ? "deterministic" : "nondeterministic");

    private static string HashParts(params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];

        foreach (var part in parts)
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, bytes.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
