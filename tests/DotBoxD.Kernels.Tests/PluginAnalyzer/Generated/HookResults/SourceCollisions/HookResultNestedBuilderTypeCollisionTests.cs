using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultNestedBuilderTypeCollisionTests
{
    [Fact]
    public void Nested_builder_name_type_does_not_leak_duplicate_member_error()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public sealed class Ok;
            }
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, IsGeneratedOkDuplicateMemberError);
        Assert.True(
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error) ||
            diagnostics.Any(IsFocusedHookResultDiagnostic),
            "Expected either non-colliding HookResult generation or a focused DBXK diagnostic, but got:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static bool IsGeneratedOkDuplicateMemberError(Diagnostic diagnostic)
        => diagnostic.Id == "CS0102" &&
           diagnostic.Location.SourceTree?.FilePath.EndsWith(
               "DamageResult.HookResultBuilders.g.cs",
               StringComparison.Ordinal) == true;

    private static bool IsFocusedHookResultDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
           diagnostic.GetMessage().Contains("HookResult", StringComparison.Ordinal);
}
