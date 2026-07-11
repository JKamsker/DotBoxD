using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.Evidence;

public sealed class VerifierEvidenceModelContractTests
{
    [Fact]
    public void VerificationDiagnostic_rejects_null_contract_fields()
    {
        var code = Assert.Throws<ArgumentNullException>(
            () => new VerificationDiagnostic(null!, "message"));
        var message = Assert.Throws<ArgumentNullException>(
            () => new VerificationDiagnostic("V-TEST", null!));

        Assert.Equal("Code", code.ParamName);
        Assert.Equal("Message", message.ParamName);
    }

    [Theory]
    [InlineData("", "message", "Code")]
    [InlineData("   ", "message", "Code")]
    [InlineData("V-TEST", "", "Message")]
    [InlineData("V-TEST", "   ", "Message")]
    public void VerificationDiagnostic_rejects_blank_contract_fields(
        string code,
        string message,
        string parameterName)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new VerificationDiagnostic(code, message));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void VerificationResult_rejects_null_diagnostic_entries()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new VerificationResult(
                false,
                [(VerificationDiagnostic)null!],
                "assembly-hash",
                "verifier-version",
                DateTimeOffset.UtcNow));

        Assert.Contains("Diagnostics", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationResult_rejects_success_with_diagnostics()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new VerificationResult(
                true,
                [new VerificationDiagnostic("V-TEST", "failure")],
                "assembly-hash",
                "verifier-version",
                DateTimeOffset.UtcNow));

        Assert.Contains("Diagnostics", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactManifest_rejects_null_optimization_flag_entries()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => Manifest() with { OptimizationFlags = ["boxed-values", null!] });

        Assert.Contains("OptimizationFlags", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationManifestIdentity_rejects_null_manifest_factory_argument()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => VerificationManifestIdentity.FromManifest(null!));

        Assert.Equal("manifest", exception.ParamName);
    }

    [Fact]
    public void ArtifactManifest_rejects_null_identity_fields()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => Manifest() with { CacheKey = null! });

        Assert.Equal("CacheKey", exception.ParamName);
    }

    private static ArtifactManifest Manifest()
        => new(
            1,
            "cache-key",
            "module-hash",
            "plan-hash",
            "policy-hash",
            "binding-hash",
            "runtime-hash",
            "compiler-version",
            "type-system-version",
            "effect-analysis-version",
            "verifier-version",
            "1.0.0",
            "net10.0",
            ["boxed-values"],
            "assembly-hash",
            DateTimeOffset.UtcNow);
}
