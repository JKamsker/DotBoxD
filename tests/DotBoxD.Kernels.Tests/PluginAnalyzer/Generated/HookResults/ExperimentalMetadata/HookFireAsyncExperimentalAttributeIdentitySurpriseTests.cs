using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncExperimentalAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_fire_async_extensions_ignore_aliased_lookalike_experimental_attributes()
    {
        var foreignExperimentalAttribute = PluginServerGenerationTestDriver.CompileReference(
            """
            namespace System.Diagnostics.CodeAnalysis;

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ExperimentalAttribute(string diagnosticId) : System.Attribute
            {
                public string DiagnosticId { get; } = diagnosticId;
            }
            """,
            "ForeignExperimentalAttributes")
            .WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignExperimentalAttributes"]));

        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            Source,
            foreignExperimentalAttribute);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var fireAsyncSource = Assert.Single(
            result.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.DoesNotContain(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"FOREIGN_HOOK_FIRE\")]",
            fireAsyncSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"REAL_HOOK_FIRE\")]",
            fireAsyncSource,
            StringComparison.Ordinal);
    }

    private const string Source = """
        extern alias ForeignExperimentalAttributes;

        using System.Diagnostics.CodeAnalysis;
        using DotBoxD.Abstractions;

        namespace Regression.Game;

        [ForeignExperimentalAttributes::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_HOOK_FIRE")]
        [Hook("combat.foreign", typeof(ForeignDamageResult))]
        public sealed record ForeignDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct ForeignDamageResult(bool Success, string? Reason, int Amount);

        [Experimental("REAL_HOOK_FIRE")]
        [Hook("combat.real", typeof(RealDamageResult))]
        public sealed record RealDamageContext(int Amount);

        [HookResult]
        public readonly partial record struct RealDamageResult(bool Success, string? Reason, int Amount);
        """;
}
