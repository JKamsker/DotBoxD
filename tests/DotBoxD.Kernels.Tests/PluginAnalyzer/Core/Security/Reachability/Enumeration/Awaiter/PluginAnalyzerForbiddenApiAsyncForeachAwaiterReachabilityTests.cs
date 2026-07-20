using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiAsyncForeachAwaiterReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_api_reached_through_async_foreach_awaiter_get_result()
    {
        const string source = """
            namespace Sample
            {
                using System;
                using System.Runtime.CompilerServices;
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

                    public BoolAwaitable MoveNextAsync() => new();

                    public ValueTask DisposeAsync() => default;
                }

                public sealed class BoolAwaitable
                {
                    public BoolAwaiter GetAwaiter() => new();
                }

                public sealed class BoolAwaiter : INotifyCompletion
                {
                    public bool IsCompleted => true;

                    public void OnCompleted(Action continuation) => continuation();

                    public bool GetResult()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return false;
                    }
                }

                [Plugin("async-foreach-awaiter-leak")]
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
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "await foreach (var item in new HelperAsyncEnumerable())");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_explicit_await_awaiter_get_result()
    {
        const string source = """
            namespace Sample
            {
                using System;
                using System.Runtime.CompilerServices;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class BoolAwaitable
                {
                    public BoolAwaiter GetAwaiter() => new();
                }

                public sealed class BoolAwaiter : INotifyCompletion
                {
                    public bool IsCompleted => true;

                    public void OnCompleted(Action continuation) => continuation();

                    public bool GetResult()
                    {
                        _ = System.IO.File.ReadAllText("/x");
                        return false;
                    }
                }

                [Plugin("explicit-awaiter-leak")]
                public sealed class ExplicitAwaitKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public async void Handle(string e, HookContext context)
                    {
                        await new BoolAwaitable();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "await new BoolAwaitable();");
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
            "DotBoxDPluginAnalyzerAsyncForeachAwaiterReachabilityTest",
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
