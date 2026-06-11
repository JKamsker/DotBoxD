namespace SafeIR.Compiler;

using System.Security.Cryptography;
using System.Text;
using SafeIR;
using SafeIR.Verifier;

public static class CacheKeyBuilder
{
    public const string CompilerVersion = "safe-ir-compiler-4";
    public const string RuntimeFacadeHash = "safe-ir-runtime-facade-4";
    public const string LanguageVersion = "1.0.0";
    public const string TargetFramework = "net10.0";

    public static string Build(ExecutionPlan plan, VerificationPolicy policy, bool optimize)
    {
        var parts = new[] {
            "safe-ir-cache-v1",
            plan.ModuleHash,
            LanguageVersion,
            CompilerVersion,
            policy.VerifierVersion,
            RuntimeFacadeHash,
            plan.BindingManifestHash,
            plan.PolicyHash,
            TargetFramework,
            optimize ? "opt" : "boxed-values",
            plan.Policy.Deterministic ? "deterministic" : "nondeterministic"
        };

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
    }
}
