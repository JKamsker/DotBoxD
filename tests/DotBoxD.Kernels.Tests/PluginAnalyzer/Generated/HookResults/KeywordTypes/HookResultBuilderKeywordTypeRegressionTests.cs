using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderKeywordTypeRegressionTests
{
    [Fact]
    public void HookResult_builders_escape_keyword_result_type_names()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct @event(bool Success, string? Reason, int Damage);

            public static class HookResultFactory
            {
                public static @event Build() => @event.Ok().WithDamage(3);
            }
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                !diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => string.Equals(diagnostic.Id, "DBXK100", StringComparison.Ordinal));

        var generated = string.Join(
            "\n",
            PluginAnalyzerGeneratedPackageFactory.RunGenerator(source)
                .GeneratedTrees
                .Select(tree => tree.GetText().ToString()));

        Assert.Contains("public readonly partial record struct @event", generated, StringComparison.Ordinal);
        Assert.Contains("public static @event Ok()", generated, StringComparison.Ordinal);
        Assert.Contains("public static @event Reject(string? reason = null)", generated, StringComparison.Ordinal);
        Assert.Contains("public @event WithDamage(int damage)", generated, StringComparison.Ordinal);
    }
}
