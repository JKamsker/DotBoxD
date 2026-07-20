using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiAwaitUsingAwaiterReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Fact]
    public async Task Reports_forbidden_api_reached_through_await_using_awaiter_get_result()
    {
        var source = $$"""
            namespace Sample
            {
                using System;
                using System.Runtime.CompilerServices;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public sealed class Helper
                {
                    public DisposeAwaitable DisposeAsync() => new();
                }

                {{DisposeAwaitableSource}}

                [Plugin("await-using-awaiter-leak")]
                public sealed class AwaitUsingAwaiterKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public async void Handle(string e, HookContext context)
                    {
                        await using var helper = new Helper();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "await using var helper = new Helper();");
    }

    [Fact]
    public async Task Reports_forbidden_api_reached_through_explicit_dispose_awaitable()
    {
        var source = $$"""
            namespace Sample
            {
                using System;
                using System.Runtime.CompilerServices;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                {{DisposeAwaitableSource}}

                [Plugin("explicit-dispose-awaitable-leak")]
                public sealed class ExplicitDisposeAwaitableKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public async void Handle(string e, HookContext context)
                    {
                        await new DisposeAwaitable();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(source);
        AssertSingleForbiddenDiagnosticAt(source, diagnostics, "await new DisposeAwaitable();");
    }

    private const string DisposeAwaitableSource = """
        public sealed class DisposeAwaitable
        {
            public DisposeAwaiter GetAwaiter() => new();
        }

        public sealed class DisposeAwaiter : INotifyCompletion
        {
            public bool IsCompleted => true;

            public void OnCompleted(Action continuation) => continuation();

            public void GetResult() => _ = System.IO.File.ReadAllText("/x");
        }
        """;

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
            "DotBoxDPluginAnalyzerAwaitUsingAwaiterReachabilityTest",
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
