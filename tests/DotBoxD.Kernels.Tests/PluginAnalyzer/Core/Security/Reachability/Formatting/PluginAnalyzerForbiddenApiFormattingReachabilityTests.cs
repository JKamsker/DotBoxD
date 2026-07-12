using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiFormattingReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData("$\"{new Helper()}\".Length > 0", "return $\"{new Helper()}\".Length > 0;")]
    [InlineData("(\"\" + new Helper()).Length > 0", "return (\"\" + new Helper()).Length > 0;")]
    public async Task Reports_forbidden_tostring_reached_through_implicit_formatting(
        string expression,
        string expectedLine)
    {
        var diagnostics = await AnalyzeAsync(CreateSource(expression));
        AssertSingleForbiddenDiagnosticAt(diagnostics, expectedLine);
    }

    [Fact]
    public async Task Reports_forbidden_tostring_reached_through_explicit_call()
    {
        var diagnostics = await AnalyzeAsync(CreateSource("new Helper().ToString().Length > 0"));
        AssertSingleForbiddenDiagnosticAt(diagnostics, "return new Helper().ToString().Length > 0;");
    }

    private static string CreateSource(string expression)
    {
        return $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public override string ToString()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return string.Empty;
                    }
                }

                [Plugin("formatting-leak")]
                public sealed class FormattingKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return {{expression}};
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        ImmutableArray<Diagnostic> diagnostics,
        string expectedLine)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var actualLine = diagnostic.Location.GetLineSpan().Path;
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var sourceLine = diagnostic.Location.SourceTree?.ToString().Split('\n')[position.Line].Trim();

        Assert.Equal("Source.cs", actualLine);
        Assert.Equal(expectedLine, sourceLine);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Source.cs");
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerFormattingReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
