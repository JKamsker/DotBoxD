using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncPrivateNestedContextRegressionTests
{
    [Fact]
    public void FireAsync_extension_rejects_private_nested_hook_contexts_before_emitting_sources()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Hooks;

            public static class DamageHooks
            {
                [Hook("damage.private", typeof(DamageResult))]
                private sealed record DamageContext(int Amount);
            }

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, IsGeneratedInaccessibleContextDiagnostic);
        if (diagnostics.Any(IsFocusedAccessibilityDiagnostic))
        {
            Assert.DoesNotContain(result.GeneratedTrees, ContainsFireAsyncExtensions);
            return;
        }

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static bool IsGeneratedInaccessibleContextDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id == "CS0122" &&
           diagnostic.Location.SourceTree?.FilePath.EndsWith(
               "DotBoxDHookFireAsyncExtensions.g.cs",
               StringComparison.Ordinal) == true &&
           diagnostic.GetMessage().Contains("DamageHooks.DamageContext", StringComparison.Ordinal);

    private static bool IsFocusedAccessibilityDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
           diagnostic.Severity == DiagnosticSeverity.Error &&
           diagnostic.GetMessage().Contains("DamageContext", StringComparison.Ordinal) &&
           (diagnostic.GetMessage().Contains("private", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.GetMessage().Contains("access", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsFireAsyncExtensions(SyntaxTree tree)
        => tree.FilePath.EndsWith("DotBoxDHookFireAsyncExtensions.g.cs", StringComparison.Ordinal);
}
