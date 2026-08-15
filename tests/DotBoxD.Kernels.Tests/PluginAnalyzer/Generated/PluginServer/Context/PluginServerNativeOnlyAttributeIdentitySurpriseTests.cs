using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    [Fact]
    public async Task Foreign_NativeOnly_attribute_does_not_report_DBXK116()
    {
        var foreignNativeOnlyAttribute = CompileForeignNativeOnlyAttribute();

        var diagnostics = await AnalyzerDiagnosticsAsync(
            """
            extern alias ForeignNativeOnly;

            namespace Sample;

            public static class Helper
            {
                [ForeignNativeOnly::DotBoxD.Abstractions.NativeOnly]
                public static string Native() => "x";
            }
            """,
            foreignNativeOnlyAttribute);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK116");
    }

    [Fact]
    public async Task DotBoxD_NativeOnly_attribute_outside_declared_context_reports_DBXK116()
    {
        var diagnostics = await AnalyzerDiagnosticsAsync(
            """
            using DotBoxD.Abstractions;

            namespace Sample;

            public static class Helper
            {
                [NativeOnly]
                public static string Native() => "x";
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK116");
    }

    private static MetadataReference CompileForeignNativeOnlyAttribute()
    {
        const string source = """
            namespace DotBoxD.Abstractions;

            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Property)]
            public sealed class NativeOnlyAttribute : System.Attribute { }
            """;
        var compilation = CSharpCompilation.Create(
            "ForeignNativeOnly_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);
        Assert.Empty(emitResult.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return MetadataReference.CreateFromImage(assemblyStream.ToArray())
            .WithAliases(ImmutableArray.Create("ForeignNativeOnly"));
    }
}
