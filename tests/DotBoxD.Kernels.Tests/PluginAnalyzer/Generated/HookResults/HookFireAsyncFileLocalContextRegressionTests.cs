using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncFileLocalContextRegressionTests
{
    [Fact]
    public void FireAsync_extension_rejects_file_local_hook_contexts_before_emitting_sources()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Regression.Game;

            [Hook("combat.damage", typeof(DamageResult))]
            file sealed record FileDamageContext(int Amount);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.GetMessage().Contains("FileDamageContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "CS0234" &&
                diagnostic.GetMessage().Contains("FileDamageContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void FireAsync_extension_rejects_hook_contexts_nested_in_file_local_types()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Regression.Game;

            file static class FileContextContainer
            {
                [Hook("combat.damage", typeof(DamageResult))]
                public sealed record NestedDamageContext(int Amount);
            }

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.GetMessage().Contains("NestedDamageContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "CS0234" &&
                diagnostic.GetMessage().Contains("NestedDamageContext", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }
}
