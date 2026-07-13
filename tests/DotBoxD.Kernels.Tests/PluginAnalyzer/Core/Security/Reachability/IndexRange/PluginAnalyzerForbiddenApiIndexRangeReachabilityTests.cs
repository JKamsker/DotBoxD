using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiIndexRangeReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_direct_forbidden_api_control()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("direct-forbidden-control")]
                public sealed class DirectForbiddenKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "_ = System.IO.File.ReadAllText(\"/x\");");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_from_end_index_access()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public int Length => 1;

                    public int this[int index]
                    {
                        get
                        {
                            _ = System.IO.File.ReadAllText("/x");
                            return index;
                        }
                    }
                }

                [Plugin("from-end-index-leak")]
                public sealed class FromEndIndexKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new Helper()[^1] >= 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "return new Helper()[^1] >= 0;");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_range_slice_access()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public int Length => 2;

                    public Helper Slice(int start, int length)
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return this;
                    }
                }

                [Plugin("range-slice-leak")]
                public sealed class RangeSliceKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        return new Helper()[1..].Length >= 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            "return new Helper()[1..].Length >= 0;");
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
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerIndexRangeReachabilityTest",
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
