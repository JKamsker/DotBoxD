using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncExtensionTypeCollisionTests
{
    [Fact]
    public void FireAsync_extension_does_not_leak_duplicate_type_error_when_user_declares_runtime_helper_type()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace DotBoxD.Plugins.Runtime
            {
                public static class HookRegistryFireAsyncExtensions
                {
                }
            }

            namespace Sample
            {
                [Hook("collision.damage", typeof(DamageResult))]
                public sealed record DamageCtx(int Damage);

                [HookResult]
                public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            }
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, IsGeneratedFireAsyncDuplicateTypeError);
        Assert.True(
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error) ||
            diagnostics.Any(IsFocusedHookDiagnostic),
            "Expected either non-colliding FireAsync generation or a focused DBXK diagnostic, but got:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static bool IsGeneratedFireAsyncDuplicateTypeError(Diagnostic diagnostic)
        => diagnostic.Id == "CS0101" &&
           diagnostic.Location.SourceTree?.FilePath.EndsWith(
               "DotBoxDHookFireAsyncExtensions.g.cs",
               StringComparison.Ordinal) == true;

    private static bool IsFocusedHookDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
           diagnostic.GetMessage().Contains("FireAsync", StringComparison.Ordinal);
}
