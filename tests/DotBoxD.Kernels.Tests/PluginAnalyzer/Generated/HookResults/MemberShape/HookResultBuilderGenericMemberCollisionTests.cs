using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderGenericMemberCollisionTests
{
    [Fact]
    public void Generic_author_builder_does_not_suppress_non_generic_ok_builder()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public static DamageResult Ok<T>() => new() { Success = true, Damage = 1 };
            }

            public static class DamageResultUsage
            {
                public static DamageResult Build() => DamageResult.Ok().WithDamage(5);
            }
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                !diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal));

        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.RunGenerator(source)
            .GeneratedTrees
            .Select(tree => tree.GetText().ToString()));

        Assert.Contains("public static DamageResult Ok()", generated, StringComparison.Ordinal);
    }
}
