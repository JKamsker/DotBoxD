using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDisposalReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_api_reached_through_using_dispose()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper : System.IDisposable
                {
                    public void Dispose() => _ = System.IO.File.ReadAllText("/x");
                }

                [Plugin("using-dispose-leak")]
                public sealed class UsingDisposeKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        using var helper = new Helper();
                        return helper is not null;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "using var helper = new Helper();");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_await_using_dispose_async()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                public sealed class Helper : System.IAsyncDisposable
                {
                    public System.Threading.Tasks.ValueTask DisposeAsync()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return System.Threading.Tasks.ValueTask.CompletedTask;
                    }
                }

                [Plugin("await-using-dispose-leak")]
                public sealed class AwaitUsingDisposeKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        ShouldHandleAsync(e, context);
                        return true;
                    }

                    private static async void ShouldHandleAsync(string e, HookContext context)
                    {
                        await using var helper = new Helper();
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "await using var helper = new Helper();");
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
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerDisposalReachabilityTest",
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
