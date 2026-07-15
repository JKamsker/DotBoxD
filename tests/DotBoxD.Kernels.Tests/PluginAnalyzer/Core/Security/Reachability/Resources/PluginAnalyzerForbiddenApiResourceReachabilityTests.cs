using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiResourceReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_resource_reader_and_writer_path_constructors_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("resource-file-leak")]
                public sealed class ResourceKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        using var reader = new System.Resources.ResourceReader("plugin.resources");
                        using var writer = new System.Resources.ResourceWriter("plugin.resources");
                        return reader is not null && writer is not null;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var forbiddenDiagnostics = diagnostics.Where(d => d.Id == "DBXK001").ToArray();
        Assert.Equal(2, forbiddenDiagnostics.Length);
        Assert.Contains(
            forbiddenDiagnostics,
            diagnostic => diagnostic.GetMessage().Contains("System.Resources.ResourceReader", StringComparison.Ordinal));
        Assert.Contains(
            forbiddenDiagnostics,
            diagnostic => diagnostic.GetMessage().Contains("System.Resources.ResourceWriter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reports_file_positive_control_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("file-control")]
                public sealed class FileKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return System.IO.File.ReadAllText("plugin.txt").Length > 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, "Source.cs");
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerResourceReachabilityTest",
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
