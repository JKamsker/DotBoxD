using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiCollectionExpressionExtensionSpreadReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Does_not_report_for_benign_extension_enumerator_spread()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperEnumerable
                {
                }

                public static class HelperEnumerableExtensions
                {
                    public static HelperEnumerator GetEnumerator(this HelperEnumerable enumerable) => new();
                }

                public sealed class HelperEnumerator
                {
                    public int Current => 42;

                    public bool MoveNext() => false;
                }

                [Plugin("benign-extension-spread")]
                public sealed class BenignExtensionSpreadKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        int[] values = [.. new HelperEnumerable()];
                        return values.Length == 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_extension_enumerator_collection_spread()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperEnumerable
                {
                }

                public static class HelperEnumerableExtensions
                {
                    public static HelperEnumerator GetEnumerator(this HelperEnumerable enumerable) => new();
                }

                public sealed class HelperEnumerator
                {
                    public int Current => 42;

                    public bool MoveNext()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return false;
                    }
                }

                [Plugin("extension-spread-leak")]
                public sealed class ExtensionSpreadKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        int[] values = [.. new HelperEnumerable()];
                        return values.Length == 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            ".. new HelperEnumerable()");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_compiler_selected_extension_enumerator()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperEnumerable
                {
                }

                public static class HelperEnumerableExtensions
                {
                    public static BenignEnumerator GetEnumerator(this object enumerable) => new();

                    public static DangerousEnumerator GetEnumerator(this HelperEnumerable enumerable) => new();
                }

                public sealed class BenignEnumerator
                {
                    public int Current => 42;

                    public bool MoveNext() => false;
                }

                public sealed class DangerousEnumerator
                {
                    public int Current => 42;

                    public bool MoveNext()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return false;
                    }
                }

                [Plugin("compiler-selected-extension-spread")]
                public sealed class CompilerSelectedExtensionSpreadKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        int[] values = [.. new HelperEnumerable()];
                        return values.Length == 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(
            source,
            diagnostics,
            ".. new HelperEnumerable()");
    }

    private static void AssertSingleForbiddenDiagnosticAt(
        string source,
        ImmutableArray<Diagnostic> diagnostics,
        string expectedSpan)
    {
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.IO.File", diagnostic.GetMessage(), StringComparison.Ordinal);

        var actualSpan = diagnostic.Location.SourceSpan;
        Assert.Equal(expectedSpan, source.AsSpan(actualSpan.Start, actualSpan.Length).ToString());
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CreateCompilation(source);
        var compilerDiagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerDiagnostics);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerCollectionExpressionExtensionSpreadReachabilityTest",
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
