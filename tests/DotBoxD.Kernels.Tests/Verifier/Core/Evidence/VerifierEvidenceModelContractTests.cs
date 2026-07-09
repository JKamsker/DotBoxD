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

    [Fact]
    public void ArtifactManifest_constructs_with_valid_identity_fields()
    {
        var manifest = Manifest();

        Assert.Equal("cache-key", manifest.CacheKey);
        Assert.Equal("assembly-hash", manifest.AssemblyHash);
    }

    [Fact]
    public void ArtifactManifest_rejects_blank_constructor_identity_fields()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new ArtifactManifest(
                1,
                "   ",
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
                DateTimeOffset.UtcNow));

        Assert.Equal("CacheKey", exception.ParamName);
    }

    [Theory]
    [InlineData("CacheKey", "   ")]
    [InlineData("ModuleHash", "")]
    [InlineData("PlanHash", "   ")]
    [InlineData("PolicyHash", "\t")]
    [InlineData("BindingManifestHash", "")]
    [InlineData("RuntimeFacadeHash", "   ")]
    [InlineData("CompilerVersion", "")]
    [InlineData("TypeSystemVersion", "   ")]
    [InlineData("EffectAnalysisVersion", "\t")]
    [InlineData("VerifierVersion", "\t")]
    [InlineData("LanguageVersion", "")]
    [InlineData("TargetFramework", "   ")]
    [InlineData("AssemblyHash", "")]
    public void ArtifactManifest_rejects_blank_init_identity_fields(
        string fieldName,
        string value)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => WithIdentityField(fieldName, value));

        Assert.Equal(fieldName, exception.ParamName);
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

    private static ArtifactManifest WithIdentityField(string fieldName, string value)
        => fieldName switch
        {
            "CacheKey" => Manifest() with { CacheKey = value },
            "ModuleHash" => Manifest() with { ModuleHash = value },
            "PlanHash" => Manifest() with { PlanHash = value },
            "PolicyHash" => Manifest() with { PolicyHash = value },
            "BindingManifestHash" => Manifest() with { BindingManifestHash = value },
            "RuntimeFacadeHash" => Manifest() with { RuntimeFacadeHash = value },
            "CompilerVersion" => Manifest() with { CompilerVersion = value },
            "TypeSystemVersion" => Manifest() with { TypeSystemVersion = value },
            "EffectAnalysisVersion" => Manifest() with { EffectAnalysisVersion = value },
            "VerifierVersion" => Manifest() with { VerifierVersion = value },
            "LanguageVersion" => Manifest() with { LanguageVersion = value },
            "TargetFramework" => Manifest() with { TargetFramework = value },
            "AssemblyHash" => Manifest() with { AssemblyHash = value },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null)
        };
}
