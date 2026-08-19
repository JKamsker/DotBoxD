using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class HookResultAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_hook_results_ignore_aliased_lookalike_hook_result_attributes()
    {
        var foreignHookResultAttribute = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace DotBoxD.Abstractions;

            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public sealed class HookResultAttribute : System.Attribute
            {
            }
            """,
            "ForeignHookResultAttributes")
            .WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignHookResultAttributes"]));

        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            Source,
            foreignHookResultAttribute);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("ForeignMarkedResult Ok()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ForeignMarkedResult Reject(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ForeignMarkedResult WithDamage(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("ForeignMarkedResult : global::DotBoxD.Abstractions.IHookResult", generated, StringComparison.Ordinal);
        Assert.Contains("GenuineResult Ok()", generated, StringComparison.Ordinal);
        Assert.Contains("GenuineResult Reject(", generated, StringComparison.Ordinal);
        Assert.Contains("GenuineResult WithDamage(", generated, StringComparison.Ordinal);
        Assert.Contains("GenuineResult : global::DotBoxD.Abstractions.IHookResult", generated, StringComparison.Ordinal);
    }

    private const string Source = """
        #nullable enable
        extern alias ForeignHookResultAttributes;

        using DotBoxD.Abstractions;

        namespace Regression;

        [ForeignHookResultAttributes::DotBoxD.Abstractions.HookResult]
        public readonly partial record struct ForeignMarkedResult(bool Success, string? Reason, int Damage);

        [HookResult]
        public readonly partial record struct GenuineResult(bool Success, string? Reason, int Damage);
        """;
}
