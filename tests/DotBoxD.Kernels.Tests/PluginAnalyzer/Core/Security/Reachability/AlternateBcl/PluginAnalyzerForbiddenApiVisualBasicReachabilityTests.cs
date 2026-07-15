using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiVisualBasicReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "Microsoft.VisualBasic.Interaction.Shell(\"plugin-command\");",
        "Microsoft.VisualBasic.Interaction")]
    [InlineData(
        "_ = System.IO.File.ReadAllText(\"/x\");",
        "System.IO.File")]
    public async Task Reports_alternate_bcl_host_apis_reached_from_event_kernel(
        string statement,
        string expectedForbiddenApi)
    {
        var source = Source(statement);

        var diagnostics = await AnalyzeAsync(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedForbiddenApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string statement)
        => $$"""
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("visual-basic-host-api")]
                public sealed class VisualBasicHostApiKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{statement}}
                        return true;
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
            "DotBoxDPluginAnalyzerVisualBasicReachabilityTest",
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
