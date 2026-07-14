using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDeclarationSignatureTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static TheoryData<DeclarationSignatureCase> ForbiddenDeclarationSignatureCases { get; } = new()
    {
        new(
            "indexer parameter type",
            "public int this[System.IO.FileInfo info] => 0;"),
        new(
            "nested delegate return type",
            "public delegate System.IO.FileInfo? InfoFactory();"),
    };

    [Theory]
    [MemberData(nameof(ForbiddenDeclarationSignatureCases))]
    public async Task Reports_forbidden_host_type_in_declaration_signatures(
        DeclarationSignatureCase testCase)
    {
        var diagnostics = await AnalyzeAsync($$"""
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("declaration-signature-leak")]
                public sealed class DeclarationSignatureKernel : IEventKernel<string>
                {
                    {{testCase.MemberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.FileInfo", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_safe_indexer_declaration_signature()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Plugins;
                using DotBoxD.Abstractions;

                [Plugin("safe-indexer-declaration")]
                public sealed class SafeIndexerKernel : IEventKernel<string>
                {
                    public int this[int index] => index;

                    public bool ShouldHandle(string e, HookContext context) => this[0] == 0;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK001");
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
            "DotBoxDPluginAnalyzerForbiddenDeclarationSignatureTest",
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

    public sealed record DeclarationSignatureCase(string Name, string MemberDeclaration)
    {
        public override string ToString() => Name;
    }
}
