using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Services.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncExperimentalContextSurpriseTests
{
    [Fact]
    public void Generated_fire_async_extension_preserves_or_rejects_experimental_hook_contexts()
    {
        var result = RunGenerator(Source);
        var rejection = result.Diagnostics
            .Where(IsFocusedExperimentalContextRejection)
            .ToArray();

        if (rejection.Length > 0)
        {
            Assert.DoesNotContain(result.GeneratedSources, ContainsFireAsyncExtensions);
            return;
        }

        var fireAsyncSource = Assert.Single(result.GeneratedSources, ContainsFireAsyncExtensions);
        Assert.DoesNotContain(result.Diagnostics, result.IsGeneratedFireAsyncExperimentalDiagnostic);
        Assert.Contains(
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_FIRE\", " +
            "Message = \"Generated FireAsync is experimental.\", " +
            "UrlFormat = \"https://example.test/diagnostics/{0}\")]\n" +
            "    public static global::System.Threading.Tasks.ValueTask<global::Regression.Game.DamageResult?> FireAsync(",
            NormalizeLineEndings(fireAsyncSource),
            StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, result.IsUserFireAsyncCallExperimentalDiagnostic);
    }

    private static bool IsFocusedExperimentalContextRejection(Diagnostic diagnostic)
        => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
           diagnostic.Severity == DiagnosticSeverity.Error &&
           diagnostic.GetMessage().Contains("Experimental", StringComparison.OrdinalIgnoreCase) &&
           diagnostic.GetMessage().Contains("DamageContext", StringComparison.Ordinal);

    private static bool ContainsFireAsyncExtensions(string source)
        => source.Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal);

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static GeneratorOutcome RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "UserSource.cs");
        var compilation = CSharpCompilation.Create(
            "ExperimentalFireAsyncContext",
            [syntaxTree],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(HookRegistry).Assembly.Location)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Select(static tree => tree.GetText().ToString())
            .ToArray();
        var fireAsyncTree = runResult.GeneratedTrees.SingleOrDefault(
            tree => ContainsFireAsyncExtensions(tree.GetText().ToString()));

        return new GeneratorOutcome(
            syntaxTree,
            fireAsyncTree,
            generatedSources,
            generatorDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(static reference => MetadataReference.CreateFromFile(reference));
    }

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private const string Source = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace Regression.Game;

        [Experimental("DBXEXP_FIRE", Message = "Generated FireAsync is experimental.", UrlFormat = "https://example.test/diagnostics/{0}")]
        [Hook("combat.damage", typeof(DamageResult))]
        public sealed record DamageContext(int Amount);

        [HookResult]
        public readonly partial record struct DamageResult(bool Success, string? Reason, int Amount);

        public static class Usage
        {
            public static async ValueTask<int> FireAsync(HookRegistry hooks)
            {
        #pragma warning disable DBXEXP_FIRE
                var context = new DamageContext(5);
        #pragma warning restore DBXEXP_FIRE
                var result = await hooks.FireAsync(context);
                return result?.Amount ?? 0;
            }
        }
        """;

    private sealed record GeneratorOutcome(
        SyntaxTree UserTree,
        SyntaxTree? FireAsyncTree,
        IReadOnlyList<string> GeneratedSources,
        IReadOnlyList<Diagnostic> Diagnostics)
    {
        public bool IsGeneratedFireAsyncExperimentalDiagnostic(Diagnostic diagnostic)
            => diagnostic.Id == "DBXEXP_FIRE" &&
               FireAsyncTree is not null &&
               ReferenceEquals(diagnostic.Location.SourceTree, FireAsyncTree);

        public bool IsUserFireAsyncCallExperimentalDiagnostic(Diagnostic diagnostic)
            => diagnostic.Id == "DBXEXP_FIRE" &&
               ReferenceEquals(diagnostic.Location.SourceTree, UserTree) &&
               diagnostic.Location.GetLineSpan().StartLinePosition.Line == 22;
    }
}
