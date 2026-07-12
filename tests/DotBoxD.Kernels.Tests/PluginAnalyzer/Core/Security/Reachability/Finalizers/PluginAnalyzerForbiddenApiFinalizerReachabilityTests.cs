using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiFinalizerReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_finalizer_reached_through_helper_allocation()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper
                {
                    ~Helper() => _ = System.IO.File.ReadAllText("/x");

                    public bool Ok() => true;
                }

                [Plugin("finalizer-leak")]
                public sealed class FinalizerKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => new Helper().Ok();

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertNoCompilerErrors(diagnostics);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "public bool ShouldHandle(string e, HookContext context) => new Helper().Ok();");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_direct_helper_call_control()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper
                {
                    public bool Danger() => System.IO.File.ReadAllText("/x").Length > 0;
                }

                [Plugin("direct-helper-call-control")]
                public sealed class DirectHelperCallKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => new Helper().Danger();

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        AssertNoCompilerErrors(diagnostics);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "public bool ShouldHandle(string e, HookContext context) => new Helper().Danger();");
    }

    private static void AssertNoCompilerErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var compilerErrors = diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal) &&
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString());

        Assert.Empty(compilerErrors);
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
        var compilerDiagnostics = compilation.GetDiagnostics();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        var analyzerDiagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return compilerDiagnostics.AddRange(analyzerDiagnostics);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerFinalizerReachabilityTest",
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
