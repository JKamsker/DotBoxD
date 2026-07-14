using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumeratorReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Does_not_report_benign_async_foreach_enumerator_value_task_shape()
    {
        const string source = """
            namespace Sample
            {
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperAsyncEnumerable
                {
                    public HelperAsyncEnumerator GetAsyncEnumerator() => new();
                }

                public sealed class HelperAsyncEnumerator
                {
                    public int Current => 0;

                    public ValueTask<bool> MoveNextAsync() => new(false);

                    public ValueTask DisposeAsync() => default;
                }

                [Plugin("benign-async-foreach-enumerator")]
                public sealed class AsyncForeachKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public async void Handle(string e, HookContext context)
                    {
                        await foreach (var item in new HelperAsyncEnumerable())
                        {
                            _ = item;
                        }
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        Assert.Empty(diagnostics.Where(d => d.Id == "DBXK001"));
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_foreach_enumerator()
    {
        const string source = """
            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperEnumerable
                {
                    public HelperEnumerator GetEnumerator() => new();
                }

                public sealed class HelperEnumerator : System.IDisposable
                {
                    public int Current => 0;

                    public bool MoveNext()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return false;
                    }

                    public void Dispose() { }
                }

                [Plugin("foreach-enumerator-leak")]
                public sealed class ForeachKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context)
                    {
                        foreach (var item in new HelperEnumerable())
                        {
                            return item == 1;
                        }

                        return false;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "foreach (var item in new HelperEnumerable())");
    }

    [Theory]
    [InlineData(
        """
        get
        {
            _ = System.IO.File.ReadAllText("/x");
            return 0;
        }
        """,
        "return new(false);",
        "return default;")]
    [InlineData(
        "get { return 0; }",
        """
        _ = System.IO.File.ReadAllText("/x");
        return new(false);
        """,
        "return default;")]
    [InlineData(
        "get { return 0; }",
        "return new(false);",
        """
        _ = System.IO.File.ReadAllText("/x");
        return default;
        """)]
    public async Task Reports_forbidden_api_reached_through_async_foreach_enumerator_member(
        string currentBody,
        string moveNextBody,
        string disposeBody)
    {
        var source = $$"""
            namespace Sample
            {
                using System.Threading.Tasks;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class HelperAsyncEnumerable
                {
                    public HelperAsyncEnumerator GetAsyncEnumerator() => new();
                }

                public sealed class HelperAsyncEnumerator
                {
                    public int Current
                    {
                        {{currentBody}}
                    }

                    public ValueTask<bool> MoveNextAsync()
                    {
                        {{moveNextBody}}
                    }

                    public ValueTask DisposeAsync()
                    {
                        {{disposeBody}}
                    }
                }

                [Plugin("async-foreach-enumerator-leak")]
                public sealed class AsyncForeachKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public async void Handle(string e, HookContext context)
                    {
                        await foreach (var item in new HelperAsyncEnumerable())
                        {
                            _ = item;
                        }
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        var diagnostic = Assert.Single(
            diagnostics.Where(d =>
                d.Id == "DBXK001" &&
                d.GetMessage().Contains("System.IO.File", StringComparison.Ordinal)));

        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        var actualLine = source.Split('\n')[position.Line].Trim();
        Assert.Equal("await foreach (var item in new HelperAsyncEnumerable())", actualLine);
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
        var compilerErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compilerErrors);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DotBoxD.Plugins.Analyzer.Analysis.PluginAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return CSharpCompilation.Create(
            "DotBoxDPluginAnalyzerEnumeratorReachabilityTest",
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
