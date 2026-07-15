using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiTransactionReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private static readonly Lazy<ImmutableArray<MetadataReference>> SharedReferences = new(CreateSharedReferences);

    [Fact]
    public async Task Reports_ambient_transaction_mutation_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            System.Transactions.Transaction.Current =
                new System.Transactions.CommittableTransaction();
            return true;
            """));

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "DBXK001");
        Assert.Contains("System.Transactions", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_file_api_control_from_event_kernel()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            System.IO.File.ReadAllText(e);
            return true;
            """));

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "DBXK001");
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_benign_event_kernel_control()
    {
        var diagnostics = await AnalyzeAsync(Source("""
            var normalized = e.Trim();
            return normalized.Length > 0;
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("transaction-host-state")]
                public sealed class TransactionKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerTransactionReachabilityTest",
            [syntaxTree],
            SharedReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> CreateSharedReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select<string, MetadataReference>(reference => MetadataReference.CreateFromFile(reference))
            .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
            .ToImmutableArray();
    }
}
