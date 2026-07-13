using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenUnsafeCodeTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_direct_forbidden_call_when_unsafe_compilation_is_enabled()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("unsafe-direct-forbidden-control")]
                public sealed class DirectForbiddenKernel : IEventKernel<string>
                {
                    public unsafe bool ShouldHandle(string e, HookContext context)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleDiagnosticAt(source, diagnostics, "DBXK001", "_ = System.IO.File.ReadAllText(\"/x\");");
    }

    [Fact]
    public async Task Reports_unsafe_pointer_stackalloc_in_event_kernel()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("unsafe-pointer-stackalloc")]
                public sealed class UnsafePointerKernel : IEventKernel<string>
                {
                    public unsafe bool ShouldHandle(string e, HookContext context)
                    {
                        int* values = stackalloc int[1];
                        values[0] = e.Length;
                        return values[0] >= 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = AssertSingleDiagnosticAt(source, diagnostics, "DBXK", "int* values = stackalloc int[1];");
        Assert.Contains("unsafe", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    private static Diagnostic AssertSingleDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedIdPrefix,
        string expectedLine)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id.StartsWith(expectedIdPrefix, StringComparison.Ordinal)));

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal(expectedLine, actualLine);

        return diagnostic;
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
            "DotBoxDPluginAnalyzerForbiddenUnsafeCodeTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
