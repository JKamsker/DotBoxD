using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiMemoryCacheReachabilityTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Theory]
    [InlineData(
        "memory-cache-static-initializer",
        """
        private static readonly MemoryCache Cache = CreateCache();

        private static MemoryCache CreateCache()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            for (var i = 0; i < 128; i++)
            {
                cache.Set("entry-" + i, i);
            }

            return cache;
        }
        """,
        "Microsoft.Extensions.Caching.Memory")]
    [InlineData(
        "file-control",
        """
        private static readonly bool FileExists =
            System.IO.File.Exists("plugin.cache");
        """,
        "System.IO.File")]
    public async Task Reports_host_managed_cache_population_from_static_initializers(
        string pluginId,
        string staticMembers,
        string expectedApi)
    {
        var diagnostics = await AnalyzeAsync(Source(pluginId, staticMembers));

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
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
            "DotBoxDPluginAnalyzerMemoryCacheReachabilityTest",
            [syntaxTree],
            MetadataReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> MetadataReferences()
    {
        var paths = TrustedPlatformReferencePaths();
        var seenFileNames = new HashSet<string>(
            paths.Select(Path.GetFileName).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        foreach (var path in AspNetCoreTestReferences.AssemblyPaths("Microsoft.Extensions.*.dll"))
        {
            if (seenFileNames.Add(Path.GetFileName(path)))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    private static IEnumerable<string> TrustedPlatformReferencePaths()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static string Source(string pluginId, string staticMembers)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;
                using Microsoft.Extensions.Caching.Memory;

                [Plugin("{{pluginId}}")]
                public sealed class MemoryCacheKernel : IEventKernel<string>
                {
            {{Indent(staticMembers, 8)}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

    private static string Indent(string text, int spaces)
        => string.Join(
            Environment.NewLine,
            text.Split('\n').Select(line => new string(' ', spaces) + line.TrimEnd()));
}
