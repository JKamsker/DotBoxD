using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiBackgroundWorkerReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_backgroundworker_scheduling_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            var worker = new BackgroundWorker();
            worker.DoWork += (_, _) => _ = e.Length;
            worker.RunWorkerAsync();
            """));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.ComponentModel.BackgroundWorker", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_file_positive_control_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            _ = System.IO.File.ReadAllText("/x");
            """));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_benign_componentmodel_type_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            var args = new CancelEventArgs(cancel: false);
            _ = args.Cancel;
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string Source(string statement)
        => $$"""
            namespace Sample
            {
                using System.ComponentModel;
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("background-worker-scheduling")]
                public sealed class BackgroundWorkerKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return e.Length > 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

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
            "DotBoxDPluginAnalyzerBackgroundWorkerReachabilityTest",
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
