using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiObjectPoolReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "default object pool static retention",
        """
        private static readonly ObjectPool<Retained> Pool = CreatePool();

        private static ObjectPool<Retained> CreatePool()
        {
            var pool = new DefaultObjectPool<Retained>(
                new DefaultPooledObjectPolicy<Retained>(),
                maximumRetained: 100_000);

            for (var i = 0; i < 100_000; i++)
            {
                pool.Return(new Retained());
            }

            return pool;
        }

        private sealed class Retained
        {
        }
        """,
        "Microsoft.Extensions.ObjectPool")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-pool.txt\");",
        "System.IO.File")]
    public async Task Reports_forbidden_object_pool_retention_in_static_initializers(
        string testCase,
        string staticMember,
        string expectedForbiddenApi)
    {
        var source = Source(staticMember);

        var diagnostics = await AnalyzeAsync(source);

        Assert.True(
            diagnostics.Any(
                diagnostic => diagnostic.Id == "DBXK001" &&
                    diagnostic.GetMessage().Contains(expectedForbiddenApi, StringComparison.Ordinal)),
            $"{testCase}: {string.Join(Environment.NewLine, diagnostics.Select(d => d.GetMessage()))}");
    }

    private static string Source(string staticMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.ObjectPool;

                [Plugin("object-pool-host-state")]
                public sealed class ObjectPoolKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => e.Length >= 0;

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
            "DotBoxDPluginAnalyzerObjectPoolReachabilityTest",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(
                    AspNetCoreTestReferences.FindAssembly("Microsoft.Extensions.ObjectPool.dll")))
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
