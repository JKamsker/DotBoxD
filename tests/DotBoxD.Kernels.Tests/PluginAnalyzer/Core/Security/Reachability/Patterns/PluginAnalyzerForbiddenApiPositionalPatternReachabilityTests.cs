using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPositionalPatternReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_api_reached_through_positional_pattern_deconstruct()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public void Deconstruct(out int first, out int second)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        first = 1;
                        second = 2;
                    }
                }

                [Plugin("positional-pattern-deconstruct-leak")]
                public sealed class PositionalPatternKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new Helper() is (var first, _);
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "return new Helper() is (var first, _);");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_switch_positional_pattern_deconstruct()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public void Deconstruct(out int first, out int second)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        first = 1;
                        second = 2;
                    }
                }

                [Plugin("switch-positional-pattern-deconstruct-leak")]
                public sealed class SwitchPositionalPatternKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new Helper() switch
                        {
                            (var first, _) => first > 0,
                            _ => false,
                        };
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "(var first, _) => first > 0,");
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedLine)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal(expectedLine, actualLine);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerPositionalPatternReachabilityTest",
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
